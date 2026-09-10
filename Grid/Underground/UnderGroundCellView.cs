using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// UndergroundCellView — Vue complète d'une case de grotte.
///
/// Gère : sprites sol/mur, nombres adjacents, spawn ennemis,
///        drapeaux, portail sortie, révélation cascade.
///
/// Crée dynamiquement les SpriteRenderers enfants nécessaires
/// (pas besoin de prefab — tout est configuré par code).
/// </summary>
public class UndergroundCellView : MonoBehaviour, IPointerClickHandler
{
    // ── Renderers (créés dynamiquement) ───────────────────────────────────────
    private SpriteRenderer _bgRenderer;      // fond (sol/mur)
    private SpriteRenderer _numberRenderer;  // indicateur chiffre adjacent
    private SpriteRenderer _iconRenderer;    // icône contenu (ennemi/trésor/piège)

    // ── Données ───────────────────────────────────────────────────────────────
    private Cell _cell;
    private CaveSpriteSet _spriteSet;
    private CaveLayout _layout;
    private Sprite _autoTiledSprite;
    private Sprite _hiddenSprite;
    private bool _isFloor;
    private bool _isWallContour;
    private bool _isExitPortal;
    private bool _initialized;
    private EnemyInstance _enemyInstance;
    private bool _enemySpawned; // true = ennemi déjà créé, ne PAS respawn

    // Sprites nombres viennent de CaveSpriteSet (assignés dans Inspector)

    // =========================================================================
    // INITIALISATION
    // =========================================================================

    public void InitializeCave(Cell cell, CaveLayout layout, CaveSpriteSet spriteSet)
    {
        _cell = cell;
        _layout = layout;
        _spriteSet = spriteSet;

        // Créer les renderers enfants
        SetupRenderers();

        _isFloor = layout.IsFloor(cell.X, cell.Y);
        _isWallContour = CaveAutoTiler.IsWallContour(layout, cell.X, cell.Y);
        _autoTiledSprite = CaveAutoTiler.GetSprite(layout, cell.X, cell.Y, spriteSet);
        _hiddenSprite = spriteSet?.hiddenRock;

        var ug = UndergroundGrid.Instance;
        _isExitPortal = ug != null && ug.IsExitPortal(cell.X, cell.Y);

        // Murs → toujours révélés
        if (_isWallContour || !_isFloor)
            cell.ForceReveal();



        ApplySprite();
        _initialized = true;
    }

    private void SetupRenderers()
    {
        // Background (principal)
        _bgRenderer = GetComponent<SpriteRenderer>();
        if (_bgRenderer == null)
            _bgRenderer = gameObject.AddComponent<SpriteRenderer>();

        // Nombre (enfant)
        var numGO = new GameObject("Num");
        numGO.transform.SetParent(transform);
        numGO.transform.localPosition = new Vector3(0, 0, -0.01f);
        numGO.transform.localScale = Vector3.one;
        _numberRenderer = numGO.AddComponent<SpriteRenderer>();
        _numberRenderer.sortingLayerName = "CellContent";
        _numberRenderer.sortingOrder = _bgRenderer.sortingOrder + 2;
        _numberRenderer.enabled = false;

        // Icône contenu (enfant)
        var iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(transform);
        iconGO.transform.localPosition = new Vector3(0, 0, -0.02f);
        iconGO.transform.localScale = Vector3.one;
        _iconRenderer = iconGO.AddComponent<SpriteRenderer>();
        _iconRenderer.sortingLayerName = "CellContent";
        _iconRenderer.sortingOrder = _bgRenderer.sortingOrder + 3;
        _iconRenderer.enabled = false;
    }



    // =========================================================================
    // AFFICHAGE
    // =========================================================================

    public void ApplySprite()
    {
        if (_bgRenderer == null || _cell == null) return;

        HideNumber();
        HideIcon();

        if (_cell.IsRevealed)
            ShowRevealed();
        else
            ShowHidden();
    }

    private void ShowHidden()
    {
        if (_isWallContour)
            _bgRenderer.sprite = _autoTiledSprite ?? _hiddenSprite;
        else
            _bgRenderer.sprite = _hiddenSprite;
    }

    private void ShowRevealed()
    {
        if (!_isFloor)
        {
            _bgRenderer.sprite = _autoTiledSprite ?? _spriteSet?.wallFull ?? _hiddenSprite;
            return;
        }

        // Sol révélé — fond
        Sprite ground = null;
        var gvs = GroundVariantSystem.Instance;
        if (gvs != null && _layout != null)
            ground = gvs.GetCaveGroundSprite(_cell.X, _cell.Y, _layout.Width, _layout.Height);
        _bgRenderer.sprite = ground ?? _spriteSet?.floor;

        // Contenu de la case
        int contentInt = (int)_cell.Content;
        bool isEnemy = contentInt >= 10 && contentInt < 20;
        bool isBoss = _cell.Content == CellContent.Enemy_Boss;

        if ((isEnemy || isBoss) && !_enemySpawned)
        {
            SpawnEnemyInstance(_cell.Content);
        }
        else if (_cell.Content == CellContent.Empty && _enemyInstance != null)
        {
            // Ennemi battu → ne pas respawn, nettoyer la référence
            _enemyInstance = null;
        }

        if (_cell.AdjacentDangerCount > 0 && !isEnemy && !isBoss)
        {
            ShowNumber(_cell.AdjacentDangerCount);
        }
    }

    // ── Nombres ──────────────────────────────────────────────────────────────

    private void ShowNumber(int count)
    {
        if (count <= 0 || count > 8 || _numberRenderer == null || _spriteSet == null)
        { HideNumber(); return; }

        Sprite numSprite = _spriteSet.GetNumberSprite(count);
        if (numSprite != null)
        {
            _numberRenderer.sprite = numSprite;
            _numberRenderer.enabled = true;
        }
        else
        {
            HideNumber();
        }
    }

    private void HideNumber()
    {
        if (_numberRenderer != null) _numberRenderer.enabled = false;
    }

    // ── Icônes ───────────────────────────────────────────────────────────────

    private void ShowIcon(Sprite sprite)
    {
        if (_iconRenderer == null) return;
        _iconRenderer.sprite = sprite;
        _iconRenderer.enabled = sprite != null;
    }

    private void HideIcon()
    {
        if (_iconRenderer != null) _iconRenderer.enabled = false;
    }

    // ── Ennemis ──────────────────────────────────────────────────────────────

    private void SpawnEnemyInstance(CellContent enemyType)
    {
        // Vérifier si l'ennemi existe déjà (Unity: objet détruit == null mais pas toujours)
        if (_enemyInstance != null && _enemyInstance.gameObject != null) return;
        _enemyInstance = null; // reset si détruit

        var db = EnemyDatabase.Instance;
        var data = db?.Get(enemyType);

        // Sauvegarder le sprite du prefab AVANT Instantiate
        Sprite prefabSprite = null;
        if (data?.prefab != null)
        {
            var prefabSR = data.prefab.GetComponent<SpriteRenderer>();
            if (prefabSR != null) prefabSprite = prefabSR.sprite;
        }

        // Créer l'instance
        GameObject go;
        if (data != null && data.prefab != null)
        {
            go = Instantiate(data.prefab, null);
        }
        else
        {
            // Fallback : pas de prefab trouvé dans EnemyDatabase
            go = new GameObject($"E_{enemyType}");
            var fallbackSR = go.AddComponent<SpriteRenderer>();
            fallbackSR.sortingLayerName = "CellContent";
            fallbackSR.sortingOrder = 5;
            go.AddComponent<BoxCollider2D>().size = Vector2.one * 0.8f;
            Debug.LogWarning($"[UCV] ⚠️ Prefab introuvable pour {enemyType} dans EnemyDatabase !");
        }

        // Position sur la case
        go.transform.position = transform.position + new Vector3(0f, 0f, -0.15f);

        // Stopper toutes les coroutines AVANT Initialize
        // (empêche FallbackAttackDelay de lancer une attaque immédiate)
        go.GetComponent<MonoBehaviour>()?.StopAllCoroutines();

        // Initialiser l'EnemyInstance
        _enemyInstance = go.GetComponent<EnemyInstance>();
        if (_enemyInstance == null) _enemyInstance = go.AddComponent<EnemyInstance>();
        _enemyInstance.Initialize(enemyType, _cell.X, _cell.Y);

        // APRÈS Initialize : stopper l'attaque auto + forcer le sprite
        _enemyInstance.StopAllCoroutines();

        var sr = go.GetComponent<SpriteRenderer>()
              ?? go.GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            sr.sortingLayerName = "CellContent";
            sr.sortingOrder = 5;
            sr.enabled = true;
            // Forcer le sprite depuis le prefab (Initialize peut le perdre)
            if (prefabSprite != null)
                sr.sprite = prefabSprite;
        }

        _enemySpawned = true; // empêche le respawn dans les prochains RefreshAllViews

        string spName = (sr != null && sr.sprite != null) ? sr.sprite.name : "NULL";
        string dbInfo = data != null ? $"prefab={data.prefab?.name ?? "null"}" : "DB_MANQUANT";
        Debug.Log($"[UCV] Ennemi spawné : {enemyType} ({_cell.X},{_cell.Y}) " +
                  $"sprite={spName} {dbInfo} prefabSprite={(prefabSprite?.name ?? "null")}");
    }

    // =========================================================================
    // CLIC
    // =========================================================================

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_cell == null || !_initialized) return;
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (!_isFloor || _isWallContour) return;

        // Portail sortie
        if (_isExitPortal)
        {
            CaveManager.Instance?.ExitCave(_cell.X, _cell.Y);
            return;
        }

        var ug = UndergroundGrid.Instance;
        if (ug == null) return;

        // Premier clic → placer les dangers
        if (!ug.DangersPlaced)
        {
            var caveGen = FindObjectOfType<CaveGenerator>(true);
            ug.PlaceDangers(_cell.X, _cell.Y, caveGen?._config);
        }

        // Case déjà révélée avec ennemi → le héros doit s'approcher et attaquer
        // (géré par HeroController.PathfindAndMove → combat)
        // Ne pas re-traiter ici, laisser le système de mouvement gérer
        if (_cell.IsRevealed && _cell.IsEnemy && _enemyInstance != null)
            return; // HeroController gère l'approche + combat

        // Révéler la case
        ug.RevealCell(_cell.X, _cell.Y);

        // Rafraîchir les vues pour montrer les nombres et ennemis révélés
        RefreshAllViews();

        // Si on vient de révéler un ennemi → le combat est géré par HeroCombat
        // Ne pas re-traiter
    }

    /// <summary>Force le sprite après 1 frame (après Awake/Start de EnemyAnimator).</summary>
    private System.Collections.IEnumerator ForceSpriteLate(GameObject go, Sprite sprite)
    {
        yield return null; // attendre 1 frame
        if (go == null) yield break;
        var sr = go.GetComponent<SpriteRenderer>() ?? go.GetComponentInChildren<SpriteRenderer>();
        if (sr != null && sprite != null)
        {
            sr.sprite = sprite;
            sr.enabled = true;
        }
    }

    // =========================================================================
    // TOOLTIP PORTAIL
    // =========================================================================

    private void OnMouseEnter()
    {
        if (_isExitPortal && CaveManager.Instance?.IsInCave == true)
            TooltipUI.Show("Sortir de la grotte");
    }

    private void OnMouseExit()
    {
        if (_isExitPortal) TooltipUI.Hide();
    }

    // =========================================================================
    // REFRESH GLOBAL
    // =========================================================================

    public static void RefreshAllViews()
    {
        foreach (var view in FindObjectsOfType<UndergroundCellView>())
            view.ApplySprite();
    }
}
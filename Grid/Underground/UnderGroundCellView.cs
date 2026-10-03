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
///
/// PORTAILS (nouvelle logique) :
///   Les cases portail sont des MURS qui portent le sprite bordBasCave.
///   _isEntryPortal et _isExitPortal sont détectés via CaveAutoTiler,
///   qui est la source de vérité unique pour ces positions.
///   Plus de référence à UndergroundGrid.IsExitPortal().
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

    /// <summary>Y grille de cette cell view — utilisé par CaveManager pour isoler les caves.</summary>
    public int GetCellY() => _cell?.Y ?? -1;
    private Sprite _autoTiledSprite;
    private Sprite _hiddenSprite;
    private bool _isFloor;
    private bool _isWallContour;
    private bool _isEntryPortal;   // case mur portail entrée (y=0)
    private bool _isExitPortal;    // case mur portail sortie (y=H-1)
    private bool _initialized;
    private EnemyInstance _enemyInstance;
    private bool _enemySpawned;    // true = ennemi déjà créé, ne PAS respawn

    // =========================================================================
    // INITIALISATION
    // =========================================================================

    public void InitializeCave(Cell cell, CaveLayout layout, CaveSpriteSet spriteSet)
    {
        _cell = cell;
        _layout = layout;
        _spriteSet = spriteSet;

        SetupRenderers();

        _isFloor = layout.IsFloor(cell.X, cell.Y);
        _isWallContour = CaveAutoTiler.IsWallContour(layout, cell.X, cell.Y);
        _autoTiledSprite = CaveAutoTiler.GetSprite(layout, cell.X, cell.Y, spriteSet);
        // Garantir un sprite fallback pour les murs de contour — évite les cases vides
        if (_autoTiledSprite == null && (_isWallContour || _isEntryPortal || _isExitPortal))
            _autoTiledSprite = spriteSet?.wallFull;
        _hiddenSprite = spriteSet?.hiddenRock ?? spriteSet?.wallFull;

        // ── Détection portails via CaveAutoTiler (source unique de vérité) ────
        _isEntryPortal = CaveAutoTiler.IsEntryPortal(layout, cell.X, cell.Y);
        _isExitPortal = CaveAutoTiler.IsExitPortal(layout, cell.X, cell.Y);

        // Murs de contour + portails → toujours révélés (toujours visibles)
        // Les roches profondes (!_isWallContour && !_isFloor) ne sont pas révélées
        // — elles n'ont pas de cell view (SpawnViews les filtre) et sont couvertes par le fog
        if (_isWallContour || _isEntryPortal || _isExitPortal)
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
        _bgRenderer.enabled = true;
        if (_isWallContour || _isEntryPortal || _isExitPortal)
            _bgRenderer.sprite = _autoTiledSprite ?? _hiddenSprite;
        else
            _bgRenderer.sprite = _hiddenSprite;
    }

    private void ShowRevealed()
    {
        _bgRenderer.enabled = true;
        if (!_isFloor || _isEntryPortal || _isExitPortal)
        {
            _bgRenderer.sprite = _autoTiledSprite ?? _spriteSet?.wallFull ?? _hiddenSprite;
            return;
        }

        // Sol révélé — fond procédural
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
            _enemyInstance = null; // ennemi battu — ne pas respawn
        }

        if (_cell.AdjacentDangerCount > 0 && !isEnemy && !isBoss)
            ShowNumber(_cell.AdjacentDangerCount);
    }

    // ── Nombres ───────────────────────────────────────────────────────────────

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
    }

    private void HideNumber()
    {
        if (_numberRenderer != null) _numberRenderer.enabled = false;
    }

    private void HideIcon()
    {
        if (_iconRenderer != null) _iconRenderer.enabled = false;
    }

    // ── Spawn ennemi ──────────────────────────────────────────────────────────

    private void SpawnEnemyInstance(CellContent enemyType)
    {
        var db = EnemyDatabase.Instance;
        var data = db?.Get(enemyType);
        if (data?.prefab == null) return;

        var ug = UndergroundGrid.Instance;
        if (ug == null) return;

        Vector3 worldPos = ug.GridToWorld(_cell.X, _cell.Y);
        var go = Object.Instantiate(data.prefab, worldPos, Quaternion.identity);

        // Parent dans CaveSpawner.EnemiesContainer si disponible
        var cm = CaveManager.Instance;
        if (cm != null)
        {
            var spawner = cm.GetSpawner(cm.ActiveCaveIndex);
            if (spawner != null)
            {
                spawner.EnsureContainers();
                go.transform.SetParent(spawner.EnemiesContainer);
            }
        }

        Sprite prefabSprite = data.prefab.GetComponent<SpriteRenderer>()?.sprite;

        var enemy = go.GetComponent<EnemyInstance>();
        if (enemy != null)
            enemy.Initialize(enemyType, _cell.X, _cell.Y);

        var sr = go.GetComponent<SpriteRenderer>()
              ?? go.GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            sr.sortingLayerName = "CellContent";
            sr.sortingOrder = 5;
            sr.enabled = true;
            if (prefabSprite != null) sr.sprite = prefabSprite;
        }

        _enemySpawned = true;

        string spName = (sr != null && sr.sprite != null) ? sr.sprite.name : "NULL";
        Debug.Log($"[UCV] Ennemi spawné : {enemyType} ({_cell.X},{_cell.Y}) " +
                  $"cave={cm?.ActiveCaveIndex ?? -1}");
    }

    // =========================================================================
    // CLIC
    // =========================================================================

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_cell == null || !_initialized) return;
        if (eventData.button != PointerEventData.InputButton.Left) return;

        // ── Portail (entrée OU sortie) → sortir de la grotte ───────────────
        // Les deux portails sont bidirectionnels — on peut sortir par les deux.
        // CaveManager.ExitCave détermine quel portail surface correspond.
        if (_isExitPortal || _isEntryPortal)
        {
            CaveManager.Instance?.ExitCave(_cell.X, _cell.Y);
            return;
        }

        // ── Murs → non cliquables ─────────────────────────────────────────────
        if (!_isFloor || _isWallContour) return;

        var ug = UndergroundGrid.Instance;
        if (ug == null) return;

        var hero = HeroController.Instance;
        if (hero == null) return;

        // ── Safe first click → placer les dangers DANS LA CAVE ACTIVE ────────
        var cm = CaveManager.Instance;
        int ci = cm != null ? cm.ActiveCaveIndex : 0;

        if (!ug.DangersPlacedForCave(ci))
        {
            var spawner = cm?.GetSpawner(ci);
            var config = spawner?.Config;
            if (config == null)
            {
                var caveGen = FindObjectOfType<CaveGenerator>(true);
                config = caveGen?._config;
            }
            if (config != null)
            {
                var (minY, maxY) = cm != null ? cm.GetCaveYRange(ci) : (0, -1);
                Debug.Log($"[UCV] PlaceDangers cave={ci} Y=[{minY},{maxY}] " +
                          $"config={config.name} ratio={config.dangerRatio}");
                ug.PlaceDangers(_cell.X, _cell.Y, config, minY, maxY);
                ug.MarkDangersPlaced(ci);
            }
            else
            {
                Debug.LogWarning($"[UCV] ❌ Pas de CaveConfig pour cave={ci} !");
            }
        }

        var target = new Vector2Int(_cell.X, _cell.Y);

        // ── Combat en cours → fuir d'abord, puis bouger ───────────────────────
        var combat = HeroCombat.Instance;
        if (combat != null && combat.IsInCombat)
        {
            combat.RequestFlee(() =>
            {
                if (!_cell.IsRevealed)
                    hero.ApproachAndRevealOnGrid(target, ug);
                else
                    hero.MoveOnGrid(target, ug);
            });
            return;
        }

        // ── Case non révélée → s'approcher et révéler ─────────────────────────
        if (!_cell.IsRevealed)
        {
            hero.ApproachAndRevealOnGrid(target, ug);
            return;
        }

        // ── Case révélée (vide ou ennemi) → se déplacer ───────────────────────
        // IsBlocked bloque les ennemis révélés → PathfindAndMove s'arrête adjacent
        // → EnemyInstance.TriggerCombat se déclenche à l'arrivée
        hero.MoveOnGrid(target, ug);
    }

    // ── Force sprite après 1 frame (après Awake/Start de EnemyAnimator) ──────

    private System.Collections.IEnumerator ForceSpriteLate(GameObject go, Sprite sprite)
    {
        yield return null;
        if (go == null) yield break;
        var sr = go.GetComponent<SpriteRenderer>()
              ?? go.GetComponentInChildren<SpriteRenderer>();
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
        else if (_isEntryPortal)
            TooltipUI.Show("Entrer dans la grotte");
    }

    private void OnMouseExit()
    {
        if (_isExitPortal || _isEntryPortal) TooltipUI.Hide();
    }

    // =========================================================================
    // REFRESH GLOBAL
    // =========================================================================

    public void Refresh()
    {
        if (!_initialized || _layout == null || _spriteSet == null) return;
        _isFloor = _layout.IsFloor(_cell.X, _cell.Y);
        _isWallContour = CaveAutoTiler.IsWallContour(_layout, _cell.X, _cell.Y);
        _autoTiledSprite = CaveAutoTiler.GetSprite(_layout, _cell.X, _cell.Y, _spriteSet);
        if (_autoTiledSprite == null && (_isWallContour || _isEntryPortal || _isExitPortal))
            _autoTiledSprite = _spriteSet?.wallFull;
        ApplySprite();
    }

    /// <summary>
    /// Rafraîchit uniquement les cases sol — chiffres et contenu.
    /// Ne recalcule PAS les bitmasks des murs.
    /// Utilisé après révélation et mort d'ennemi.
    /// </summary>
    public static void RefreshFloorNumbers()
    {
        foreach (var view in FindObjectsOfType<UndergroundCellView>())
            if (view._isFloor) view.ApplySprite();
    }

    public static void RefreshAllViews()
    {
        foreach (var view in FindObjectsOfType<UndergroundCellView>())
            view.Refresh();
    }
}
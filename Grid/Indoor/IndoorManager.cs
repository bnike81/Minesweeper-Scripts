using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// IndoorManager — Transitions MainGrid ↔ Intérieurs Tilemap.
///
/// FLOW :
///   1. SheeperFarmSpawner → RegisterBuilding(surfaceX, surfaceY, "Cabane")
///   2. BuildingDoorTrigger → EnterBuilding(x, y)
///   3. SceneLoader.EnterIndoor() → masque MainGrid + charge scène Indoor
///   4. OnIndoorSceneReady → active CabaneSetup + téléporte héros à la porte
///   5. IndoorExitTrigger (DoorExit) → ExitBuilding() → SceneLoader.ExitIndoor()
///   6. OnReturnToSurface → masque CabaneSetup + retéléporte en Grid 0
/// </summary>
public class IndoorManager : MonoBehaviour
{
    public static IndoorManager Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Contenu Indoor")]
    [Tooltip("GO parent de tout le contenu de la cabane (CabaneSetup).\n" +
             "Contient les 3 Tilemaps + IndoorTilemapGrid + DoorExit.\n" +
             "Est DÉSACTIVÉ au démarrage et ACTIVÉ quand on entre dans la cabane.")]
    [SerializeField] private GameObject _indoorContent;

    [Tooltip("Trouvé automatiquement dans _indoorContent — laisser vide.")]
    [SerializeField] private IndoorTilemapGrid _tilemapGrid;

    [Header("Fog Indoor")]
    [Tooltip("Composant IndoorFog sur le même GO ou enfant — crée la grille + fog.")]
    [SerializeField] private IndoorFog _indoorFog;

    // ── Registre des bâtiments ────────────────────────────────────────────────

    private struct Building
    {
        public int surfaceX, surfaceY;
        public string label;
    }

    private readonly List<Building> _buildings = new();
    private Building _active;

    // ── État ──────────────────────────────────────────────────────────────────

    public bool IsIndoor { get; private set; }

    /// <summary>Pour CaveCellView.OnPointerClick (compatibilité IGridContext).</summary>
    public IGridContext ActiveIndoor =>
        (_tilemapGrid != null && _tilemapGrid.IsReady) ? _tilemapGrid as IGridContext : null;

    // =========================================================================
    // INIT
    // =========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // MASQUER le contenu indoor au démarrage
        // Les tilemaps ne doivent pas être visibles avant d'entrer dans la cabane
        if (_indoorContent != null)
        {
            _indoorContent.SetActive(false);
            Debug.Log("[IndoorManager] CabaneSetup masqué au démarrage.");
        }
        else
        {
            Debug.LogWarning("[IndoorManager] ⚠️ _indoorContent non assigné dans l'Inspector !\n" +
                             "Assigner le GO 'CabaneSetup' dans le champ 'Indoor Content'.");
        }

        // Découverte auto
        if (_tilemapGrid == null && _indoorContent != null)
            _tilemapGrid = _indoorContent.GetComponentInChildren<IndoorTilemapGrid>(true);
        if (_indoorFog == null)
            _indoorFog = GetComponentInChildren<IndoorFog>(true);
        if (_indoorFog == null)
            _indoorFog = gameObject.AddComponent<IndoorFog>();
    }

    private void OnEnable() => EventBus.Subscribe<OnRunStarted>(OnRunStarted);
    private void OnDisable() => EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);

    private void OnRunStarted(OnRunStarted _)
    {
        _buildings.Clear();
        IsIndoor = false;
        _indoorFog?.Clear();
        _indoorContent?.SetActive(false);
        _tilemapGrid = null;
    }

    // =========================================================================
    // ENREGISTREMENT DES BÂTIMENTS
    // =========================================================================

    public void RegisterBuilding(int surfaceX, int surfaceY, string label = "Bâtiment")
    {
        // Éviter les doublons (SheeperFarmSpawner peut appeler 2× si OnGridExtended)
        foreach (var b in _buildings)
            if (b.surfaceX == surfaceX && b.surfaceY == surfaceY) return;

        _buildings.Add(new Building
        {
            surfaceX = surfaceX,
            surfaceY = surfaceY,
            label = label
        });
        Debug.Log($"[IndoorManager] Bâtiment enregistré : ({surfaceX},{surfaceY}) \"{label}\"");
    }

    // =========================================================================
    // ENTRÉE DANS LE BÂTIMENT
    // =========================================================================

    public void EnterBuilding(int surfaceX, int surfaceY)
    {
        if (IsIndoor) { Debug.Log("[IndoorManager] Déjà en intérieur."); return; }

        // Trouver le bâtiment
        Building? found = null;
        foreach (var b in _buildings)
        {
            if (Mathf.Abs(b.surfaceX - surfaceX) <= 1 &&
                Mathf.Abs(b.surfaceY - surfaceY) <= 2)
            { found = b; break; }
        }
        if (!found.HasValue && _buildings.Count == 1) found = _buildings[0];

        if (!found.HasValue)
        {
            Debug.LogWarning($"[IndoorManager] Aucun bâtiment près de ({surfaceX},{surfaceY}). " +
                             $"Enregistrés : {_buildings.Count}");
            return;
        }
        _active = found.Value;

        // Charger la scène Indoor additivement → masque MainGrid
        SceneLoader.Instance?.EnterIndoor();

        // Attendre que la transition soit terminée pour téléporter le héros
        EventBus.Subscribe<OnZoneTransitionComplete>(OnIndoorSceneReady);

        IsIndoor = true;
        Debug.Log($"[IndoorManager] ► Entrée {_active.label}...");
    }

    private void OnIndoorSceneReady(OnZoneTransitionComplete e)
    {
        EventBus.Unsubscribe<OnZoneTransitionComplete>(OnIndoorSceneReady);
        if (e.Zone != SceneLoader.ZoneScene.Indoor) return;

        // Activer le contenu indoor (tilemaps visibles)
        if (_indoorContent != null)
        {
            _indoorContent.SetActive(true);
            Debug.Log("[IndoorManager] CabaneSetup activé.");
        }

        // Retrouver IndoorTilemapGrid si besoin
        if (_tilemapGrid == null && _indoorContent != null)
            _tilemapGrid = _indoorContent.GetComponentInChildren<IndoorTilemapGrid>(true);

        if (_tilemapGrid == null)
        {
            Debug.LogError("[IndoorManager] ❌ IndoorTilemapGrid introuvable dans CabaneSetup !\n" +
                           "Vérifier que TileMapGrid a le script IndoorTilemapGrid.");
            return;
        }

        // Transmettre la position de retour surface
        _tilemapGrid.SetSurfaceReturnPos(
            new Vector2Int(_active.surfaceX, _active.surfaceY));

        // ── Aligner la porte indoor sur la porte outdoor (même système de coords) ─
        _tilemapGrid.AlignToOutdoorDoor(new Vector2Int(_active.surfaceX, _active.surfaceY));

        // ── Générer la grille + fog indoor ────────────────────────────────────
        _indoorFog?.Generate(_tilemapGrid);

        // ── Téléporter le héros à la porte ────────────────────────────────────
        var spawnWorld = _tilemapGrid.HeroSpawnWorldPos;
        var spawnGrid = _tilemapGrid.HeroSpawnGrid;
        HeroController.Instance?.TeleportToCave(spawnGrid.x, spawnGrid.y, spawnWorld);

        TooltipUI.Hide();

        Debug.Log($"[IndoorManager] ► Dans {_active.label} " +
                  $"— spawn ({spawnGrid.x},{spawnGrid.y}) world={spawnWorld}");
    }

    // =========================================================================
    // SORTIE DU BÂTIMENT
    // =========================================================================

    public void ExitBuilding(int doorX = 0, int doorY = 0)
    {
        if (!IsIndoor) return;

        // Masquer le contenu indoor + nettoyer le fog
        _indoorFog?.Clear();
        _indoorContent?.SetActive(false);

        // Décharger la scène Indoor
        SceneLoader.Instance?.ExitIndoor();

        EventBus.Subscribe<OnZoneTransitionComplete>(OnReturnToSurface);

        IsIndoor = false;
        _tilemapGrid = null;
    }

    private void OnReturnToSurface(OnZoneTransitionComplete e)
    {
        EventBus.Unsubscribe<OnZoneTransitionComplete>(OnReturnToSurface);
        if (e.Zone != SceneLoader.ZoneScene.MainGrid) return;

        // Retéléporter le héros en surface
        float cs = GridManager.Instance?.CellStep ?? 1.05f;
        var returnWorld = new Vector3(
            _active.surfaceX * cs, _active.surfaceY * cs, 0f);
        HeroController.Instance?.TeleportToCave(
            _active.surfaceX, _active.surfaceY, returnWorld);

        Debug.Log($"[IndoorManager] ◄ Retour surface ({_active.surfaceX},{_active.surfaceY})");
    }
}
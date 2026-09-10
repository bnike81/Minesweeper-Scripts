using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// MountainSpawner — Orchestre la génération de montagnes via WFC.
///
/// NOUVEAU : utilise MountainWFCGenerator au lieu de Base/Mid/Top.
/// La Recipe reste la même — le WFC la respecte.
///
/// Flow :
///   OnGridExtended(level == spawnAtLevel)
///     → MountainWFCGenerator.Generate(recipe, baseX, baseY, gridWidth)
///     → parcourt les Placements
///     → MountainBuilder.PlaceAt(type, gx, gy) pour chaque pièce
///     → CaveEntranceTrigger attaché sur les faces cave
/// </summary>
public class MountainSpawner : MonoBehaviour
{
    [Header("=== Recette ===")]
    [SerializeField] private MountainRecipe _recipe;

    [Header("=== Sprites centralisés ===")]
    [SerializeField] private MountainSprites _sprites;

    [Header("=== Sorting ===")]
    [SerializeField] private string _sortingLayer = "CellContent";
    [SerializeField] private int _sortingOrder = 2;

    private bool _generated = false;
    private readonly List<GameObject> _allSpawned = new(128);
    private MountainWFCGenerator _lastWfc;
    private MountainBuilder _lastBuilder;

    // =========================================================================
    // INIT
    // =========================================================================

    private void Awake()
    {
        // Pas de singleton — plusieurs MountainSpawner peuvent coexister
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnGridExtended>(OnGridExtended);
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnGridExtended>(OnGridExtended);
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
    }

    private void OnRunStarted(OnRunStarted e)
    {
        _generated = false;
        foreach (var go in _allSpawned) if (go) Destroy(go);
        _allSpawned.Clear();
        MountainBuilder.ClearSliceCache();
    }

    // =========================================================================
    // DÉCLENCHEMENT
    // =========================================================================

    private void OnGridExtended(OnGridExtended evt)
    {
        if (_recipe == null) return;
        if (evt.Level == _recipe.spawnAtLevel && !_generated)
        {
            if (Random.value <= _recipe.spawnChance)
                StartCoroutine(SpawnMountainNextFrame());
        }
        else if (_generated && _lastWfc != null && _lastBuilder != null)
        {
            // Re-réserver les cellules montagne sur les nouvelles rangées de grille
            StartCoroutine(ReReserveNextFrame());
        }
    }

    private IEnumerator ReReserveNextFrame()
    {
        yield return null;
        yield return null;
        // Re-marquer les sprites montagne sur les cellules existantes
        foreach (var p in _lastWfc.Placements)
        {
            _lastBuilder.Reserve(p.gridX, p.gridY, MountainPieceData.GetHeight(p.type));
        }
        _lastBuilder.ReservePlateau(_lastWfc.Placements);
        _lastBuilder.ClearMainGridPlateau();
    }

    private IEnumerator SpawnMountainNextFrame()
    {
        // Attendre que la grille et le fog soient prêts
        yield return null;
        var gm = GridManager.Instance;
        if (gm == null) yield break;
        while (MainGrid.Instance?.IsSpawning == true) yield return null;
        var fog = FogOfWar.Instance;
        if (fog != null) while (fog.IsBuilding) yield return null;
        yield return null;

        if (_recipe == null || _sprites == null) yield break;

        _generated = true;
        SpawnMountainWFC(gm);
    }

    // =========================================================================
    // GÉNÉRATION WFC
    // =========================================================================

    private void SpawnMountainWFC(GridManager gm)
    {
        // Position de la base
        int ext = GameManager.Instance?.GridHeightExtensionPerLevel ?? 16;
        int totalMtnHeight = _recipe.midTotalHeight + 12; // base(5) + mid + top(~7)
        int baseY = gm.Height - ext + ext / 3;
        // S'assurer que le sommet reste dans la grille
        baseY = Mathf.Clamp(baseY, 4, gm.Height - totalMtnHeight - 2);
        int baseX = gm.Width / 2;

        Debug.Log($"[MountainSpawner] WFC spawn '{_recipe.recipeName}' " +
                  $"baseX={baseX} baseY={baseY} level={_recipe.spawnAtLevel}");

        // Générer la séquence de pièces via WFC
        var wfc = new MountainWFCGenerator();
        wfc.Generate(_recipe, baseX, baseY, gm.Width);

        // Créer le builder pour placer les sprites
        var builder = new MountainBuilder(
            _sprites, gm.CellStep, gm,
            transform, _sortingLayer, _sortingOrder,
            _allSpawned);

        // Placer chaque pièce
        foreach (var p in wfc.Placements)
        {
            builder.PlaceAt(p.type, p.gridX, p.gridY);
        }

        // Marquer le plateau intérieur comme réservé
        builder.ReservePlateau(wfc.Placements);

        // Vider MainGrid sur le plateau
        builder.ClearMainGridPlateau();

        // Enregistrer le plateau sur le HighGrid commun
        builder.RegisterHighGrid();

        // Sauvegarder pour re-réserver quand la grille s'étend
        _lastWfc = wfc;
        _lastBuilder = builder;

        Debug.Log($"[MountainSpawner] WFC terminé : {wfc.Placements.Count} pièces, " +
                  $"sommet Y={wfc.SommetY}");
    }
}
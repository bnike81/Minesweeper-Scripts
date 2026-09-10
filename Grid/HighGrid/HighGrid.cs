using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// HighGrid — Grille altitude +1 commune à TOUTES les montagnes.
/// Singleton indépendant, pas enfant de MountainSpawner.
///
/// Chaque MountainSpawner enregistre son plateau via RegisterPlateau().
/// HighGrid fusionne tous les plateaux en une seule grille.
///
/// Hiérarchie :
///   MainGrid/HighGrid (GO indépendant)
///     ├── HighGridVisuals
///     └── HighGridFog
/// </summary>
public class HighGrid : MonoBehaviour
{
    public static HighGrid Instance { get; private set; }

    private Dictionary<(int, int), Cell> _cells = new();
    private Dictionary<int, int> _globalLeftAtY = new();
    private Dictionary<int, int> _globalRightAtY = new();
    private int _minY = int.MaxValue, _maxY = int.MinValue;
    private bool _visualsCreated;

    // Références aux sous-systèmes
    private HighGridVisuals _visuals;
    private HighGridFog _fog;

    public bool HasCells => _cells.Count > 0;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // =========================================================================
    // ENREGISTREMENT DE PLATEAUX
    // =========================================================================

    /// <summary>
    /// Enregistre un plateau montagneux. Peut être appelé plusieurs fois
    /// par différents MountainSpawner.
    /// </summary>
    public void RegisterPlateau(
        Dictionary<int, int> leftAtY,
        Dictionary<int, int> rightAtY,
        int minY, int maxY)
    {
        int newCells = 0;

        // Fusionner les limites
        foreach (var kvp in leftAtY)
        {
            int cy = kvp.Key;
            if (!_globalLeftAtY.ContainsKey(cy) || kvp.Value < _globalLeftAtY[cy])
                _globalLeftAtY[cy] = kvp.Value;
        }
        foreach (var kvp in rightAtY)
        {
            int cy = kvp.Key;
            if (!_globalRightAtY.ContainsKey(cy) || kvp.Value > _globalRightAtY[cy])
                _globalRightAtY[cy] = kvp.Value;
        }

        if (minY < _minY) _minY = minY;
        if (maxY > _maxY) _maxY = maxY;

        // Interpoler les Y manquants
        int lastLeft = -1, lastRight = -1;
        for (int cy = _minY; cy <= _maxY; cy++)
        {
            if (_globalLeftAtY.ContainsKey(cy)) lastLeft = _globalLeftAtY[cy];
            else if (lastLeft >= 0) _globalLeftAtY[cy] = lastLeft;

            if (_globalRightAtY.ContainsKey(cy)) lastRight = _globalRightAtY[cy];
            else if (lastRight >= 0) _globalRightAtY[cy] = lastRight;
        }

        // Créer les cellules UNIQUEMENT sur le plateau intérieur
        var gm = GridManager.Instance;
        for (int cy = _minY; cy <= _maxY; cy++)
        {
            if (!_globalLeftAtY.ContainsKey(cy) || !_globalRightAtY.ContainsKey(cy)) continue;
            int lx = _globalLeftAtY[cy] + 1;
            int rx = _globalRightAtY[cy];

            for (int cx = lx; cx < rx; cx++)
            {
                if (_cells.ContainsKey((cx, cy))) continue;

                // Vérifier que c'est bien du plateau intérieur (pas de la silhouette)
                if (gm != null)
                {
                    var mainCell = gm.GetCell(cx, cy);
                    if (mainCell == null) continue;
                    if (!mainCell.IsMountainPlateau) continue;
                }

                _cells[(cx, cy)] = new Cell(cx, cy, BiomeType.Cave);
                newCells++;
            }
        }

        Debug.Log($"[HighGrid] Plateau enregistré : +{newCells} cases (total {_cells.Count})");

        // Recréer les visuels et fog
        RebuildVisuals();
    }

    // =========================================================================
    // VISUELS ET FOG
    // =========================================================================

    private void RebuildVisuals()
    {
        // Chercher les GOs enfants existants (placés dans la hiérarchie)
        if (_visuals == null) _visuals = GetComponentInChildren<HighGridVisuals>();
        if (_fog == null) _fog = GetComponentInChildren<HighGridFog>();

        // Si pas trouvés, les créer dynamiquement
        if (_visuals == null)
        {
            var visualsGO = new GameObject("HighGridVisuals");
            visualsGO.transform.SetParent(transform);
            _visuals = visualsGO.AddComponent<HighGridVisuals>();
        }
        if (_fog == null)
        {
            var fogGO = new GameObject("HighGridFog");
            fogGO.transform.SetParent(transform);
            _fog = fogGO.AddComponent<HighGridFog>();
        }

        // Initialiser avec les données actuelles
        _visuals.Initialize(this);
        _fog.Initialize(this, _visuals);
    }

    // =========================================================================
    // ACCÈS
    // =========================================================================

    public Cell GetCell(int worldX, int worldY)
    {
        _cells.TryGetValue((worldX, worldY), out var cell);
        return cell;
    }

    public bool IsInPlateau(int worldX, int worldY)
    {
        return _cells.ContainsKey((worldX, worldY));
    }

    public Dictionary<(int, int), Cell> GetAllCells() => _cells;

    public float CellStep
    {
        get
        {
            if (GridManager.Instance != null) return GridManager.Instance.CellStep;
            return 1.063f;
        }
    }
}
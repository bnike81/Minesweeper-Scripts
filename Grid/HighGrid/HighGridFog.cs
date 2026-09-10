using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// HighGridFog — Fog coopératif piloté par l'état de MainGrid fog.
///
/// PAS basé sur la position du héros.
/// Basé sur les CASES RÉVÉLÉES de MainGrid à la frontière.
///
/// Principe :
///   1. Vérifier quelles cases MainGrid proches de la frontière sont révélées
///   2. Récupérer l'opacité du fog MainGrid sur ces cases
///   3. Continuer la diffusion dans le plateau avec la distance restante
///   4. Inversement (futur) : si héros sur le plateau, diffuser vers MainGrid
/// </summary>
public class HighGridFog : MonoBehaviour
{
    [Header("=== Activation ===")]
    [SerializeField] private bool _enabled = true;

    [Header("=== Opacité par Distance (mêmes que FogOfWar) ===")]
    [SerializeField, Range(0f, 1f)] private float _fogDist1 = 0.12f;
    [SerializeField, Range(0f, 1f)] private float _fogDist2 = 0.35f;
    [SerializeField, Range(0f, 1f)] private float _fogDist3 = 0.58f;
    [SerializeField, Range(0f, 1f)] private float _fogDist4 = 0.78f;
    [SerializeField, Range(0f, 1f)] private float _fogFull = 0.92f;

    [Header("=== Dégradé ===")]
    [SerializeField] private bool _smoothFog = true;

    [Header("=== Visuel ===")]
    [SerializeField] private Color _fogColor = Color.black;
    [SerializeField] private int _sortingOrder = 12;
    [SerializeField] private string _sortingLayer = "Fog";

    private HighGrid _highGrid;
    private HighGridVisuals _visuals;
    private Dictionary<(int, int), SpriteRenderer> _overlays = new();
    private HashSet<(int, int)> _boundaryCells = new();
    private Sprite _fogSprite;
    private Material _fogMaterial;
    private float _cellStep;
    private bool _initialized;

    // Cache pour éviter de recalculer chaque frame
    private Dictionary<(int, int), float> _boundaryAlphaCache = new();
    private int _lastRevealedCount = -1;

    // =========================================================================
    // INIT
    // =========================================================================

    public void Initialize(HighGrid highGrid, HighGridVisuals visuals = null)
    {
        _highGrid = highGrid;
        _visuals = visuals;
        _cellStep = highGrid.CellStep;

        var tex = new Texture2D(4, 4);
        var pixels = new Color[16];
        for (int i = 0; i < 16; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        _fogSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4),
            new Vector2(0.5f, 0.5f), 4f);

        // Material identique à FogOfWar pour la même teinte de brouillard
        _fogMaterial = new Material(Shader.Find("Sprites/Default"));
        _fogMaterial.mainTexture = tex;
        _fogMaterial.enableInstancing = true;

        DetectBoundaryCells();
        CreateOverlays();
        _initialized = true;

        Debug.Log($"[HighGridFog] Initialisé : enabled={_enabled} overlays={_overlays.Count} " +
                  $"frontière={_boundaryCells.Count}");
    }

    private void DetectBoundaryCells()
    {
        _boundaryCells.Clear();
        var cells = _highGrid.GetAllCells();
        int[] dx = { -1, 1, 0, 0 };
        int[] dy = { 0, 0, -1, 1 };

        foreach (var kvp in cells)
        {
            int wx = kvp.Key.Item1, wy = kvp.Key.Item2;
            for (int d = 0; d < 4; d++)
            {
                if (!cells.ContainsKey((wx + dx[d], wy + dy[d])))
                {
                    _boundaryCells.Add((wx, wy));
                    break;
                }
            }
        }
    }

    private void CreateOverlays()
    {
        foreach (Transform child in transform) Destroy(child.gameObject);
        _overlays.Clear();

        var cells = _highGrid.GetAllCells();
        foreach (var kvp in cells)
        {
            int wx = kvp.Key.Item1, wy = kvp.Key.Item2;

            var go = new GameObject($"HF_{wx}_{wy}");
            go.transform.SetParent(transform);
            go.transform.position = new Vector3(wx * _cellStep, wy * _cellStep, -0.05f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _fogSprite;
            sr.sortingLayerName = _sortingLayer;
            sr.sortingOrder = _sortingOrder;
            sr.color = new Color(_fogColor.r, _fogColor.g, _fogColor.b, _fogFull);
            if (_fogMaterial != null) sr.sharedMaterial = _fogMaterial;

            _overlays[(wx, wy)] = sr;
        }
    }

    // =========================================================================
    // MISE À JOUR — piloté par l'état des cases MainGrid
    // =========================================================================

    private void Update()
    {
        if (!_initialized || !_enabled) return;
        UpdateFog();
    }

    private void UpdateFog()
    {
        // Pour chaque case du plateau, calculer le fog directement
        foreach (var kvp in _overlays)
        {
            int wx = kvp.Key.Item1, wy = kvp.Key.Item2;
            var sr = kvp.Value;

            float alpha = GetPlateauAlpha(wx, wy);
            sr.color = new Color(_fogColor.r, _fogColor.g, _fogColor.b, alpha);
            // Fog est une couche par-dessus — pas de contrôle des arbres
        }
    }

    /// <summary>
    /// Pour chaque case frontière, récupérer l'opacité MainGrid fog
    /// la plus basse (= la plus révélée) des voisins non-montagne.
    /// </summary>
    private void UpdateBoundaryAlphas(GridManager gm)
    {
        _boundaryAlphaCache.Clear();
        var fogOfWar = FogOfWar.Instance;

        foreach (var bc in _boundaryCells)
        {
            float bestAlpha = _fogFull;

            // Scanner dans un rayon de 3 autour de la frontière
            // pour trouver des cases MainGrid non-montagne
            for (int ddx = -3; ddx <= 3; ddx++)
                for (int ddy = -3; ddy <= 3; ddy++)
                {
                    int nx = bc.Item1 + ddx, ny = bc.Item2 + ddy;

                    var cell = gm.GetCell(nx, ny);
                    if (cell == null) continue;
                    if (cell.IsMountainReserved) continue; // ignorer silhouette + plateau

                    // Distance de la frontière à cette case MainGrid
                    float distToMain = Mathf.Sqrt(ddx * ddx + ddy * ddy);

                    // Récupérer l'opacité fog MainGrid sur cette case
                    float mainAlpha;
                    if (cell.IsRevealed)
                        mainAlpha = 0f;
                    else if (fogOfWar != null)
                        mainAlpha = fogOfWar.GetFogAlpha(nx, ny);
                    else
                        continue;

                    // L'opacité à la frontière = fog MainGrid + distance traversée
                    float stepPerCell = (_fogFull - _fogDist1) / 4f;
                    float alphaAtBoundary = mainAlpha + distToMain * stepPerCell;
                    alphaAtBoundary = Mathf.Min(alphaAtBoundary, _fogFull);

                    if (alphaAtBoundary < bestAlpha) bestAlpha = alphaAtBoundary;
                }

            _boundaryAlphaCache[bc] = bestAlpha;
        }
    }

    /// <summary>
    /// Calcule l'opacité fog pour une case du plateau.
    /// Distance DIRECTE depuis la case MainGrid non-montagne la plus proche.
    /// Pas d'intermédiaire frontière — la distance est continue.
    /// </summary>
    private float GetPlateauAlpha(int wx, int wy)
    {
        var gm = GridManager.Instance;
        var fogOfWar = FogOfWar.Instance;
        if (gm == null) return _fogFull;

        float bestAlpha = _fogFull;
        float stepPerCell = (_fogFull - _fogDist1) / 4f;

        // Scanner les cases MainGrid dans un rayon = visibilityRange + épaisseur silhouette
        int scanRadius = 6;
        for (int ddx = -scanRadius; ddx <= scanRadius; ddx++)
            for (int ddy = -scanRadius; ddy <= scanRadius; ddy++)
            {
                int nx = wx + ddx, ny = wy + ddy;
                var cell = gm.GetCell(nx, ny);
                if (cell == null) continue;
                if (cell.IsMountainReserved) continue;

                // Opacité fog MainGrid sur cette case
                float mainAlpha;
                if (cell.IsRevealed)
                    mainAlpha = 0f;
                else if (fogOfWar != null)
                    mainAlpha = fogOfWar.GetFogAlpha(nx, ny);
                else
                    continue;

                // Distance DIRECTE de cette case MainGrid à la case plateau
                float dist = Mathf.Sqrt(ddx * ddx + ddy * ddy);

                // Continuer le gradient : alpha = fog_main + distance × step
                float alpha = mainAlpha + dist * stepPerCell;
                alpha = Mathf.Min(alpha, _fogFull);

                if (alpha < bestAlpha) bestAlpha = alpha;
            }

        return bestAlpha;
    }

    // =========================================================================
    // ALPHA CALCULATION
    // =========================================================================

    private float GetAlphaForDistance(float dist)
    {
        if (dist <= 1f) return _fogDist1;
        if (dist <= 2f) return _smoothFog ? Mathf.Lerp(_fogDist1, _fogDist2, dist - 1f) : _fogDist2;
        if (dist <= 3f) return _smoothFog ? Mathf.Lerp(_fogDist2, _fogDist3, dist - 2f) : _fogDist3;
        if (dist <= 4f) return _smoothFog ? Mathf.Lerp(_fogDist3, _fogDist4, dist - 3f) : _fogDist4;
        return _fogFull;
    }
}
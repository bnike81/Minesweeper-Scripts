using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// UndergroundFog — Fog dense avec 3 niveaux.
/// Supporte le mode additif via Extend() : ajoute du fog pour une nouvelle cave
/// sans effacer le fog existant.
/// </summary>
public class UndergroundFog : MonoBehaviour
{
    [Header("Apparence")]
    [SerializeField] private Color _fogColor = Color.black;
    [SerializeField, Range(0.8f, 1f)] private float _fogFull = 0.95f;
    [SerializeField, Range(0.3f, 0.8f)] private float _fogVisited = 0.60f;
    [SerializeField] private string _sortingLayer = "CellContent";
    [SerializeField] private int _sortingOrder = 8;

    [Header("Vision")]
    [SerializeField, Range(1, 5)] private int _visionRadius = 3;
    [SerializeField, Range(0, 3)] private int _partialRadius = 1;

    private readonly Dictionary<(int, int), SpriteRenderer> _overlays = new();
    private readonly HashSet<(int, int)> _visited = new();
    private readonly HashSet<(int, int)> _caveCells = new();
    private Sprite _whiteSprite;
    private Material _fogMaterial;

    // =========================================================================
    // GÉNÉRATION — première cave (efface tout et recrée)
    // =========================================================================

    public void Generate(UndergroundGrid grid)
    {
        Clear();
        if (grid == null || !grid.IsGenerated) return;

        CreateSprite();

        float cs = grid.CellSize;
        Color full = new Color(_fogColor.r, _fogColor.g, _fogColor.b, _fogFull);

        // Couvrir toute la zone
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                if (_overlays.ContainsKey((x, y))) continue;

                Vector3 pos = grid.GridToWorld(x, y, -0.05f);
                var go = new GameObject("UF");
                go.transform.position = pos;
                go.transform.localScale = new Vector3(cs, cs, 1f);
                go.transform.SetParent(transform);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _whiteSprite;
                sr.sharedMaterial = _fogMaterial;
                sr.color = full;
                sr.sortingLayerName = _sortingLayer;
                sr.sortingOrder = _sortingOrder;

                _overlays[(x, y)] = sr;
            }

        // Identifier les cave cells via le GRID (pas le layout)
        _visited.Clear();
        _caveCells.Clear();
        CollectCaveCells(grid);

        Debug.Log($"[UndergroundFog] ✅ {_overlays.Count} fog, {_caveCells.Count} cave cells");
    }

    // =========================================================================
    // EXTEND — ajouter du fog pour une cave additionnelle
    // Ne détruit PAS le fog existant. Ajoute des overlays si la grille a grandi.
    // Ajoute les cave cells du nouveau layout.
    // =========================================================================

    public void Extend(UndergroundGrid grid, CaveLayout newLayout)
    {
        if (grid == null) return;

        CreateSprite();

        float cs = grid.CellSize;
        Color full = new Color(_fogColor.r, _fogColor.g, _fogColor.b, _fogFull);

        // Ajouter des overlays pour les nouvelles cases (grille agrandie)
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                if (_overlays.ContainsKey((x, y))) continue;

                Vector3 pos = grid.GridToWorld(x, y, -0.05f);
                var go = new GameObject("UF");
                go.transform.position = pos;
                go.transform.localScale = new Vector3(cs, cs, 1f);
                go.transform.SetParent(transform);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _whiteSprite;
                sr.sharedMaterial = _fogMaterial;
                sr.color = full;
                sr.sortingLayerName = _sortingLayer;
                sr.sortingOrder = _sortingOrder;

                _overlays[(x, y)] = sr;
            }

        // Ajouter les cave cells du nouveau layout
        for (int x = 0; x < newLayout.Width; x++)
            for (int y = 0; y < newLayout.Height; y++)
            {
                if (newLayout.IsFloor(x, y) || CaveAutoTiler.IsWallContour(newLayout, x, y))
                    _caveCells.Add((x, y));
            }

        Debug.Log($"[UndergroundFog] ✅ Extend : {_overlays.Count} fog, {_caveCells.Count} cave cells");
    }

    // =========================================================================
    // COLLECTE — identifie les cave cells via le grid (sol + contour)
    // =========================================================================

    private void CollectCaveCells(UndergroundGrid grid)
    {
        // Utiliser le grid directement — fonctionne pour toutes les caves
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                if (grid.IsFloor(x, y))
                { _caveCells.Add((x, y)); continue; }

                // Mur de contour : mur avec au moins un voisin sol
                if (!grid.IsFloor(x, y))
                {
                    bool adjFloor = false;
                    if (grid.IsFloor(x - 1, y) || grid.IsFloor(x + 1, y) ||
                        grid.IsFloor(x, y - 1) || grid.IsFloor(x, y + 1) ||
                        grid.IsFloor(x - 1, y - 1) || grid.IsFloor(x + 1, y - 1) ||
                        grid.IsFloor(x - 1, y + 1) || grid.IsFloor(x + 1, y + 1))
                        adjFloor = true;
                    if (adjFloor) _caveCells.Add((x, y));
                }
            }
    }

    // =========================================================================
    // MISE À JOUR VISION
    // =========================================================================

    public void UpdateVision(int heroX, int heroY)
    {
        int fullR = _visionRadius;
        int fadeR = _visionRadius + _partialRadius;

        for (int dx = -fullR; dx <= fullR; dx++)
            for (int dy = -fullR; dy <= fullR; dy++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dy) <= fullR)
                    _visited.Add((heroX + dx, heroY + dy));
            }

        Color fullColor = new Color(_fogColor.r, _fogColor.g, _fogColor.b, _fogFull);
        Color visitedColor = new Color(_fogColor.r, _fogColor.g, _fogColor.b, _fogVisited);

        foreach (var kvp in _overlays)
        {
            int x = kvp.Key.Item1, y = kvp.Key.Item2;
            var sr = kvp.Value;
            if (sr == null) continue;

            if (!_caveCells.Contains((x, y)))
            {
                sr.gameObject.SetActive(true);
                sr.color = fullColor;
                continue;
            }

            int dist = Mathf.Abs(x - heroX) + Mathf.Abs(y - heroY);

            if (dist <= fullR)
            {
                sr.gameObject.SetActive(false);
            }
            else if (dist <= fadeR)
            {
                sr.gameObject.SetActive(true);
                float t = (float)(dist - fullR) / Mathf.Max(1, _partialRadius);
                float alpha = Mathf.Lerp(0f, _fogVisited, t);
                sr.color = new Color(_fogColor.r, _fogColor.g, _fogColor.b, alpha);
            }
            else if (_visited.Contains((x, y)))
            {
                sr.gameObject.SetActive(true);
                sr.color = visitedColor;
            }
            else
            {
                sr.gameObject.SetActive(true);
                sr.color = fullColor;
            }
        }
    }

    // =========================================================================
    // UTILITAIRES
    // =========================================================================

    public void HideAll()
    {
        foreach (var sr in _overlays.Values)
            if (sr != null) sr.gameObject.SetActive(false);
    }

    public void ShowAll()
    {
        foreach (var sr in _overlays.Values)
            if (sr != null) sr.gameObject.SetActive(true);
    }

    public void Clear()
    {
        foreach (var sr in _overlays.Values)
        {
            if (sr == null) continue;
            if (Application.isPlaying) Destroy(sr.gameObject);
            else DestroyImmediate(sr.gameObject);
        }
        _overlays.Clear();
        _visited.Clear();
        _caveCells.Clear();

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
    }

    private void CreateSprite()
    {
        if (_whiteSprite != null) return;
        var tex = new Texture2D(4, 4);
        var px = new Color[16];
        for (int i = 0; i < 16; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.Apply();
        _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        _fogMaterial = new Material(Shader.Find("Sprites/Default"));
        _fogMaterial.mainTexture = _whiteSprite.texture;
    }

    private void OnDestroy()
    {
        Clear();
        if (_fogMaterial != null) Destroy(_fogMaterial);
        if (_whiteSprite != null) { Destroy(_whiteSprite.texture); Destroy(_whiteSprite); }
    }
}
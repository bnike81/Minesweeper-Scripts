using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// UndergroundFog — Fog dense avec 3 niveaux :
///   1. Fog total (0.95) — zones inexplorées
///   2. Fog partiel (0.60) — zones déjà visitées
///   3. Pas de fog — zone de vision actuelle du héros
///
/// Couvre TOUTE la grille (même dimensions que GridManager).
/// Les murs et sols sont tous cachés au départ.
/// Le fog se retire quand le héros approche.
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
    private readonly HashSet<(int, int)> _caveCells = new(); // cases grotte (sol + mur contour)
    private Sprite _whiteSprite;
    private Material _fogMaterial;

    // =========================================================================
    // GÉNÉRATION — couvre toute la grille
    // =========================================================================

    public void Generate(UndergroundGrid grid)
    {
        Clear();
        if (grid == null || !grid.IsGenerated) return;

        CreateSprite();

        var gm = GridManager.Instance;
        if (gm == null) return;

        float cs = gm.CellSize;
        int gridW = gm.Width;
        int gridH = gm.Height;
        Color full = new Color(_fogColor.r, _fogColor.g, _fogColor.b, _fogFull);

        // Couvrir TOUTE la zone (murs, sols, tout)
        for (int x = 0; x < gridW; x++)
            for (int y = 0; y < gridH; y++)
            {
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

        // Identifier les cases de la grotte (sol + mur contour) pour UpdateVision
        _visited.Clear();
        _caveCells.Clear();

        // Chercher le layout via CaveGenerator
        var caveGen = FindObjectOfType<CaveGenerator>(true);
        if (caveGen?.Layout != null && grid != null)
        {
            for (int cx = 0; cx < grid.Width; cx++)
                for (int cy = 0; cy < grid.Height; cy++)
                {
                    if (caveGen.Layout.IsFloor(cx, cy) || CaveAutoTiler.IsWallContour(caveGen.Layout, cx, cy))
                        _caveCells.Add((cx, cy));
                }
        }

        Debug.Log($"[UndergroundFog] ✅ {_overlays.Count} fog, {_caveCells.Count} cave cells, vision={_visionRadius}");
    }

    // =========================================================================
    // MISE À JOUR VISION — 3 niveaux de fog
    // =========================================================================

    public void UpdateVision(int heroX, int heroY)
    {
        int fullR = _visionRadius;
        int fadeR = _visionRadius + _partialRadius;

        // Marquer la zone actuelle comme visitée
        for (int dx = -fullR; dx <= fullR; dx++)
            for (int dy = -fullR; dy <= fullR; dy++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dy) <= fullR)
                    _visited.Add((heroX + dx, heroY + dy));
            }

        // Mettre à jour chaque overlay
        Color fullColor = new Color(_fogColor.r, _fogColor.g, _fogColor.b, _fogFull);
        Color visitedColor = new Color(_fogColor.r, _fogColor.g, _fogColor.b, _fogVisited);

        foreach (var kvp in _overlays)
        {
            int x = kvp.Key.Item1, y = kvp.Key.Item2;
            var sr = kvp.Value;
            if (sr == null) continue;

            // Cases hors de la grotte → TOUJOURS fog total (au-delà des murs)
            if (!_caveCells.Contains((x, y)))
            {
                sr.gameObject.SetActive(true);
                sr.color = fullColor;
                continue;
            }

            int dist = Mathf.Abs(x - heroX) + Mathf.Abs(y - heroY);

            if (dist <= fullR)
            {
                // Vision claire → pas de fog
                sr.gameObject.SetActive(false);
            }
            else if (dist <= fadeR)
            {
                // Transition douce
                sr.gameObject.SetActive(true);
                float t = (float)(dist - fullR) / Mathf.Max(1, _partialRadius);
                float alpha = Mathf.Lerp(0f, _fogVisited, t);
                sr.color = new Color(_fogColor.r, _fogColor.g, _fogColor.b, alpha);
            }
            else if (_visited.Contains((x, y)))
            {
                // Déjà visité → 60%
                sr.gameObject.SetActive(true);
                sr.color = visitedColor;
            }
            else
            {
                // Case grotte non visitée → fog total
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
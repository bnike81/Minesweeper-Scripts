using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// IndoorFog — Fog de guerre indoor, IDENTIQUE au FogOfWar de MainGrid.
///
/// Crée des overlays individuels par case, positionnés et dimensionnés
/// exactement comme FogOfWar :
///   - Position : (x * stepPx) / 16f   (inclut le gap de 0.0625)
///   - Scale    : cellSize (PAS cellStep) → les gaps entre cases = lignes de grille
///   - Sprite   : carré blanc 4×4, PPU=4, pivot centre
///   - Color    : noir opaque à 0.92 alpha
///
/// Cases tilemap walkables → pas de fog (0%)
/// Toutes les autres       → fog 100%
/// </summary>
public class IndoorFog : MonoBehaviour
{
    [Header("Apparence (copier les valeurs de FogOfWar MainGrid)")]
    [SerializeField] private Color _fogColor = Color.black;
    [SerializeField, Range(0f, 1f)] private float _fogAlpha = 0.92f;
    [SerializeField] private string _sortingLayer = "CellContent";
    [SerializeField] private int _sortingOrder = 8;

    private readonly List<SpriteRenderer> _overlays = new();
    private Sprite _whiteSprite;
    private Material _fogMaterial;

    /// <summary>
    /// Génère le fog indoor couvrant la zone MainGrid.
    /// Cases avec tuiles Background (marchables) → pas de fog.
    /// Tout le reste → fog opaque.
    /// </summary>
    public void Generate(IndoorTilemapGrid grid)
    {
        Clear();
        if (grid == null || !grid.IsReady) return;

        var gm = GridManager.Instance;
        if (gm == null) return;

        // ── Paramètres identiques à FogOfWar ─────────────────────────────────
        float cellStep = gm.CellStep;               // 1.0625
        float cellSize = gm.CellSize;               // 1.0 (sans spacing)
        int stepPx = Mathf.RoundToInt(cellStep * 16f); // 17
        int gridW = gm.Width;                   // 16
        int gridH = gm.Height;                  // n × 16

        // Créer le sprite (identique à FogOfWar.CreateWhiteSprite)
        if (_whiteSprite == null)
        {
            var tex = new Texture2D(4, 4);
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = Color.white;
            tex.SetPixels(px);
            tex.Apply();
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4),
                new Vector2(0.5f, 0.5f), 4f);
        }

        // Material partagé (identique à FogOfWar)
        if (_fogMaterial == null)
        {
            _fogMaterial = new Material(Shader.Find("Sprites/Default"));
            _fogMaterial.mainTexture = _whiteSprite.texture;
        }

        Color fogColor = new Color(_fogColor.r, _fogColor.g, _fogColor.b, _fogAlpha);

        // ── Créer les overlays ───────────────────────────────────────────────
        for (int x = 0; x < gridW; x++)
        {
            for (int y = 0; y < gridH; y++)
            {
                // Position identique à FogOfWar
                float wx = (x * stepPx) / 16f;
                float wy = (y * stepPx) / 16f;

                // Vérifier si cette case est un sol indoor (walkable)
                // Utiliser WorldToGrid pour convertir la position monde → coords tilemap
                Vector3 worldCenter = new Vector3(wx, wy, 0f);
                Vector2Int tileCoord = grid.WorldToGrid(worldCenter);

                bool isIndoorFloor = grid.IsInBounds(tileCoord.x, tileCoord.y)
                                  && !grid.IsBlocked(tileCoord.x, tileCoord.y);

                if (isIndoorFloor)
                    continue; // Pas de fog → les tilemaps sont visibles

                // Créer l'overlay fog (identique à FogOfWar.GetOrCreateOverlay)
                var go = new GameObject("IF");
                go.transform.position = new Vector3(wx, wy, -0.05f);
                go.transform.localScale = new Vector3(cellSize, cellSize, 1f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _whiteSprite;
                sr.sharedMaterial = _fogMaterial;
                sr.color = fogColor;
                sr.sortingLayerName = _sortingLayer;
                sr.sortingOrder = _sortingOrder;

                go.transform.SetParent(transform);
                _overlays.Add(sr);
            }
        }

        Debug.Log($"[IndoorFog] {_overlays.Count} overlays fog créés " +
                  $"({gridW}×{gridH} cases, stepPx={stepPx}, cellSize={cellSize:F4})");
    }

    public void Clear()
    {
        foreach (var sr in _overlays)
            if (sr != null) Destroy(sr.gameObject);
        _overlays.Clear();
    }

    private void OnDestroy()
    {
        Clear();
        if (_fogMaterial != null) Destroy(_fogMaterial);
        if (_whiteSprite != null)
        {
            Destroy(_whiteSprite.texture);
            Destroy(_whiteSprite);
        }
    }
}
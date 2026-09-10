using UnityEngine;

/// <summary>
/// UndergroundVisuals — Spawne les sprites de la grotte.
///
/// Pour chaque case du layout :
///   Sol          → UndergroundCellView (caché, révélable par clic)
///   Mur contour  → UndergroundCellView (toujours visible, non cliquable)
///   Roche profonde → rien (couvert par le fog)
///
/// Placé dans la scène Underground, à côté de CaveGenerator.
/// </summary>
public class UndergroundVisuals : MonoBehaviour
{
    [Header("Références")]
    [SerializeField] private GameObject _cellPrefab;
    [SerializeField] private CaveSpriteSet _spriteSet;

    [Header("Config")]
    [Tooltip("Marge autour du sol : combien de rangées de murs contour à afficher")]
    [SerializeField, Range(1, 3)] private int _wallMargin = 1;

    private GameObject _cellParent;

    /// <summary>
    /// Spawne toutes les cell views pour le layout donné.
    /// Appelé par CaveGenerator après la génération.
    /// </summary>
    public void SpawnViews(CaveLayout layout, UndergroundGrid grid, CaveSpriteSet spriteSet = null)
    {
        Clear();

        // Utiliser le spriteSet passé en paramètre OU celui de l'Inspector
        if (spriteSet != null) _spriteSet = spriteSet;

        if (_spriteSet == null)
        {
            Debug.LogError("[UndergroundVisuals] ❌ CaveSpriteSet non assigné !");
            return;
        }

        float cs = grid.CellStep;
        float cellSize = grid.CellSize; // taille du sprite (sans gap)
        _cellParent = new GameObject("CaveCells");
        _cellParent.transform.SetParent(transform);

        int spawned = 0;

        for (int x = 0; x < layout.Width; x++)
            for (int y = 0; y < layout.Height; y++)
            {
                bool isFloor = layout.IsFloor(x, y);
                bool isContour = CaveAutoTiler.IsWallContour(layout, x, y);

                // Roche profonde → skip (fog la couvre)
                if (!isFloor && !isContour) continue;

                // Position monde
                Vector3 worldPos = grid.GridToWorld(x, y);

                // Créer la cell view
                GameObject go = new GameObject($"C_{x}_{y}");
                go.transform.position = new Vector3(worldPos.x, worldPos.y, worldPos.z);
                go.transform.localScale = new Vector3(cellSize, cellSize, 1f);
                go.transform.SetParent(_cellParent.transform);

                // SpriteRenderer (AVANT d'ajouter CellViewBase qui fait GetComponent dans Awake)
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingLayerName = "CellContent";
                sr.sortingOrder = isFloor ? 0 : 1; // murs au-dessus du sol
                sr.sprite = _spriteSet.hiddenRock; // sprite initial visible

                // Collider pour le clic
                var col = go.AddComponent<BoxCollider2D>();
                col.size = Vector2.one * 0.9f;

                // CellView APRÈS le SpriteRenderer
                var cellView = go.AddComponent<UndergroundCellView>();

                var cell = grid.GetCell(x, y);
                if (cell != null)
                {
                    cellView.InitializeCave(cell, layout, _spriteSet);
                    spawned++;
                }
            }

        Debug.Log($"[UndergroundVisuals] ✅ {spawned} cell views spawnées.");
    }

    public void Clear()
    {
        // Détruire TOUS les enfants CaveCells (évite l'accumulation)
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (child.name == "CaveCells")
            {
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }
        _cellParent = null;
    }
}
using UnityEngine;

/// <summary>
/// UndergroundVisuals — Spawne les sprites de la grotte.
/// Supporte le mode additif : SpawnViewsAdditive n'efface pas les vues existantes.
/// </summary>
public class UndergroundVisuals : MonoBehaviour
{
    [Header("Références")]
    [SerializeField] private GameObject _cellPrefab;
    [SerializeField] private CaveSpriteSet _spriteSet;

    [Header("Config")]
    [SerializeField, Range(1, 3)] private int _wallMargin = 1;

    private GameObject _cellParent;

    /// <summary>Spawn normal — efface puis recrée toutes les vues.</summary>
    public void SpawnViews(CaveLayout layout, UndergroundGrid grid,
                           CaveSpriteSet spriteSet = null, Transform cellParent = null)
    {
        // Clear UNIQUEMENT si pas de parent spécifique (sinon le spawner gère son clear)
        if (cellParent == null) Clear();
        SpawnViewsInternal(layout, grid, spriteSet, cellParent);
    }

    /// <summary>Spawn additif — garde les vues existantes.</summary>
    public void SpawnViewsAdditive(CaveLayout layout, UndergroundGrid grid,
                                   CaveSpriteSet spriteSet = null, Transform cellParent = null)
    {
        SpawnViewsInternal(layout, grid, spriteSet, cellParent);
    }

    private void SpawnViewsInternal(CaveLayout layout, UndergroundGrid grid,
                                    CaveSpriteSet spriteSet, Transform parent)
    {
        if (spriteSet != null) _spriteSet = spriteSet;
        if (_spriteSet == null) return;

        float cs = grid.CellStep;
        float cellSize = grid.CellSize;

        // Parent : CaveSpawner.CellsContainer si fourni, sinon _cellParent par défaut
        Transform actualParent = parent;
        if (actualParent == null)
        {
            if (_cellParent == null)
            {
                _cellParent = new GameObject("CaveCells");
                _cellParent.transform.SetParent(transform);
            }
            actualParent = _cellParent.transform;
        }

        int spawned = 0;

        for (int x = 0; x < layout.Width; x++)
            for (int y = 0; y < layout.Height; y++)
            {
                bool isFloor = layout.IsFloor(x, y);
                bool isContour = CaveAutoTiler.IsWallContour(layout, x, y);

                if (!isFloor && !isContour) continue;

                // Vérifier qu'il n'y a pas déjà une cell view à cette position
                // (évite les doublons en mode additif)
                var cell = grid.GetCell(x, y);
                if (cell == null) continue;
                if (HasExistingView(x, y)) continue;

                Vector3 worldPos = grid.GridToWorld(x, y);

                GameObject go = new GameObject($"C_{x}_{y}");
                go.transform.position = new Vector3(worldPos.x, worldPos.y, worldPos.z);
                go.transform.localScale = new Vector3(cellSize, cellSize, 1f);
                go.transform.SetParent(actualParent);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingLayerName = "CellContent";
                sr.sortingOrder = isFloor ? 0 : 1;
                sr.sprite = _spriteSet.hiddenRock;

                var col = go.AddComponent<BoxCollider2D>();
                col.size = Vector2.one * 0.9f;

                var cellView = go.AddComponent<UndergroundCellView>();
                cellView.InitializeCave(cell, layout, _spriteSet);
                spawned++;
            }

        Debug.Log($"[UndergroundVisuals] ✅ {spawned} cell views spawnées.");
    }

    /// <summary>Vérifie si une cell view existe déjà à cette position grille.</summary>
    private bool HasExistingView(int x, int y)
    {
        if (_cellParent == null) return false;
        string name = $"C_{x}_{y}";
        foreach (Transform child in _cellParent.transform)
            if (child.name == name) return true;
        return false;
    }

    public void Clear()
    {
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
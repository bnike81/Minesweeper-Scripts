using UnityEngine;

/// <summary>
/// CaveGenerator — Orchestre la génération de la grotte.
/// Pas de yOffset — CaveLayout place ses portails directement aux Y surface.
/// </summary>
public class CaveGenerator : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] public CaveConfig _config;

    [Header("Références")]
    [SerializeField] private UndergroundGrid _undergroundGrid;
    [SerializeField] private UndergroundVisuals _visuals;
    [SerializeField] private UndergroundFog _fog;
    [SerializeField] private CaveSpriteSet _spriteSet;

    public CaveLayout Layout { get; private set; }

    public void Generate(int entryX, int entryY, int exitX, int exitY,
                         bool isTwoWay = false, bool additive = false,
                         Transform cellParent = null)
    {
        if (_config == null) { Debug.LogError("[CaveGenerator] ❌ CaveConfig null !"); return; }

        if (!additive)
        {
            _visuals?.Clear();
            _fog?.Clear();
            _undergroundGrid?.Clear();
        }

        Layout = new CaveLayout(_config, entryX, entryY, exitX, exitY, isTwoWay);

        if (_undergroundGrid == null) _undergroundGrid = FindObjectOfType<UndergroundGrid>(true);
        if (_undergroundGrid == null) { Debug.LogError("[CaveGenerator] ❌ UndergroundGrid !"); return; }

        if (additive)
            _undergroundGrid.MergeFromLayout(Layout, entryY);
        else
            _undergroundGrid.GenerateFromLayout(Layout, entryY);

        if (_visuals == null) _visuals = GetComponentInChildren<UndergroundVisuals>(true);
        if (_visuals == null) _visuals = FindObjectOfType<UndergroundVisuals>(true);

        // cellParent fourni par CaveManager → cells dans CaveSpawner.CellsContainer
        if (additive)
            _visuals?.SpawnViewsAdditive(Layout, _undergroundGrid, _spriteSet, cellParent);
        else
            _visuals?.SpawnViews(Layout, _undergroundGrid, _spriteSet, cellParent);

        if (_fog == null) _fog = GetComponentInChildren<UndergroundFog>(true);
        if (_fog == null) _fog = FindObjectOfType<UndergroundFog>(true);
        if (additive)
            _fog?.Extend(_undergroundGrid, Layout);
        else
            _fog?.Generate(_undergroundGrid);

        Debug.Log($"[CaveGenerator] ✅ {Layout.Width}×{Layout.Height} additive={additive} " +
                  $"Entry=({Layout.EntryPos.x},{Layout.EntryPos.y}) " +
                  $"Exit=({Layout.ExitPos.x},{Layout.ExitPos.y})");
    }

    public void Generate(int entryX, int exitX, bool isTwoWay = false)
        => Generate(entryX, 0, exitX, 0, isTwoWay);

    [ContextMenu("Test Traversée")] public void TestTraversee() => Generate(5, 10, 12, 22, false);
    [ContextMenu("Test Aller-retour")] public void TestAllerRetour() => Generate(7, 10, 7, 10, true);

    [Header("Debug Gizmos")]
    [SerializeField] private bool _showGizmos = true;

    private void OnDrawGizmosSelected()
    {
        if (Layout == null || !_showGizmos) return;
        float cs = GridManager.Instance?.CellStep ?? 1.063f;
        float oy = _undergroundGrid != null ? _undergroundGrid.GridToWorld(0, 0).y : 0f;

        for (int x = 0; x < Layout.Width; x++)
            for (int y = 0; y < Layout.Height; y++)
            {
                Vector3 pos = new Vector3(x * cs + cs * 0.5f, oy + y * cs, 0f);
                Vector3 size = new Vector3(cs * 0.9f, cs * 0.9f, 0.1f);

                if (CaveAutoTiler.IsPortalCell(Layout, x, y))
                { Gizmos.color = Color.blue; Gizmos.DrawCube(pos, size); continue; }
                if (!Layout.IsFloor(x, y))
                { Gizmos.color = new Color(0.3f, 0.3f, 0.3f, 0.5f); Gizmos.DrawCube(pos, size); continue; }
                Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.7f);
                Gizmos.DrawCube(pos, size);
            }
    }
}
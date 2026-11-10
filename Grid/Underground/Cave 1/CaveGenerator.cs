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

    /// <summary>
    /// Gizmos couleur par type d'élément :
    ///   🔵 Bleu vif       = Portail (entrée/sortie)
    ///   🔴 Rouge           = Bord portail protégé (hors-limite forcé)
    ///   🟡 Jaune           = Grande salle (village goblin)
    ///   🟢 Vert            = Petite salle
    ///   🔵 Cyan            = Couloir de connexion
    ///   🟣 Magenta         = Impasse (dead end)
    ///   ⬛ Gris foncé      = Mur contour (adjacent au sol)
    ///   ⬛ Gris très foncé = Roche profonde (pas de sol adjacent)
    ///   ⬜ Blanc cassé     = Sol non typé (résidu de connectivité)
    /// </summary>
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
                var cellType = Layout.GetCellType(x, y);

                // ── Portails (priorité absolue) ──
                if (CaveAutoTiler.IsPortalCell(Layout, x, y))
                {
                    Gizmos.color = new Color(0.1f, 0.3f, 1f, 1f);   // bleu vif
                    Gizmos.DrawCube(pos, size);
                    continue;
                }

                // ── Bords portail protégés (hors-limite forcé) ──
                if (cellType == CaveCellType.PortalEdge)
                {
                    Gizmos.color = new Color(1f, 0.15f, 0.15f, 0.9f); // rouge
                    Gizmos.DrawCube(pos, size);
                    continue;
                }

                // ── Cases non-sol (murs) ──
                if (!Layout.IsFloor(x, y))
                {
                    bool isContour = CaveAutoTiler.IsWallContour(Layout, x, y);
                    Gizmos.color = isContour
                        ? new Color(0.45f, 0.45f, 0.45f, 0.6f)   // gris clair = mur contour
                        : new Color(0.2f, 0.2f, 0.2f, 0.35f);    // gris foncé = roche profonde
                    Gizmos.DrawCube(pos, size);
                    continue;
                }

                // ── Cases sol — couleur par type ──
                switch (cellType)
                {
                    case CaveCellType.LargeRoom:
                        Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.85f);  // jaune doré
                        break;
                    case CaveCellType.SmallRoom:
                        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.8f);  // vert
                        break;
                    case CaveCellType.Corridor:
                        Gizmos.color = new Color(0.1f, 0.85f, 0.9f, 0.8f); // cyan
                        break;
                    case CaveCellType.DeadEnd:
                        Gizmos.color = new Color(0.85f, 0.2f, 0.85f, 0.8f); // magenta
                        break;
                    default:
                        Gizmos.color = new Color(0.9f, 0.9f, 0.85f, 0.6f); // blanc cassé = sol non typé
                        break;
                }
                Gizmos.DrawCube(pos, size);
            }

        // ── Légende dans la console (une seule fois par repaint) ──
        // Les couleurs sont documentées dans le commentaire XML ci-dessus.
    }
}
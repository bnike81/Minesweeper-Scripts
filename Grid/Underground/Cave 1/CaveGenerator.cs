using UnityEngine;

/// <summary>
/// CaveGenerator — Orchestre la génération de la grotte et crée les cells
/// dans UndergroundGrid.
///
/// FLOW :
///   1. CaveManager.EnterCave() → CaveGenerator.Generate(entryPortal, exitPortal)
///   2. CaveLayout génère le plan sol/mur
///   3. CaveGenerator crée les Cell[,] dans UndergroundGrid
///   4. CaveAutoTiler assigne les sprites (phase 2 - à venir)
///   5. UndergroundFog crée le brouillard dense
///
/// Placé sur le même GO que CaveManager dans la scène Underground.
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

    // Layout généré (accessible pour le village goblin, spawns, etc.)
    public CaveLayout Layout { get; private set; }

    /// <summary>
    /// Génère la grotte entre deux portails de montagne.
    /// entryPortalX : position X du portail d'entrée en MainGrid
    /// exitPortalX  : position X du portail de sortie en MainGrid
    /// </summary>
    public void Generate(int entryPortalX, int exitPortalX)
    {
        if (_config == null)
        {
            Debug.LogError("[CaveGenerator] ❌ CaveConfig non assigné !");
            return;
        }

        // ── Nettoyage avant génération (évite l'accumulation) ─────────────────
        _visuals?.Clear();
        _fog?.Clear();
        _undergroundGrid?.Clear();

        // ── Phase 1 : Layout structurel ──────────────────────────────────────
        Layout = new CaveLayout(
            _config,
            new Vector2Int(entryPortalX, 0),
            new Vector2Int(exitPortalX, _config.height - 1));

        // ── Phase 2 : Créer les cells dans UndergroundGrid ───────────────────
        if (_undergroundGrid == null)
            _undergroundGrid = FindObjectOfType<UndergroundGrid>(true);

        if (_undergroundGrid != null)
        {
            _undergroundGrid.GenerateFromLayout(Layout);

            // ── Phase 3 : Spawner les visuels (cell views) ───────────────────
            if (_visuals == null)
                _visuals = GetComponentInChildren<UndergroundVisuals>(true);
            if (_visuals == null)
                _visuals = FindObjectOfType<UndergroundVisuals>(true);
            _visuals?.SpawnViews(Layout, _undergroundGrid, _spriteSet);

            // ── Phase 4 : Fog of War underground ─────────────────────────────
            if (_fog == null)
                _fog = GetComponentInChildren<UndergroundFog>(true);
            if (_fog == null)
                _fog = FindObjectOfType<UndergroundFog>(true);
            _fog?.Generate(_undergroundGrid);

            Debug.Log($"[CaveGenerator] ✅ Grotte générée : {_config.width}×{_config.height}, " +
                      $"{Layout.Rooms.Count} salles, " +
                      $"entrée=({Layout.EntryPos.x},0), " +
                      $"sortie=({Layout.ExitPos.x},{_config.height - 1})");
        }
        else
        {
            Debug.LogError("[CaveGenerator] ❌ UndergroundGrid non trouvé !");
        }
    }

    /// <summary>Test rapide depuis l'Inspector : clic droit → Test Generate Cave.</summary>
    [ContextMenu("Test Generate Cave")]
    public void TestGenerate()
    {
        Generate(7, 9);
        Debug.Log("[CaveGenerator] Test terminé — sélectionne ce GO et regarde les Gizmos dans Scene View.");
    }

    [Header("Debug Gizmos")]
    [SerializeField] private bool _showTileTypes = true;

    /// <summary>Debug : dessine le layout avec couleurs par type de tuile.</summary>
    private void OnDrawGizmosSelected()
    {
        if (Layout == null) return;

        float cs = GridManager.Instance?.CellStep ?? 1.063f;
        float originY = _undergroundGrid != null ? -500f : 0f;

        for (int x = 0; x < Layout.Width; x++)
            for (int y = 0; y < Layout.Height; y++)
            {
                Vector3 pos = new Vector3(x * cs + cs * 0.5f, originY + y * cs + cs * 0.5f, 0f);
                Vector3 size = new Vector3(cs * 0.9f, cs * 0.9f, 0.1f);

                if (!Layout.IsFloor(x, y))
                {
                    // Mur contour (adjacent au sol) → marron visible
                    if (CaveAutoTiler.IsWallContour(Layout, x, y))
                    {
                        string wType = CaveAutoTiler.GetTileTypeName(Layout, x, y);
                        if (wType.StartsWith("EDGE")) Gizmos.color = new Color(0.6f, 0.4f, 0.2f, 0.7f);
                        else if (wType.StartsWith("CORNER")) Gizmos.color = new Color(0.7f, 0.3f, 0.15f, 0.7f);
                        else if (wType.StartsWith("INNER")) Gizmos.color = new Color(0.5f, 0.3f, 0.5f, 0.7f);
                        else Gizmos.color = new Color(0.5f, 0.35f, 0.2f, 0.6f);
                        Gizmos.DrawCube(pos, size);
                    }
                    // Roche profonde → invisible (juste fog)
                    continue;
                }

                if (_showTileTypes)
                {
                    // Couleur selon le type de tuile auto-tilée
                    string type = CaveAutoTiler.GetTileTypeName(Layout, x, y);
                    if (type == "FLOOR") Gizmos.color = new Color(0.5f, 0.7f, 0.4f, 0.6f);  // vert
                    else if (type.StartsWith("EDGE")) Gizmos.color = new Color(0.7f, 0.5f, 0.3f, 0.7f); // orange
                    else if (type.StartsWith("CORNER")) Gizmos.color = new Color(0.8f, 0.3f, 0.3f, 0.7f); // rouge
                    else if (type.StartsWith("INNER")) Gizmos.color = new Color(0.3f, 0.5f, 0.8f, 0.7f); // bleu
                    else if (type.StartsWith("CORR")) Gizmos.color = new Color(0.6f, 0.6f, 0.3f, 0.6f); // jaune
                    else Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.5f); // gris
                }
                else
                {
                    // Couleur par zone (salles vs couloirs)
                    Gizmos.color = new Color(0.4f, 0.6f, 0.3f, 0.5f);
                    foreach (var room in Layout.Rooms)
                    {
                        if (room.Contains(new Vector2Int(x, y)))
                        {
                            Gizmos.color = room == Layout.LargeRoom
                                ? new Color(0.8f, 0.3f, 0.2f, 0.5f)
                                : new Color(0.3f, 0.5f, 0.8f, 0.5f);
                            break;
                        }
                    }
                }

                Gizmos.DrawCube(pos, size);
            }

        // Portails
        Gizmos.color = Color.yellow;
        float pw = (_config?.portalWidth ?? 2) * cs;
        Gizmos.DrawCube(
            new Vector3(Layout.EntryPos.x * cs + pw * 0.5f, originY + cs * 0.5f, 0f),
            new Vector3(pw, cs, 0.1f));
        Gizmos.DrawCube(
            new Vector3(Layout.ExitPos.x * cs + pw * 0.5f,
                originY + (Layout.Height - 1) * cs + cs * 0.5f, 0f),
            new Vector3(pw, cs, 0.1f));
    }
}
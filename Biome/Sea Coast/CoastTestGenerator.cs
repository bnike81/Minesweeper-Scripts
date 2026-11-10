using UnityEngine;

/// <summary>
/// CoastTestGenerator -- Test en mode Edit avec Gizmos (pas de GameObjects).
///
/// Aligne sur le pattern MountainTestGenerator :
///   - Aucun GameObject cree (Gizmos uniquement)
///   - Clear() = null sur les donnees
///   - Independant de CoastGenerator
///   - Seed random pour variete des generations
///
/// [ContextMenu "Generate Coast"] pour generer sans jouer.
/// Dessine les pieces dans la Scene View avec couleurs par type.
///
/// Placer sur un GO enfant separe de CoastGenerator.
/// </summary>
public class CoastTestGenerator : MonoBehaviour
{
    [Header("Test Settings")]
    [SerializeField] private CoastConfig _config;

    [Header("Grille test")]
    [SerializeField, Range(8, 40)] private int _testGridWidth = 20;
    [SerializeField, Range(8, 40)] private int _testGridHeight = 20;
    [SerializeField] private float _testCellStep = 1.0625f;

    [Header("Debug")]
    [SerializeField] private bool _showDebugGrid = true;
    [SerializeField] private bool _logLayout = true;

    private CoastLayout _layout;

    [ContextMenu("Generate Coast")]
    public void GenerateTest()
    {
        Clear();

        if (_config == null)
        {
            Debug.LogError("[CoastTest] Assigner un CoastConfig !");
            return;
        }

        // Dimensions : config overrides > champs Inspector test
        int gw = _config.coastGridWidth > 0 ? _config.coastGridWidth : _testGridWidth;
        int gh = _config.coastGridHeight > 0 ? _config.coastGridHeight : _testGridHeight;

        // Meme chemin que CoastGenerator — factory partagee
        _layout = CoastLayout.CreateAndGenerate(_config, gw, gh);

        if (_logLayout)
            LogLayout();

        Debug.Log("[CoastTest] Layout genere -- voir Gizmos dans Scene View");
    }

    [ContextMenu("Clear Coast")]
    public void Clear()
    {
        _layout = null;
    }

    [ContextMenu("Regenerate (New Seed)")]
    public void RegenerateNewSeed()
    {
        if (_config != null)
            _config.seed = 0;
        GenerateTest();
    }

    private float ResolveCellStep()
    {
        // Priorite GridManager (comme MountainTestGenerator)
        if (GridManager.Instance != null) return GridManager.Instance.CellStep;
        return _testCellStep;
    }

    private void LogLayout()
    {
        Debug.Log("[CoastTest] === Layout " + _layout.ResolvedSide + " ===");
        Debug.Log("[CoastTest] Grid: " + _layout.GridWidth + "x" + _layout.GridHeight);
        Debug.Log("[CoastTest] Start: " + _layout.EffectiveStart + ", Length: " + _layout.EffectiveLength);

        var sb = new System.Text.StringBuilder("[CoastTest] Depths: ");
        for (int i = 0; i < _layout.Depths.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(_layout.Depths[i]);
        }
        Debug.Log(sb.ToString());

        int waters = 0, edges = 0, corners = 0, inners = 0;
        for (int x = 0; x < _layout.GridWidth; x++)
            for (int y = 0; y < _layout.GridHeight; y++)
            {
                var t = _layout.CellTypes[x, y];
                if (t == CoastCellType.Water) waters++;
                else if (t >= CoastCellType.EdgeS && t <= CoastCellType.EdgeW) edges++;
                else if (t >= CoastCellType.CornerNW && t <= CoastCellType.CornerSE) corners++;
                else if (t >= CoastCellType.InnerNW && t <= CoastCellType.InnerSE) inners++;
            }

        Debug.Log("[CoastTest] Pieces: Water=" + waters + " Edge=" + edges + " Corner=" + corners + " Inner=" + inners);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!_showDebugGrid || _layout == null) return;

        float cs = ResolveCellStep();
        int stepPx = Mathf.RoundToInt(cs * 16f);

        // Grille de terre (fond)
        Gizmos.color = new Color(0.3f, 0.3f, 0.3f, 0.15f);
        for (int x = 0; x < _layout.GridWidth; x++)
            for (int y = 0; y < _layout.GridHeight; y++)
            {
                if (_layout.IsWater[x, y]) continue;
                Vector3 center = new Vector3((x * stepPx) / 16f, (y * stepPx) / 16f, 0f);
                Gizmos.DrawWireCube(center, Vector3.one * cs * 0.95f);
            }

        // Pieces cotieres (coloriees par type)
        for (int x = 0; x < _layout.GridWidth; x++)
            for (int y = 0; y < _layout.GridHeight; y++)
            {
                var type = _layout.CellTypes[x, y];
                if (type == CoastCellType.None) continue;

                switch (type)
                {
                    case CoastCellType.Water:
                        Gizmos.color = new Color(0f, 0.3f, 1f, 0.4f); break;
                    case CoastCellType.EdgeS:
                    case CoastCellType.EdgeN:
                    case CoastCellType.EdgeE:
                    case CoastCellType.EdgeW:
                        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.6f); break;
                    case CoastCellType.CornerNW:
                    case CoastCellType.CornerNE:
                    case CoastCellType.CornerSW:
                    case CoastCellType.CornerSE:
                        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f); break;
                    case CoastCellType.InnerNW:
                    case CoastCellType.InnerNE:
                    case CoastCellType.InnerSW:
                    case CoastCellType.InnerSE:
                        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.6f); break;
                    default:
                        Gizmos.color = Color.clear; break;
                }

                Vector3 center = new Vector3((x * stepPx) / 16f, (y * stepPx) / 16f, 0f);
                Gizmos.DrawCube(center, Vector3.one * cs * 0.85f);

                UnityEditor.Handles.Label(center + Vector3.up * cs * 0.3f,
                    type.ToString(),
                    new GUIStyle
                    {
                        fontSize = 8,
                        normal = { textColor = Color.white },
                        alignment = TextAnchor.MiddleCenter
                    });
            }

        // Courbe de profondeur (ligne rouge)
        if (_layout.Depths != null)
        {
            Gizmos.color = Color.red;
            for (int i = 1; i < _layout.Depths.Length; i++)
            {
                Vector3 prev, curr;
                int pos0 = _layout.EffectiveStart + i - 1;
                int pos1 = _layout.EffectiveStart + i;

                if (CoastLayout.IsVertical(_layout.ResolvedSide))
                {
                    float xOff = _layout.ResolvedSide == CoastSide.West
                        ? (_layout.Depths[i - 1] * stepPx) / 16f
                        : ((_layout.GridWidth - _layout.Depths[i - 1]) * stepPx) / 16f;
                    float xOff2 = _layout.ResolvedSide == CoastSide.West
                        ? (_layout.Depths[i] * stepPx) / 16f
                        : ((_layout.GridWidth - _layout.Depths[i]) * stepPx) / 16f;

                    prev = new Vector3(xOff, (pos0 * stepPx) / 16f, -0.1f);
                    curr = new Vector3(xOff2, (pos1 * stepPx) / 16f, -0.1f);
                }
                else
                {
                    prev = new Vector3((pos0 * stepPx) / 16f, (_layout.Depths[i - 1] * stepPx) / 16f, -0.1f);
                    curr = new Vector3((pos1 * stepPx) / 16f, (_layout.Depths[i] * stepPx) / 16f, -0.1f);
                }

                Gizmos.DrawLine(prev, curr);
            }
        }
    }
#endif
}
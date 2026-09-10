using UnityEngine;

/// <summary>
/// GridLines - Dessine les lignes de grille entre les cases.
/// Un seul mesh de lignes grises en background, pixel perfect.
/// Pas de gap physique entre les cases — espacement purement visuel.
/// </summary>
[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class GridLines : MonoBehaviour
{
    [Header("=== Apparence ===")]
    [SerializeField] private Color _lineColor = new Color(0.25f, 0.25f, 0.25f, 1f);
    [SerializeField] private string _sortingLayer = "Default";
    [SerializeField] private int _sortingOrder = -1; // derriere les cases

    [Header("=== Paramètres ===")]
    [Tooltip("Epaisseur des lignes en unités Unity (0.0625 = 1px avec PPU=16)")]
    [SerializeField] private float _lineThickness = 0.0625f;

    private MeshFilter _filter;
    private MeshRenderer _renderer;
    private Mesh _mesh;

    // Buffers statiques — évite allocations à chaque reconstruction du mesh
    private static readonly System.Collections.Generic.List<Vector3> _verticesBuf
        = new System.Collections.Generic.List<Vector3>(1024);
    private static readonly System.Collections.Generic.List<int> _trianglesBuf
        = new System.Collections.Generic.List<int>(2048);
    private static Vector3 _v0, _v1, _v2, _v3;

    private Material _gridMaterial;

    private void Awake()
    {
        _filter = GetComponent<MeshFilter>();
        _renderer = GetComponent<MeshRenderer>();

        _gridMaterial = new Material(Shader.Find("Sprites/Default"));
        _gridMaterial.color = _lineColor;
        _renderer.material = _gridMaterial;
        _renderer.sortingLayerName = _sortingLayer;
        _renderer.sortingOrder = _sortingOrder;
    }

    private void OnDestroy()
    {
        if (_gridMaterial != null) Destroy(_gridMaterial);
        if (_mesh != null) Destroy(_mesh);
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnGridGenerated>(OnGridGenerated);
        EventBus.Subscribe<OnGridExtended>(OnGridExtended);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnGridGenerated>(OnGridGenerated);
        EventBus.Unsubscribe<OnGridExtended>(OnGridExtended);
    }

    private void OnGridGenerated(OnGridGenerated e) => BuildMesh(e.Width, e.Height);
    private void OnGridExtended(OnGridExtended e)
    {
        var gm = GridManager.Instance;
        if (gm != null) BuildMesh(gm.Width, gm.Height);
    }

    // =========================================================================
    // CONSTRUCTION DU MESH
    // =========================================================================

    public void BuildMesh(int cols, int rows)
    {
        var gm = GridManager.Instance;
        if (gm == null) return;

        float cs = gm.CellStep;   // = 1.0 avec spacing=0
        float half = _lineThickness * 0.5f;

        // Buffers statiques réutilisés — évite allocations à chaque extension
        _verticesBuf.Clear();
        _trianglesBuf.Clear();
        var vertices = _verticesBuf;
        var triangles = _trianglesBuf;

        // ── Lignes VERTICALES (contour de chaque case) ──────────────────────
        // De x=0 à x=cols (bords inclus)
        float totalH = rows * cs;
        for (int x = 0; x <= cols; x++)
        {
            float wx = x * cs - half; // bord gauche de la ligne
            AddRect(vertices, triangles,
                wx, 0f - half,
                wx + _lineThickness, totalH + half);
        }

        // ── Lignes HORIZONTALES (contour de chaque case) ─────────────────────
        float totalW = cols * cs;
        for (int y = 0; y <= rows; y++)
        {
            float wy = y * cs - half;
            AddRect(vertices, triangles,
                0f - half, wy,
                totalW + half, wy + _lineThickness);
        }

        // ── Construire le mesh ───────────────────────────────────────────────
        if (_mesh == null)
            _mesh = new Mesh { name = "GridLines" };
        else
            _mesh.Clear();

        _mesh.SetVertices(vertices);
        _mesh.SetTriangles(triangles, 0);
        _mesh.RecalculateBounds();
        _filter.mesh = _mesh;

        // Positionner à z=0.01 (juste derrière les sprites de cases)
        transform.position = new Vector3(0f, 0f, 0.01f);
    }

    // =========================================================================
    // HELPER - Ajoute un rectangle au mesh
    // =========================================================================

    private void AddRect(
        System.Collections.Generic.List<Vector3> verts,
        System.Collections.Generic.List<int> tris,
        float x0, float y0, float x1, float y1)
    {
        int i = verts.Count;
        _v0.Set(x0, y0, 0f); verts.Add(_v0);
        _v1.Set(x1, y0, 0f); verts.Add(_v1);
        _v2.Set(x1, y1, 0f); verts.Add(_v2);
        _v3.Set(x0, y1, 0f); verts.Add(_v3);
        tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
        tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
    }
}
using UnityEngine;

/// <summary>
/// StageCellView — vue PROVISOIRE d une case de stage (étape 2).
///
/// Un pavé coloré selon le terrain, le biome et l état, plus le chiffre du
/// démineur. Sert à tester la logique des stages avant le branchement des
/// vraies vues de biome (ForestCellView, BeachCellView...) à l étape 3.
///
/// Lecture des couleurs :
///   vert sombre / vert palmier / violet gris  = case cachée (forêt / plage / grotte)
///   vert clair / sable / gris                 = sol révélé
///   bleu = eau      brun = montagne      presque noir = mur de grotte
/// Un ennemi caché ne se voit pas : la case ressemble à n importe quel arbre.
/// </summary>
public class StageCellView : MonoBehaviour
{
    private static Sprite _sharedSprite;

    private static readonly Color WaterColor = new Color(0.20f, 0.45f, 0.80f);
    private static readonly Color MountainColor = new Color(0.42f, 0.34f, 0.27f);
    private static readonly Color CaveWallColor = new Color(0.13f, 0.12f, 0.15f);

    private static readonly Color ForestHidden = new Color(0.12f, 0.36f, 0.15f);
    private static readonly Color ForestGround = new Color(0.58f, 0.76f, 0.42f);
    private static readonly Color BeachHidden = new Color(0.30f, 0.55f, 0.25f);
    private static readonly Color BeachGround = new Color(0.91f, 0.83f, 0.60f);
    private static readonly Color CaveHidden = new Color(0.27f, 0.23f, 0.33f);
    private static readonly Color CaveGround = new Color(0.52f, 0.48f, 0.44f);

    // Couleurs classiques des chiffres du démineur (1 à 8)
    private static readonly Color[] NumberColors =
    {
        new Color(0.10f, 0.25f, 0.85f), new Color(0.05f, 0.45f, 0.10f),
        new Color(0.80f, 0.10f, 0.10f), new Color(0.10f, 0.10f, 0.50f),
        new Color(0.50f, 0.10f, 0.10f), new Color(0.05f, 0.45f, 0.45f),
        new Color(0.10f, 0.10f, 0.10f), new Color(0.40f, 0.40f, 0.40f)
    };

    private Cell _cell;
    private SpriteRenderer _sr;
    private TMPro.TextMeshPro _label;

    public void Setup(Cell cell, float cellSize)
    {
        _cell = cell;

        _sr = GetComponent<SpriteRenderer>();
        if (_sr == null) _sr = gameObject.AddComponent<SpriteRenderer>();
        if (_sharedSprite == null) _sharedSprite = CreateSquareSprite();

        _sr.sprite = _sharedSprite;
        _sr.sortingLayerName = "Floor";
        _sr.sortingOrder = 0;

        transform.localScale = new Vector3(cellSize, cellSize, 1f);
        Refresh();
    }

    /// <summary>Met la vue à jour d après l état actuel de la case.</summary>
    public void Refresh()
    {
        if (_cell == null || _sr == null) return;

        _sr.color = PickColor(_cell);

        // Texte : chiffre sur une case révélée, « P » sur un piège révélé
        string text = null;
        Color textColor = Color.black;
        if (!_cell.IsObstacle && _cell.IsRevealed)
        {
            if (_cell.IsTrap)
            {
                text = "P";
                textColor = new Color(0.85f, 0.10f, 0.10f);
            }
            else if (_cell.IsNumber && _cell.AdjacentDangerCount > 0)
            {
                int n = Mathf.Clamp(_cell.AdjacentDangerCount, 1, 8);
                text = n.ToString();
                textColor = NumberColors[n - 1];
            }
        }

        if (text == null)
        {
            if (_label != null) _label.gameObject.SetActive(false);
            return;
        }

        if (_label == null) _label = CreateLabel();
        _label.gameObject.SetActive(true);
        _label.text = text;
        _label.color = textColor;
    }

    private static Color PickColor(Cell cell)
    {
        switch (cell.Terrain)
        {
            case TerrainType.Eau: return WaterColor;
            case TerrainType.Montagne: return MountainColor;
            case TerrainType.MurGrotte: return CaveWallColor;
        }

        bool revealed = cell.IsRevealed;
        switch (cell.Biome)
        {
            case BiomeType.Beach: return revealed ? BeachGround : BeachHidden;
            case BiomeType.Cave: return revealed ? CaveGround : CaveHidden;
            default: return revealed ? ForestGround : ForestHidden;
        }
    }

    private TMPro.TextMeshPro CreateLabel()
    {
        var go = new GameObject("Label");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, -0.05f);

        var label = go.AddComponent<TMPro.TextMeshPro>();
        label.fontSize = 6f;
        label.alignment = TMPro.TextAlignmentOptions.Center;
        label.fontStyle = TMPro.FontStyles.Bold;

        // Même réglage de tri que le label de nom des ennemis, mais en dessous d eux
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.sortingLayerName = "CellContent";
            mr.sortingOrder = 1;
        }
        return label;
    }

    /// <summary>Sprite blanc 4x4 partagé, teinté par SpriteRenderer.color.</summary>
    private static Sprite CreateSquareSprite()
    {
        var tex = new Texture2D(4, 4);
        var pixels = new Color[16];
        for (int i = 0; i < 16; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.filterMode = FilterMode.Point;
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
    }
}
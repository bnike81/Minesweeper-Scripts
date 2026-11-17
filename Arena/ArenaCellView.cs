using UnityEngine;

/// <summary>
/// ArenaCellView — Affichage visuel d une case d arène.
///
/// Chaque case = un GameObject avec SpriteRenderer rempli d une couleur unie.
/// Pas de biome, pas d animation, pas d overlay — juste un pavé coloré.
///
/// Instancié par CombatArena.BuildFloor() apres ArenaGrid.Build().
/// </summary>
public class ArenaCellView : MonoBehaviour
{
    private static Sprite _sharedFloorSprite;

    private int _x, _y;
    private SpriteRenderer _sr;

    /// <summary>
    /// Initialise la case : position monde, couleur, taille.
    /// Crée le SpriteRenderer si absent.
    /// </summary>
    public void Setup(int x, int y, float cellSize, Color color)
    {
        _x = x;
        _y = y;

        _sr = GetComponent<SpriteRenderer>();
        if (_sr == null) _sr = gameObject.AddComponent<SpriteRenderer>();

        if (_sharedFloorSprite == null)
            _sharedFloorSprite = CreateFloorSprite();

        _sr.sprite = _sharedFloorSprite;
        _sr.color = color;
        _sr.sortingLayerName = "Floor"; // meme layer que les tiles de MainGrid
        _sr.sortingOrder = 0;

        transform.localScale = new Vector3(cellSize, cellSize, 1f);
        gameObject.name = $"ArenaCell_{x}_{y}";
    }

    /// <summary>
    /// Crée un sprite blanc 4x4 pixels (teinté via SpriteRenderer.color).
    /// Partagé entre toutes les cases pour économiser la mémoire.
    /// </summary>
    private static Sprite CreateFloorSprite()
    {
        var tex = new Texture2D(4, 4);
        var pixels = new Color[16];
        for (int i = 0; i < 16; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.filterMode = FilterMode.Point; // pas d anti-aliasing (pixel-art)
        tex.Apply();
        return Sprite.Create(
            tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
    }
}
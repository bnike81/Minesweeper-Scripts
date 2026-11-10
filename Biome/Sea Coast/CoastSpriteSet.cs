using UnityEngine;

/// <summary>
/// CoastSpriteSet -- ScriptableObject contenant tous les sprites cotiers.
/// Chaque piece a 6 frames d'animation.
/// Creer : Assets > Create > MinesweeperRPG > Coast Sprite Set
/// </summary>
[CreateAssetMenu(fileName = "CoastSpriteSet", menuName = "MinesweeperRPG/Coast Sprite Set")]
public class CoastSpriteSet : ScriptableObject
{
    [Header("Water (pleine mer)")]
    public Sprite[] water = new Sprite[6];

    [Header("Edges (bord droit)")]
    public Sprite[] edgeS = new Sprite[6];
    public Sprite[] edgeN = new Sprite[6];
    public Sprite[] edgeE = new Sprite[6];
    public Sprite[] edgeW = new Sprite[6];

    [Header("Corners (convexe)")]
    public Sprite[] cornerNW = new Sprite[6];
    public Sprite[] cornerNE = new Sprite[6];
    public Sprite[] cornerSW = new Sprite[6];
    public Sprite[] cornerSE = new Sprite[6];

    [Header("Inners (concave)")]
    public Sprite[] innerNW = new Sprite[6];
    public Sprite[] innerNE = new Sprite[6];
    public Sprite[] innerSW = new Sprite[6];
    public Sprite[] innerSE = new Sprite[6];

    public Sprite[] GetFrames(CoastCellType type)
    {
        return type switch
        {
            CoastCellType.Water => water,
            CoastCellType.EdgeS => edgeS,
            CoastCellType.EdgeN => edgeN,
            CoastCellType.EdgeE => edgeE,
            CoastCellType.EdgeW => edgeW,
            CoastCellType.CornerNW => cornerNW,
            CoastCellType.CornerNE => cornerNE,
            CoastCellType.CornerSW => cornerSW,
            CoastCellType.CornerSE => cornerSE,
            CoastCellType.InnerNW => innerNW,
            CoastCellType.InnerNE => innerNE,
            CoastCellType.InnerSW => innerSW,
            CoastCellType.InnerSE => innerSE,
            _ => null
        };
    }
}
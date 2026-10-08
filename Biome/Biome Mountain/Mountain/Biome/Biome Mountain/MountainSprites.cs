using UnityEngine;

/// <summary>
/// MountainSprites � Conteneur centralis� de tous les sprites montagne.
/// Assign� dans l'Inspector de MountainSpawner.
/// Chaque structure (Base/Mid/Top) y pioche ses sprites.
/// </summary>
[System.Serializable]
public class MountainSprites
{
    [Header("=== Angles 45� (16x80) ===")]
    public Sprite angleSideLeft;    // AngleL = descend
    public Sprite angleSideRight;   // AngleR = monte

    [Header("=== Angles Top (16x16) ===")]
    public Sprite angleTopLeft;
    public Sprite angleTopRight;

    [Header("=== Bords verticaux ===")]
    public Sprite borderSimpleLeft;
    public Sprite borderSimpleRight;
    public Sprite borderDoubleLeft;
    public Sprite borderDoubleRight;

    [Header("=== Faces (16x64) ===")]
    public Sprite faceLeft;
    public Sprite faceRight;
    public Sprite faceLeftCave;
    public Sprite faceRightCave;

    [Header("=== Virages ===")]
    public Sprite turnShortLeft;
    public Sprite turnShortRight;
    public Sprite turnLargeLeft;
    public Sprite turnLargeRight;
    public Sprite turnExtraLargeLeft;
    public Sprite turnExtraLargeRight;

    [Header("=== Bords Top horizontaux ===")]
    public Sprite topEdgeLeft;
    public Sprite topEdgeMiddle;
    public Sprite topEdgeRight;

    /// <summary>
    /// Retourne le bon sprite selon le type et le c�t� (gauche/droite).
    /// </summary>
    /// <summary>
    /// Retourne le sprite pour ce type � le c�t� G/D est encod� dans PieceType.
    /// </summary>
    public Sprite Get(PieceType type)
    {
        switch (type)
        {
            case PieceType.AngleR: return angleSideRight;
            case PieceType.AngleL: return angleSideLeft;
            case PieceType.AngleTopD: return angleTopRight;
            case PieceType.AngleTopG: return angleTopLeft;
            case PieceType.BorderSimpleL: return borderSimpleLeft;
            case PieceType.BorderSimpleR: return borderSimpleRight;
            case PieceType.BorderDoubleL: return borderDoubleLeft;
            case PieceType.BorderDoubleR: return borderDoubleRight;
            case PieceType.FaceLeft: return faceLeft;
            case PieceType.FaceRight: return faceRight;
            case PieceType.FaceLeftCave: return faceLeftCave;
            case PieceType.FaceRightCave: return faceRightCave;
            case PieceType.TurnShortL: return turnShortLeft;
            case PieceType.TurnShortR: return turnShortRight;
            case PieceType.TurnLargeL: return turnLargeLeft;
            case PieceType.TurnLargeR: return turnLargeRight;
            case PieceType.TurnExtraLargeL: return turnExtraLargeLeft;
            case PieceType.TurnExtraLargeR: return turnExtraLargeRight;
            case PieceType.TopEdge: return topEdgeMiddle;
            default: return null;
        }
    }
}
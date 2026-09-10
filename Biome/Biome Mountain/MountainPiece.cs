using UnityEngine;

/// <summary>
/// MountainPiece — Types et ancres.
/// 
/// CONVENTION UNIQUE : coin haut gauche du sprite
/// PlaceSprite(gx, gy) place le sprite VERS LE BAS depuis (gx, gy)
/// GetAnchorOut retourne directement le coin haut gauche du sprite suivant
/// Les offsets correspondent exactement à la table validée.
///
/// TABLE COMPLÈTE :
///
/// AngleR (16x80, 5 cases) :
///   APRÈS  : AngleR→(x+1,y+1) BordSD→(x+1,y+1) BordDD→(x+1,y+1) Face→(x+1,y+1) VirageD→(x+1,y+1)
///   AVANT  : AngleR→(x+1,y+1) BordSD→(x0,y+1)  BordDD→(x0,y+2)  Face→(x+1,y0)
///
/// AngleL (16x80, 5 cases) :
///   APRÈS  : AngleL→(x-1,y+1) BordSG→(x-1,y+1) BordDG→(x-1,y+1) Face→(x-1,y+1) VirageG→(x-1,y+1)
///   AVANT  : AngleL→(x-1,y+1) BordSG→(x0,y+1)  BordDG→(x0,y+2)  Face→(x-1,y0)
///
/// AngleTopG (16x16) :
///   AVANT  : BordSG→(x+1,y+1) BordDG→(x+1,y+2) BordTop→(x+1,y0) AngleTopG→(x+1,y+1)
///
/// AngleTopD (16x16) :
///   AVANT  : BordSD→(x-1,y+1) BordDD→(x-1,y+2) BordTop→(x-1,y0) AngleTopD→(x-1,y+1)
///
/// BordSimpleG (16x16) :
///   AVANT  : AngleL→(x-1,y+1) BordSG→(x0,y+1) BordDG→(x0,y+2) VirC→(x0,y+3) VirL→(x0,y+4) TopG→(x0,y+1)
///
/// BordSimpleD (16x16) :
///   AVANT  : AngleR→(x+1,y+1) BordSD→(x0,y+1) BordDD→(x0,y+2) VirC→(x0,y+3) VirL→(x0,y+4) TopD→(x0,y+1)
///
/// BordDoubleG (16x32) :
///   AVANT  : AngleL→(x-1,y+1) BordSG→(x0,y+1) BordDG→(x0,y+2) VirC→(x0,y+3) VirL→(x0,y+4) TopG→(x0,y+1)
///
/// BordDoubleD (16x32) :
///   AVANT  : AngleR→(x+1,y+1) BordSD→(x0,y+1) BordDD→(x0,y+2) VirC→(x0,y+3) VirL→(x0,y+4) TopD→(x0,y+1)
///
/// FaceG/FaceGCave (16x64) :
///   À GAUCHE : AngleL→(x-1,y+1)  FaceD→(x-1,y0)
///   À DROITE : FaceD→(x+1,y0)    AngleR→(x-1,y0)
///
/// FaceD/FaceDCave (16x64) :
///   À DROITE : AngleR→(x+1,y+1)  FaceG→(x+1,y0)
///   À GAUCHE : FaceG→(x-1,y0)    AngleL→(x+1,y0)
///
/// VirageCourtG (16x48) : AVANT AngleL→(x-1,y+1) BordSG→(x0,y+1) BordDG→(x0,y+2)
/// VirageCourtD (16x48) : AVANT AngleR→(x+1,y+1) BordSD→(x0,y+1) BordDD→(x0,y+2)
/// VirageLargeG (16x64) : AVANT AngleL→(x-1,y+1) BordSG→(x0,y+1) BordDG→(x0,y+2)
/// VirageLargeD (16x64) : AVANT AngleR→(x+1,y+1) BordSD→(x0,y+1) BordDD→(x0,y+2)
///
/// BordTop (16x16) :
///   BordTop→(x+1,y0) AngleTopD après→(x-1,y0) AngleTopG avant→(x+1,y+1)
/// </summary>

public struct MountainAnchor
{
    public int X;
    public int Y;
    public MountainAnchor(int x, int y) { X = x; Y = y; }
}

public enum PieceType
{
    AngleR,
    AngleL,
    AngleTopG,
    AngleTopD,
    BorderSimpleL,
    BorderSimpleR,
    BorderDoubleL,
    BorderDoubleR,
    FaceLeft,
    FaceRight,
    FaceLeftCave,
    FaceRightCave,
    TurnShortL,
    TurnShortR,
    TurnLargeL,
    TurnLargeR,
    TopEdge,
    TurnExtraLargeL,
    TurnExtraLargeR,
}

public static class MountainPieceData
{
    public static int GetHeight(PieceType type)
    {
        switch (type)
        {
            case PieceType.AngleR:
            case PieceType.AngleL: return 5;
            case PieceType.BorderDoubleL:
            case PieceType.BorderDoubleR: return 2;
            case PieceType.FaceLeft:
            case PieceType.FaceRight:
            case PieceType.FaceLeftCave:
            case PieceType.FaceRightCave: return 4;
            case PieceType.TurnShortL:
            case PieceType.TurnShortR: return 3;
            case PieceType.TurnLargeL:
            case PieceType.TurnLargeR: return 4;
            case PieceType.TurnExtraLargeL:
            case PieceType.TurnExtraLargeR: return 5;
            default: return 1;
        }
    }

    /// <summary>
    /// Retourne le coin haut gauche du sprite SUIVANT
    /// depuis le coin haut gauche du sprite COURANT.
    /// Offsets directs depuis la table validée — aucun calcul de h.
    /// </summary>
    public static MountainAnchor GetAnchorOut(
        PieceType current, MountainAnchor a, PieceType next)
    {
        int hN = GetHeight(next);
        int x = a.X;
        // coin haut du sprite courant = référence pour la table
        int y = a.Y + GetHeight(current) - 1;
        // Les offsets de la table donnent le coin HAUT du sprite suivant
        // On convertit ensuite en coin BAS via MountainAnchor.BottomY(ry, hN)
        // → chaque return devient (rx, ry - hN + 1)

        switch (current)
        {
            case PieceType.AngleR:
                switch (next)
                {
                    case PieceType.AngleR: return new MountainAnchor(x + 1, y + 1 - hN + 1);
                    case PieceType.BorderSimpleR:
                    case PieceType.BorderDoubleR: return new MountainAnchor(x, y + 1 - hN + 1);
                    case PieceType.FaceLeft:
                    case PieceType.FaceRight:
                    case PieceType.FaceLeftCave:
                    case PieceType.FaceRightCave: return new MountainAnchor(x + 1, y - hN + 1);
                    case PieceType.TurnShortR: case PieceType.TurnLargeR: return new MountainAnchor(x + 1, y + 1 - hN + 1);
                    default: return new MountainAnchor(x + 1, y + 1 - hN + 1);
                }

            case PieceType.AngleL:
                switch (next)
                {
                    case PieceType.AngleL: return new MountainAnchor(x - 1, y + 1 - hN + 1);
                    case PieceType.BorderSimpleL:
                    case PieceType.BorderDoubleL: return new MountainAnchor(x, y + 1 - hN + 1);
                    case PieceType.FaceLeft:
                    case PieceType.FaceRight:
                    case PieceType.FaceLeftCave:
                    case PieceType.FaceRightCave: return new MountainAnchor(x - 1, y - hN + 1);
                    case PieceType.TurnShortL: case PieceType.TurnLargeL: return new MountainAnchor(x - 1, y + 1 - hN + 1);
                    default: return new MountainAnchor(x - 1, y + 1 - hN + 1);
                }

            case PieceType.AngleTopG:
                switch (next)
                {
                    case PieceType.BorderSimpleL: return new MountainAnchor(x + 1, y + 1 - hN + 1);
                    case PieceType.BorderDoubleL: return new MountainAnchor(x + 1, y + 2 - hN + 1);
                    case PieceType.TopEdge: return new MountainAnchor(x + 1, y - hN + 1);
                    case PieceType.AngleTopG: return new MountainAnchor(x + 1, y + 1 - hN + 1);
                    default: return new MountainAnchor(x + 1, y + 1 - hN + 1);
                }

            case PieceType.AngleTopD:
                switch (next)
                {
                    case PieceType.BorderSimpleR: return new MountainAnchor(x - 1, y + 1 - hN + 1);
                    case PieceType.BorderDoubleR: return new MountainAnchor(x - 1, y + 2 - hN + 1);
                    case PieceType.TopEdge: return new MountainAnchor(x - 1, y - hN + 1);
                    case PieceType.AngleTopD: return new MountainAnchor(x - 1, y + 1 - hN + 1);
                    default: return new MountainAnchor(x - 1, y + 1 - hN + 1);
                }

            case PieceType.BorderSimpleL:
                switch (next)
                {
                    case PieceType.AngleL: return new MountainAnchor(x - 1, y + 1 - hN + 1);
                    case PieceType.BorderSimpleL: return new MountainAnchor(x, y + 1 - hN + 1);
                    case PieceType.BorderDoubleL: return new MountainAnchor(x, y + 2 - hN + 1);
                    case PieceType.TurnShortL: return new MountainAnchor(x, y + 3 - hN + 1);
                    case PieceType.TurnLargeL: return new MountainAnchor(x, y + 4 - hN + 1);
                    case PieceType.AngleTopG: return new MountainAnchor(x, y + 1 - hN + 1);
                    default: return new MountainAnchor(x, y + 1 - hN + 1);
                }

            case PieceType.BorderSimpleR:
                switch (next)
                {
                    case PieceType.AngleR: return new MountainAnchor(x + 1, y + 1 - hN + 1);
                    case PieceType.BorderSimpleR: return new MountainAnchor(x, y + 1 - hN + 1);
                    case PieceType.BorderDoubleR: return new MountainAnchor(x, y + 2 - hN + 1);
                    case PieceType.TurnShortR: return new MountainAnchor(x, y + 3 - hN + 1);
                    case PieceType.TurnLargeR: return new MountainAnchor(x, y + 4 - hN + 1);
                    case PieceType.AngleTopD: return new MountainAnchor(x, y + 1 - hN + 1);
                    default: return new MountainAnchor(x, y + 1 - hN + 1);
                }

            case PieceType.BorderDoubleL:
                switch (next)
                {
                    case PieceType.AngleL: return new MountainAnchor(x - 1, y + 1 - hN + 1);
                    case PieceType.BorderSimpleL: return new MountainAnchor(x, y + 1 - hN + 1);
                    case PieceType.BorderDoubleL: return new MountainAnchor(x, y + 2 - hN + 1);
                    case PieceType.TurnShortL: return new MountainAnchor(x, y + 3 - hN + 1);
                    case PieceType.TurnLargeL: return new MountainAnchor(x, y + 4 - hN + 1);
                    case PieceType.AngleTopG: return new MountainAnchor(x, y + 1 - hN + 1);
                    default: return new MountainAnchor(x, y + 2 - hN + 1);
                }

            case PieceType.BorderDoubleR:
                switch (next)
                {
                    case PieceType.AngleR: return new MountainAnchor(x + 1, y + 1 - hN + 1);
                    case PieceType.BorderSimpleR: return new MountainAnchor(x, y + 1 - hN + 1);
                    case PieceType.BorderDoubleR: return new MountainAnchor(x, y + 2 - hN + 1);
                    case PieceType.TurnShortR: return new MountainAnchor(x, y + 3 - hN + 1);
                    case PieceType.TurnLargeR: return new MountainAnchor(x, y + 4 - hN + 1);
                    case PieceType.AngleTopD: return new MountainAnchor(x, y + 1 - hN + 1);
                    default: return new MountainAnchor(x, y + 2 - hN + 1);
                }

            case PieceType.FaceLeft:
            case PieceType.FaceLeftCave:
                switch (next)
                {
                    case PieceType.FaceRight: case PieceType.FaceRightCave: return new MountainAnchor(x + 1, y - hN + 1);
                    case PieceType.AngleR: return new MountainAnchor(x - 1, y - hN + 1);
                    case PieceType.AngleL: return new MountainAnchor(x - 1, y + 1 - hN + 1);
                    default: return new MountainAnchor(x + 1, y - hN + 1);
                }

            case PieceType.FaceRight:
            case PieceType.FaceRightCave:
                switch (next)
                {
                    case PieceType.FaceLeft: case PieceType.FaceLeftCave: return new MountainAnchor(x - 1, y - hN + 1);
                    case PieceType.AngleL: return new MountainAnchor(x + 1, y - hN + 1);
                    case PieceType.AngleR: return new MountainAnchor(x + 1, y + 1 - hN + 1);
                    default: return new MountainAnchor(x + 1, y - hN + 1);
                }

            case PieceType.TurnShortL:
                switch (next)
                {
                    case PieceType.BorderSimpleL: return new MountainAnchor(x, y + 3 - hN + 1);
                    case PieceType.BorderDoubleL: return new MountainAnchor(x, y + 3 - hN + 1);
                    case PieceType.TopEdge: return new MountainAnchor(x + 1, y + 3 - hN + 1);
                    default: return new MountainAnchor(x, y + 3 - hN + 1);
                }

            case PieceType.TurnShortR:
                switch (next)
                {
                    case PieceType.BorderSimpleR: return new MountainAnchor(x, y + 3 - hN + 1);
                    case PieceType.BorderDoubleR: return new MountainAnchor(x, y + 3 - hN + 1);
                    case PieceType.TopEdge: return new MountainAnchor(x - 1, y + 3 - hN + 1);
                    default: return new MountainAnchor(x, y + 3 - hN + 1);
                }

            case PieceType.TurnLargeL:
                switch (next)
                {
                    case PieceType.BorderSimpleL: return new MountainAnchor(x, y + 4 - hN + 1);
                    case PieceType.BorderDoubleL: return new MountainAnchor(x, y + 4 - hN + 1);
                    case PieceType.TopEdge: return new MountainAnchor(x + 1, y + 4 - hN + 1);
                    default: return new MountainAnchor(x, y + 4 - hN + 1);
                }

            case PieceType.TurnLargeR:
                switch (next)
                {
                    case PieceType.BorderSimpleR: return new MountainAnchor(x, y + 4 - hN + 1);
                    case PieceType.BorderDoubleR: return new MountainAnchor(x, y + 4 - hN + 1);
                    case PieceType.TopEdge: return new MountainAnchor(x - 1, y + 4 - hN + 1);
                    default: return new MountainAnchor(x, y + 4 - hN + 1);
                }

            case PieceType.TopEdge:
                switch (next)
                {
                    case PieceType.TopEdge: return new MountainAnchor(x + 1, y - hN + 1);
                    case PieceType.AngleTopD: return new MountainAnchor(x - 1, y - hN + 1);
                    case PieceType.AngleTopG: return new MountainAnchor(x + 1, y - hN + 1);
                    default: return new MountainAnchor(x + 1, y - hN + 1);
                }

            default:
                return new MountainAnchor(x, y + 1 - hN + 1);
        }
    }

}
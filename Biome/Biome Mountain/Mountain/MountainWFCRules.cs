using System.Collections.Generic;

/// <summary>
/// MountainWFCRules — Règles de voisinage vertical pour le WFC montagne.
///
/// Pour chaque PieceType, définit quelles pièces peuvent venir AU-DESSUS.
/// Séparé en côté gauche et côté droit.
///
/// Les hauteurs multi-cellules (Face=4, Angle=5, Turn=3-4) sont gérées
/// par MountainPiece.GetHeight + GetAnchorOut — on ne les change pas.
/// </summary>
public static class MountainWFCRules
{
    // ── Côté GAUCHE : ce qui peut suivre au-dessus ──────────────────────────

    public static readonly Dictionary<PieceType, PieceType[]> LeftAbove = new()
    {
        // Angle bas gauche → monte et démarre la colonne gauche
        { PieceType.AngleL, new[] {
            PieceType.BorderSimpleL,
            PieceType.BorderDoubleL,
            PieceType.FaceLeft,
            PieceType.FaceLeftCave,
            PieceType.TurnShortL,
        }},

        // Bord simple gauche → continue ou virage
        { PieceType.BorderSimpleL, new[] {
            PieceType.BorderSimpleL,
            PieceType.BorderDoubleL,
            PieceType.AngleL,           // cassure (décrochement)
            PieceType.TurnShortL,       // virage court
            PieceType.TurnLargeL,       // virage long
            PieceType.AngleTopG,        // fermeture haut
        }},

        // Bord double gauche → continue ou virage
        { PieceType.BorderDoubleL, new[] {
            PieceType.BorderSimpleL,
            PieceType.BorderDoubleL,
            PieceType.AngleL,
            PieceType.TurnShortL,
            PieceType.TurnLargeL,
            PieceType.AngleTopG,
        }},

        // Face gauche → la paire droite ou angle cassure
        { PieceType.FaceLeft, new[] {
            PieceType.FaceRight,        // paire standard
            PieceType.FaceRightCave,    // paire avec cave
            PieceType.AngleR,           // cassure droite
            PieceType.AngleL,           // cassure gauche
        }},

        // Face cave gauche → comme FaceLeft
        { PieceType.FaceLeftCave, new[] {
            PieceType.FaceRight,
            PieceType.FaceRightCave,
            PieceType.AngleR,
            PieceType.AngleL,
        }},

        // Virage court gauche → revient au bord
        { PieceType.TurnShortL, new[] {
            PieceType.BorderSimpleL,
            PieceType.BorderDoubleL,
            PieceType.AngleTopG,
        }},

        // Virage long gauche → revient au bord
        { PieceType.TurnLargeL, new[] {
            PieceType.BorderSimpleL,
            PieceType.BorderDoubleL,
            PieceType.AngleTopG,
        }},

        // Angle top gauche → fermeture avec bord top
        { PieceType.AngleTopG, new[] {
            PieceType.TopEdge,
        }},
    };

    // ── Côté DROIT : ce qui peut suivre au-dessus ───────────────────────────

    public static readonly Dictionary<PieceType, PieceType[]> RightAbove = new()
    {
        { PieceType.AngleR, new[] {
            PieceType.BorderSimpleR,
            PieceType.BorderDoubleR,
            PieceType.FaceRight,
            PieceType.FaceRightCave,
            PieceType.TurnShortR,
        }},

        { PieceType.BorderSimpleR, new[] {
            PieceType.BorderSimpleR,
            PieceType.BorderDoubleR,
            PieceType.AngleR,
            PieceType.TurnShortR,
            PieceType.TurnLargeR,
            PieceType.AngleTopD,
        }},

        { PieceType.BorderDoubleR, new[] {
            PieceType.BorderSimpleR,
            PieceType.BorderDoubleR,
            PieceType.AngleR,
            PieceType.TurnShortR,
            PieceType.TurnLargeR,
            PieceType.AngleTopD,
        }},

        { PieceType.FaceRight, new[] {
            PieceType.FaceLeft,
            PieceType.FaceLeftCave,
            PieceType.AngleL,
            PieceType.AngleR,
        }},

        { PieceType.FaceRightCave, new[] {
            PieceType.FaceLeft,
            PieceType.FaceLeftCave,
            PieceType.AngleL,
            PieceType.AngleR,
        }},

        { PieceType.TurnShortR, new[] {
            PieceType.BorderSimpleR,
            PieceType.BorderDoubleR,
            PieceType.AngleTopD,
        }},

        { PieceType.TurnLargeR, new[] {
            PieceType.BorderSimpleR,
            PieceType.BorderDoubleR,
            PieceType.AngleTopD,
        }},

        { PieceType.AngleTopD, new[] {
            PieceType.TopEdge,
        }},
    };

    /// <summary>
    /// Vérifie si "above" peut être placé au-dessus de "current" sur le côté donné.
    /// </summary>
    public static bool CanFollow(PieceType current, PieceType above, bool isLeftSide)
    {
        var dict = isLeftSide ? LeftAbove : RightAbove;
        if (!dict.TryGetValue(current, out var allowed)) return false;
        foreach (var a in allowed)
            if (a == above) return true;
        return false;
    }

    /// <summary>
    /// Retourne les pièces autorisées au-dessus de "current".
    /// </summary>
    public static PieceType[] GetAllowed(PieceType current, bool isLeftSide)
    {
        var dict = isLeftSide ? LeftAbove : RightAbove;
        return dict.TryGetValue(current, out var allowed) ? allowed : System.Array.Empty<PieceType>();
    }
}

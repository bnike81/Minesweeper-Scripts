using UnityEngine;

/// <summary>
/// CaveAutoTiler — Sprites de mur par bitmask classique.
/// Aucune dépendance WFC — logique pure bitmask S/E/N/W.
///
/// PORTAILS = edgeS :
///   IsPortalCell() intercepte avant le bitmask → bordBasCave.
///   StampPortalEdgeS dans CaveLayout garantit c==N sur le portail.
/// </summary>
public static class CaveAutoTiler
{
    private const int S = 1, E = 2, N = 4, W = 8;

    // =========================================================================
    // SPRITE
    // =========================================================================

    public static Sprite GetSprite(CaveLayout layout, int x, int y, CaveSpriteSet sprites)
    {
        if (sprites == null) return null;

        // Sol
        if (layout.IsFloor(x, y)) return sprites.floor;

        // Portail — intercepté avant bitmask
        if (IsPortalCell(layout, x, y))
            return sprites.bordBasCave ?? sprites.edgeS ?? sprites.wallFull;

        // Bitmask
        int c = CardinalMask(layout, x, y);
        int d = DiagonalMask(layout, x, y);

        if (c == 0 && d == 0) return sprites.wallFull;

        return Resolve(c, d, sprites) ?? sprites.wallFull;
    }

    // =========================================================================
    // PORTAILS
    // =========================================================================

    public static bool IsPortalCell(CaveLayout layout, int x, int y)
        => IsEntryPortal(layout, x, y) || IsExitPortal(layout, x, y);

    public static bool IsEntryPortal(CaveLayout layout, int x, int y)
        => x == layout.EntryPos.x && y == layout.EntryPos.y;

    public static bool IsExitPortal(CaveLayout layout, int x, int y)
        => x == layout.ExitPos.x && y == layout.ExitPos.y;

    // =========================================================================
    // CONTOUR
    // IsWallContour : true si la case est un mur visible à afficher.
    // false = roche profonde (pas de cell view, couverte par le fog).
    // =========================================================================

    public static bool IsWallContour(CaveLayout layout, int x, int y)
    {
        if (layout.IsFloor(x, y)) return false;
        if (x < 0 || x >= layout.Width || y < 0 || y >= layout.Height) return false;
        if (IsPortalCell(layout, x, y)) return true;
        return CardinalMask(layout, x, y) > 0 || DiagonalMask(layout, x, y) > 0;
    }

    // =========================================================================
    // BITMASKS
    // =========================================================================

    private static int CardinalMask(CaveLayout layout, int x, int y)
    {
        int m = 0;
        if (layout.IsFloor(x, y - 1)) m |= S;
        if (layout.IsFloor(x + 1, y)) m |= E;
        if (layout.IsFloor(x, y + 1)) m |= N;
        if (layout.IsFloor(x - 1, y)) m |= W;
        return m;
    }

    private static int DiagonalMask(CaveLayout layout, int x, int y)
    {
        int m = 0;
        if (layout.IsFloor(x + 1, y - 1)) m |= 1; // SE
        if (layout.IsFloor(x + 1, y + 1)) m |= 2; // NE
        if (layout.IsFloor(x - 1, y + 1)) m |= 4; // NW
        if (layout.IsFloor(x - 1, y - 1)) m |= 8; // SW
        return m;
    }

    // =========================================================================
    // RÉSOLUTION SPRITE
    // =========================================================================

    private static Sprite Resolve(int c, int d, CaveSpriteSet s)
    {
        if (c == S) return s.edgeN ?? s.wallFull;
        if (c == N) return s.edgeS ?? s.wallFull;
        if (c == E) return s.edgeW ?? s.wallFull;
        if (c == W) return s.edgeE ?? s.wallFull;

        if (c == (S | E)) return s.innerNW ?? s.wallFull;
        if (c == (S | W)) return s.innerNE ?? s.wallFull;
        if (c == (N | E)) return s.innerSW ?? s.wallFull;
        if (c == (N | W)) return s.innerSE ?? s.wallFull;

        if (c == (N | S)) return s.corridorH ?? s.wallFull;
        if (c == (E | W)) return s.corridorV ?? s.wallFull;

        if (c == (S | E | N)) return s.deadEndW ?? s.innerNW ?? s.wallFull;
        if (c == (S | W | N)) return s.deadEndE ?? s.innerNE ?? s.wallFull;
        if (c == (E | W | N)) return s.deadEndS ?? s.innerSW ?? s.wallFull;
        if (c == (E | W | S)) return s.deadEndN ?? s.innerSE ?? s.wallFull;
        if (c == (S | E | N | W)) return s.wallFull;

        if (c == 0)
        {
            if ((d & 1) != 0) return s.cornerNW ?? s.wallFull;
            if ((d & 2) != 0) return s.cornerSW ?? s.wallFull;
            if ((d & 4) != 0) return s.cornerSE ?? s.wallFull;
            if ((d & 8) != 0) return s.cornerNE ?? s.wallFull;
        }

        if (c == S && (d & 1) != 0) return s.angle45_NW ?? s.edgeN ?? s.wallFull;
        if (c == S && (d & 8) != 0) return s.angle45_NE ?? s.edgeN ?? s.wallFull;
        if (c == N && (d & 2) != 0) return s.angle45_SW ?? s.edgeS ?? s.wallFull;
        if (c == N && (d & 4) != 0) return s.angle45_SE ?? s.edgeS ?? s.wallFull;
        if (c == E && (d & 1) != 0) return s.angle45_NW ?? s.edgeW ?? s.wallFull;
        if (c == E && (d & 2) != 0) return s.angle45_SW ?? s.edgeW ?? s.wallFull;
        if (c == W && (d & 4) != 0) return s.angle45_SE ?? s.edgeE ?? s.wallFull;
        if (c == W && (d & 8) != 0) return s.angle45_NE ?? s.edgeE ?? s.wallFull;

        return s.wallFull;
    }

    // =========================================================================
    // DEBUG
    // =========================================================================

    public static string GetTileTypeName(CaveLayout layout, int x, int y)
    {
        if (layout.IsFloor(x, y)) return "FLOOR";
        if (IsEntryPortal(layout, x, y)) return "PORTAL_ENTRY";
        if (IsExitPortal(layout, x, y)) return "PORTAL_EXIT";
        int c = CardinalMask(layout, x, y), d = DiagonalMask(layout, x, y);
        if (c == 0 && d == 0) return "ROCK";
        if (c == S) return "EDGE_N"; if (c == N) return "EDGE_S";
        if (c == E) return "EDGE_W"; if (c == W) return "EDGE_E";
        if (c == (S | E)) return "INNER_NW"; if (c == (S | W)) return "INNER_NE";
        if (c == (N | E)) return "INNER_SW"; if (c == (N | W)) return "INNER_SE";
        return $"WALL_C{c}_D{d}";
    }
}
using UnityEngine;

/// <summary>
/// CaveAutoTiler — Place les murs AUTOUR du sol marchable.
///
/// CONVENTION SPRITES (selon l'utilisateur) :
///   "Bord haut" (edgeN) : bas du sprite = sol, haut = roche
///   "Coin extérieur" : 1 seule diagonale adjacente est du sol
///   "Coin intérieur" : 3+ cases adjacentes sont du sol (angle concave)
///
/// BITMASK : indique où est le SOL vu depuis la case MUR
///   S=1 (sol en bas), E=2 (sol à droite), N=4 (sol en haut), W=8 (sol à gauche)
/// </summary>
public static class CaveAutoTiler
{
    private const int S = 1, E = 2, N = 4, W = 8;

    public static Sprite GetSprite(CaveLayout layout, int x, int y, CaveSpriteSet sprites)
    {
        if (sprites == null) return null;

        if (layout.IsFloor(x, y))
        {
            // ── Portail entrée (bord bas de la grotte) ───────────────────────
            // La case portail est du SOL mais au bord → sprite porte
            var entry = layout.EntryPos;
            if (x == entry.x && y == 0 && sprites.portalEntry != null)
                return sprites.portalEntry;

            // ── Portail sortie (bord haut de la grotte) ──────────────────────
            var exit = layout.ExitPos;
            if (x == exit.x && y == layout.Height - 1 && sprites.portalExit != null)
                return sprites.portalExit;

            return sprites.floor;
        }

        int c = GetFloorCardinalMask(layout, x, y);
        int d = GetFloorDiagonalMask(layout, x, y);

        if (c == 0 && d == 0) return sprites.wallFull;

        return ResolveWallSprite(c, d, sprites);
    }

    public static bool IsWallContour(CaveLayout layout, int x, int y)
    {
        if (layout.IsFloor(x, y)) return false;
        if (x < 0 || x >= layout.Width || y < 0 || y >= layout.Height) return false;
        return GetFloorCardinalMask(layout, x, y) > 0
            || GetFloorDiagonalMask(layout, x, y) > 0;
    }

    // =========================================================================
    // BITMASKS
    // =========================================================================

    private static int GetFloorCardinalMask(CaveLayout layout, int x, int y)
    {
        int mask = 0;
        if (layout.IsFloor(x, y - 1)) mask |= S;
        if (layout.IsFloor(x + 1, y)) mask |= E;
        if (layout.IsFloor(x, y + 1)) mask |= N;
        if (layout.IsFloor(x - 1, y)) mask |= W;
        return mask;
    }

    private static int GetFloorDiagonalMask(CaveLayout layout, int x, int y)
    {
        int mask = 0;
        if (layout.IsFloor(x + 1, y - 1)) mask |= 1;  // SE
        if (layout.IsFloor(x + 1, y + 1)) mask |= 2;  // NE
        if (layout.IsFloor(x - 1, y + 1)) mask |= 4;  // NW
        if (layout.IsFloor(x - 1, y - 1)) mask |= 8;  // SW
        return mask;
    }

    // =========================================================================
    // RÉSOLUTION
    //
    // SOL au sud → ce mur est le BORD HAUT (edgeN) du sol
    //   Le sprite "bord haut" a son bas qui connecte au sol
    //
    // 2 cardinaux adjacents (ex: sol S+E) → COIN INTÉRIEUR
    //   Car ce mur est à l'intérieur d'un virage de sol (3+ voisins sol)
    //
    // 0 cardinal, 1 diagonal (ex: sol diag SE) → COIN EXTÉRIEUR
    //   Car ce mur est à l'extérieur (1 seul voisin sol en diagonale)
    // =========================================================================

    private static Sprite ResolveWallSprite(int c, int d, CaveSpriteSet s)
    {
        // ── 1 côté sol → BORD ──────────────────────────────────────────────
        // "bord haut" connecte avec sol en bas (x0,y-1) → c==S → edgeN
        // "bord bas"  connecte avec sol en haut (x0,y+1) → c==N → edgeS
        // "bord gauche" connecte avec sol à droite (x+1,y0) → c==E → edgeW
        // "bord droite" connecte avec sol à gauche (x-1,y0) → c==W → edgeE
        if (c == S) return s.edgeN ?? s.wallFull;
        if (c == N) return s.edgeS ?? s.wallFull;
        if (c == E) return s.edgeW ?? s.wallFull;
        if (c == W) return s.edgeE ?? s.wallFull;

        // ── 2 côtés sol adjacents → COIN INTÉRIEUR (concave, 3+ voisins sol) ─
        if (c == (S | E)) return s.innerNW ?? s.wallFull;
        if (c == (S | W)) return s.innerNE ?? s.wallFull;
        if (c == (N | E)) return s.innerSW ?? s.wallFull;
        if (c == (N | W)) return s.innerSE ?? s.wallFull;

        // ── 2 côtés sol opposés → couloir ────────────────────────────────────
        if (c == (N | S)) return s.corridorH ?? s.wallFull;
        if (c == (E | W)) return s.corridorV ?? s.wallFull;

        // ── 3+ côtés sol → presqu'île / isolé ───────────────────────────────
        if (c == (S | E | N)) return s.deadEndW ?? s.wallFull;
        if (c == (S | W | N)) return s.deadEndE ?? s.wallFull;
        if (c == (E | W | N)) return s.deadEndS ?? s.wallFull;
        if (c == (E | W | S)) return s.deadEndN ?? s.wallFull;
        if (c == (S | E | N | W)) return s.wallFull;

        // ── 0 cardinal, diagonal seul → COIN EXTÉRIEUR (convexe, 1 voisin) ──
        if (c == 0)
        {
            if ((d & 1) != 0) return s.cornerNW ?? s.wallFull;  // sol diag SE → coin ext haut-gauche
            if ((d & 2) != 0) return s.cornerSW ?? s.wallFull;  // sol diag NE → coin ext bas-gauche
            if ((d & 4) != 0) return s.cornerSE ?? s.wallFull;  // sol diag NW → coin ext bas-droite
            if ((d & 8) != 0) return s.cornerNE ?? s.wallFull;  // sol diag SW → coin ext haut-droite
        }

        // ── Angle 45° (1 cardinal + 1 diagonal) ─────────────────────────────
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
        int c = GetFloorCardinalMask(layout, x, y);
        int d = GetFloorDiagonalMask(layout, x, y);
        if (c == 0 && d == 0) return "ROCK";
        if (c == S) return "EDGE_N";
        if (c == N) return "EDGE_S";
        if (c == E) return "EDGE_W";
        if (c == W) return "EDGE_E";
        if (c == (S | E)) return "INNER_NW";
        if (c == (S | W)) return "INNER_NE";
        if (c == (N | E)) return "INNER_SW";
        if (c == (N | W)) return "INNER_SE";
        if (c == 0 && d > 0) return "CORNER_EXT";
        return $"WALL_C{c}_D{d}";
    }
}
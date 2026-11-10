using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// CaveLayout — Génération structurelle cave, fidèle au chapitre 2.
///
/// MODIFICATION ADDITIVE : Height = assez grand pour les Y surface.
/// EntryLocalY = entryPortalY + yPad (décalé vers le haut pour laisser de
/// l'espace sous les portails). yPad = 0 quand le portail est déjà assez haut.
/// Chaque cave occupe sa propre zone Y — pas de chevauchement.
/// </summary>
public enum CaveCellType : byte
{
    None,           // roche / hors-limite
    LargeRoom,      // grande salle (village goblin)
    SmallRoom,      // petite salle
    Corridor,       // couloir de connexion
    DeadEnd,        // impasse
    PortalEdge,     // bord portail protégé (hors-limite forcé)
}

public class CaveLayout
{
    public int Width { get; }
    public int Height { get; }

    private readonly bool[,] _floor;
    private readonly CaveCellType[,] _cellType;  // debug — nature de chaque case

    public readonly List<RectInt> Rooms = new();
    public RectInt LargeRoom { get; private set; }

    /// <summary>Type debug de chaque case (pour Gizmos couleur).</summary>
    public CaveCellType GetCellType(int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Height ? _cellType[x, y] : CaveCellType.None;

    public Vector2Int EntryPos { get; private set; }
    public Vector2Int ExitPos { get; private set; }
    public int EntryLocalY { get; private set; }
    public int ExitLocalY { get; private set; }

    private System.Random _rng;
    private int _epx, _xpx;
    private int _pw;
    private bool _isTwoWay;        // mono-portail : ExitPos = EntryPos
    private int _caveTopY;         // borne haute de la zone de génération (salles/couloirs)
    private int _caveBotY;         // borne basse de la zone de génération (sous les portails)
    public int CaveBotY => _caveBotY;

    public bool IsFloor(int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Height && _floor[x, y];
    public bool[,] GetFloorMap() => _floor;
    public bool IsPortalEntry(int x, int y) => x == EntryPos.x && y == EntryPos.y;
    public bool IsPortalExit(int x, int y) => x == ExitPos.x && y == ExitPos.y;

    public CaveLayout(CaveConfig config, Vector2Int entryPortal, Vector2Int exitPortal)
        : this(config, entryPortal.x, 0, exitPortal.x, config.height - 1, false) { }

    public CaveLayout(CaveConfig config,
                      int entryPortalX, int entryPortalY,
                      int exitPortalX, int exitPortalY,
                      bool isTwoWay = false)
    {
        Width = GridManager.Instance?.Width ?? config.width;

        // ── RNG initialisé tôt car le roll détermine la taille du tableau ────
        _rng = config.seed >= 0 ? new System.Random(config.seed) : new System.Random();
        _pw = Mathf.Clamp(config.portalWidth, 1, 3);

        // ── Distribution verticale — CALCULÉE EN PREMIER ─────────────────────
        // Détermine combien d'espace est nécessaire SOUS les portails.
        // 25% : équilibré (≈45% du height sous portails, ≈55% au-dessus)
        // 50% : majoritairement au-dessus (≈20% sous, ≈80% au-dessus)
        // 25% : presque tout au-dessus (juste ~8 cells sous — 1 salle + couloir)
        int roll = _rng.Next(100);
        int belowExtent, aboveExtent;
        if (roll < 25)
        {
            belowExtent = Mathf.RoundToInt(config.height * 0.45f);
            aboveExtent = config.height - belowExtent;
        }
        else if (roll < 75)
        {
            belowExtent = Mathf.RoundToInt(config.height * 0.20f);
            aboveExtent = config.height - belowExtent;
        }
        else
        {
            belowExtent = 8;
            aboveExtent = config.height - belowExtent;
        }

        // ── Padding vertical — garantir l'espace sous les portails ───────────
        // Si le portail bas est trop près du Y=0 pour belowExtent, on décale
        // les portails vers le haut dans le tableau pour créer l'espace.
        const int marge = 4;
        int rawBaseY = Mathf.Min(entryPortalY, exitPortalY);
        int neededBelow = belowExtent + marge;      // espace total requis sous le portail bas
        int yPad = Mathf.Max(0, neededBelow - rawBaseY);  // décalage nécessaire

        // Décaler les Y des portails dans le tableau
        entryPortalY += yPad;
        exitPortalY += yPad;

        // ── Height = assez grand pour le contenu au-dessus ET en dessous ─────
        int maxY = Mathf.Max(entryPortalY, exitPortalY);
        Height = Mathf.Max(maxY + config.height + 10,
                           (GridManager.Instance?.Height ?? config.height) + yPad);

        _floor = new bool[Width, Height];
        _cellType = new CaveCellType[Width, Height];

        _epx = Mathf.Clamp(entryPortalX, 2, Width - _pw - 2);
        _xpx = Mathf.Clamp(exitPortalX, 2, Width - _pw - 2);

        // ── Y locaux — portails décalés par yPad ─────────────────────────────
        _isTwoWay = isTwoWay;
        int deltaY = isTwoWay ? 8 : Mathf.Abs(exitPortalY - entryPortalY);
        int baseY = Mathf.Min(entryPortalY, exitPortalY);

        if (isTwoWay)
        {
            EntryLocalY = Mathf.Clamp(baseY, marge, Height - marge - 1);
            ExitLocalY = EntryLocalY;
            _xpx = _epx;
        }
        else
        {
            EntryLocalY = Mathf.Clamp(baseY, marge, Height - deltaY - marge - 1);
            ExitLocalY = Mathf.Clamp(baseY + deltaY, EntryLocalY + 8, Height - marge - 1);
        }

        // ── Bornes de génération _caveBotY / _caveTopY ───────────────────────
        // Grâce au yPad, EntryLocalY est assez haut pour que belowExtent tienne.
        int lowestPortalY = EntryLocalY;
        _caveBotY = Mathf.Max(marge, lowestPortalY - belowExtent);

        if (isTwoWay)
        {
            _caveTopY = Mathf.Clamp(EntryLocalY + aboveExtent,
                                    EntryLocalY + 8, Height - marge - 1);
        }
        else
        {
            _caveTopY = Mathf.Clamp(EntryLocalY + aboveExtent,
                                    ExitLocalY + 4, Height - marge - 1);
        }

        // ── Portails ─────────────────────────────────────────────────────────

        EntryPos = new Vector2Int(_epx, EntryLocalY);
        ExitPos = isTwoWay ? EntryPos : new Vector2Int(_xpx, ExitLocalY);

        // Sol minimal au-dessus du portail — juste la case de connexion (py+1)
        // Le reste sera connecté par les couloirs normaux de ConnectAll
        SetFloor(_epx, EntryLocalY + 1);
        if (!isTwoWay)
            SetFloor(_xpx, ExitLocalY + 1);

        // ── Scaling proportionnel à la hauteur effective ──────────────────
        // Référence : height 20 → 1 grande salle, 4 petites, 2 impasses (≈5 salles)
        //             height 60 → 3 grandes salles, 12 petites, 6 impasses (≈15 salles)
        int effectiveHeight = Mathf.Max(8, _caveTopY - _caveBotY);
        float scale = effectiveHeight / 20f;

        int largeRoomCount = config.alwaysLargeRoom
            ? Mathf.Max(1, Mathf.RoundToInt(scale))     // 20→1, 40→2, 60→3
            : 0;
        int smallRoomCount = Mathf.Max(1, Mathf.RoundToInt(3f * scale));  // 20→3, 40→6, 60→9 (moins compact)
        int deadEndCount = Mathf.Max(1, Mathf.RoundToInt(2.5f * scale)); // 20→2, 40→5, 60→7 (plus d'impasses)

        // ── Placement organique ─────────────────────────────────────────
        // 1) Grandes salles d'abord (positions aléatoires) — elles ont besoin
        //    de plus d'espace, les placer en premier garantit qu'elles trouvent
        //    de la place avant que les petites fragmentent la grille.
        int largeAttempts = Mathf.Max(120, largeRoomCount * 40);
        for (int lr = 0; lr < largeRoomCount; lr++)
        {
            var rect = TryPlaceLargeRoom(config, largeAttempts);
            if (!rect.HasValue) continue;
            if (lr == 0) LargeRoom = rect.Value;
            CarveRoom(rect.Value, isLarge: true);
        }

        // 2) Petites salles — positions aléatoires, remplissent les espaces
        int maxAttempts = Mathf.Max(80, smallRoomCount * 12);
        for (int i = 0; i < smallRoomCount; i++)
        {
            var room = TryPlaceSmallRoom(config, maxAttempts);
            if (room.HasValue) CarveRoom(room.Value);
        }

        ConnectAll(config);

        // ── Impasses — quantité proportionnelle ──────────────────────────
        var deadEndPositions = new List<Vector2Int>();
        for (int i = 0; i < deadEndCount; i++)
            CarveDeadEnd(config, deadEndPositions);

        EnsureConnectivity();
        EnforceBorders();
        EnforceWallThickness();

        StampPortalEdgeS(_epx, EntryLocalY);
        if (!isTwoWay)
            StampPortalEdgeS(_xpx, ExitLocalY);

        string mode = isTwoWay ? "MONO-PORTAIL" : "DEUX-PORTAILS";
        string distrib = roll < 25 ? "ÉQUILIBRÉ" : roll < 75 ? "MAJORITAIRE-HAUT" : "PRESQUE-TOUT-HAUT";
        int belowCells = EntryLocalY - _caveBotY;
        int aboveCells = _caveTopY - EntryLocalY;
        Debug.Log($"[CaveLayout] {Width}×{Height} effH={effectiveHeight} scale={scale:F1} " +
                  $"{Rooms.Count} salles ({largeRoomCount}L+{smallRoomCount}S) " +
                  $"{deadEndCount} impasses {mode} {distrib} yPad={yPad} | " +
                  $"Entry=({EntryPos.x},{EntryPos.y}) Exit=({ExitPos.x},{ExitPos.y}) " +
                  $"caveBotY={_caveBotY} caveTopY={_caveTopY} below={belowCells} above={aboveCells}");
    }

    // =========================================================================
    // CARVE PORTAL JUNCTION — identique doc 17
    // =========================================================================

    // (Anciennes CarvePortalJunction / CarvePortalJunctionBelow supprimées)
    // Les portails sont désormais connectés par les couloirs normaux de ConnectAll.

    // =========================================================================
    // STAMP PORTAL edgeS — identique doc 17
    // =========================================================================

    private void StampPortalEdgeS(int px, int py)
    {
        if (!InBounds(px, py)) return;

        // Portail lui-même : px-1..px+1 à py = hors-limite
        _floor[px, py] = false; _cellType[px, py] = CaveCellType.PortalEdge;
        if (InBounds(px - 1, py)) { _floor[px - 1, py] = false; _cellType[px - 1, py] = CaveCellType.PortalEdge; }
        if (InBounds(px + 1, py)) { _floor[px + 1, py] = false; _cellType[px + 1, py] = CaveCellType.PortalEdge; }

        // Sous le portail : px-1..px+1 à py-1 = hors-limite (bord sud complet)
        for (int dx = -1; dx <= 1; dx++)
            if (InBounds(px + dx, py - 1))
            { _floor[px + dx, py - 1] = false; _cellType[px + dx, py - 1] = CaveCellType.PortalEdge; }

        // Colonne centrale py-2, py-3 = hors-limite (connexion visuelle vers le bas)
        for (int dy = -2; dy >= -3; dy--)
            if (InBounds(px, py + dy))
            { _floor[px, py + dy] = false; _cellType[px, py + dy] = CaveCellType.PortalEdge; }

        // Au-dessus du portail : sol pour connexion à la cave
        if (InBounds(px, py + 1) && py + 1 < Height - 1)
            _floor[px, py + 1] = true;
    }

    // =========================================================================
    // CONNEXION — identique doc 17
    // =========================================================================

    private void ConnectAll(CaveConfig config)
    {
        var pts = new List<Vector2Int>();

        // Ancrage portail entrée — juste au-dessus (py+1), la case sol posée
        pts.Add(new Vector2Int(_epx, EntryLocalY + 1));

        // Points de connexion sous les portails si génération étendue en bas
        if (_caveBotY < EntryLocalY - 6)
        {
            // Ancrage latéral sous le portail — contourne la zone protégée (px±3)
            int belowLateralX = _epx < Width / 2 ? _epx + 3 : _epx - 3;
            belowLateralX = Mathf.Clamp(belowLateralX, 2, Width - 3);
            pts.Add(new Vector2Int(belowLateralX, EntryLocalY - 3));

            // Ancrage au fond de la zone basse
            int belowX = _rng.Next(2, Width - 3);
            pts.Add(new Vector2Int(belowX, _caveBotY + 2));
        }

        foreach (var r in Rooms)
            pts.Add(new Vector2Int(r.x + r.width / 2, r.y + r.height / 2));

        if (!_isTwoWay)
        {
            // Ancrage portail sortie — juste au-dessus (py+1)
            pts.Add(new Vector2Int(_xpx, ExitLocalY + 1));
        }
        else if (Rooms.Count == 0)
        {
            int topTargetX = _rng.Next(2, Width - 3);
            pts.Add(new Vector2Int(topTargetX, _caveTopY));
        }

        pts.Sort((a, b) => a.y.CompareTo(b.y));

        for (int i = 0; i < pts.Count - 1; i++)
        {
            int w = _rng.Next(config.corridorMinWidth, config.corridorMaxWidth + 1);
            CarveCorridor(pts[i], pts[i + 1], w);
        }
    }

    // =========================================================================
    // COULOIRS ORGANIQUES — identique doc 17
    // =========================================================================

    private void CarveCorridor(Vector2Int from, Vector2Int to, int thick)
    {
        int cx = from.x, cy = from.y;
        int steps = 0, maxSteps = (Width + Height) * 3;

        // Bornes asymétriques pour largeurs paires : thick=2→(0,1), thick=3→(-1,1)
        int w = Mathf.Clamp(thick, 2, 4);
        int halfLo = (w - 1) / 2;   // 2→0, 3→1, 4→1
        int halfHi = w / 2;          // 2→1, 3→1, 4→2

        while ((cx != to.x || cy != to.y) && steps++ < maxSteps)
        {
            for (int dx = -halfLo; dx <= halfHi; dx++)
                for (int dy = -halfLo; dy <= halfHi; dy++)
                {
                    SetFloor(cx + dx, cy + dy);
                    if (InBounds(cx + dx, cy + dy) && _cellType[cx + dx, cy + dy] == CaveCellType.None)
                        _cellType[cx + dx, cy + dy] = CaveCellType.Corridor;
                }

            int dX = to.x - cx;
            int dY = to.y - cy;

            if (dX != 0 && dY != 0)
            {
                if (_rng.Next(100) < 55)
                { cx += dX > 0 ? 1 : -1; cy += dY > 0 ? 1 : -1; }
                else if (Mathf.Abs(dY) >= Mathf.Abs(dX))
                { cy += dY > 0 ? 1 : -1; }
                else
                { cx += dX > 0 ? 1 : -1; }
            }
            else if (dY != 0)
            {
                cy += dY > 0 ? 1 : -1;
                if (_rng.Next(100) < 25 && dX == 0)
                    cx += _rng.Next(2) == 0 ? 1 : -1;
            }
            else
            {
                cx += dX > 0 ? 1 : -1;
                if (_rng.Next(100) < 25 && dY == 0)
                    cy += _rng.Next(2) == 0 ? 1 : -1;
            }

            cx = Mathf.Clamp(cx, 1, Width - w - 1);
            cy = Mathf.Clamp(cy, 1, Height - w - 1);
        }

        // Stamp final au point d'arrivée — mêmes bornes
        for (int dx = -halfLo; dx <= halfHi; dx++)
            for (int dy = -halfLo; dy <= halfHi; dy++)
            {
                SetFloor(to.x + dx, to.y + dy);
                if (InBounds(to.x + dx, to.y + dy) && _cellType[to.x + dx, to.y + dy] == CaveCellType.None)
                    _cellType[to.x + dx, to.y + dy] = CaveCellType.Corridor;
            }
    }

    // =========================================================================
    // SALLES — identique doc 17
    // =========================================================================

    /// <summary>
    /// Placement aléatoire d'une grande salle — position libre sur toute la grille.
    /// Plus de centrage horizontal ni d'espacement uniforme vertical.
    /// </summary>
    private RectInt? TryPlaceLargeRoom(CaveConfig config, int maxAttempts)
    {
        int lw = config.largeRoomWidth;
        int lh = config.largeRoomHeight;
        for (int a = 0; a < maxAttempts; a++)
        {
            int x = _rng.Next(1, Mathf.Max(2, Width - lw - 1));
            int minGenY = Mathf.Max(2, _caveBotY + 1);
            int maxGenY = Mathf.Min(Height - lh - 2, _caveTopY - lh);
            if (maxGenY <= minGenY) continue;
            int y = _rng.Next(minGenY, maxGenY);

            var room = new RectInt(x, y, lw, lh);

            // Protection portails — marge 4 pour laisser l'espace aux bords
            if (Overlaps(room, _epx, EntryLocalY, 4)) continue;
            if (!_isTwoWay && Overlaps(room, _xpx, ExitLocalY, 4)) continue;

            // Marge 5 entre grandes salles et toutes les salles existantes
            bool ov = false;
            foreach (var r in Rooms)
                if (r.Overlaps(Expand(room, 5))) { ov = true; break; }
            if (!ov) return room;
        }
        return null;
    }

    private RectInt? TryPlaceSmallRoom(CaveConfig config, int maxAttempts)
    {
        for (int a = 0; a < maxAttempts; a++)
        {
            int w = _rng.Next(config.smallRoomMin, config.smallRoomMax + 1);
            int h = _rng.Next(config.smallRoomMin, config.smallRoomMax + 1);
            int x = _rng.Next(1, Width - w - 1);
            // Contraindre dans la zone de génération active (_caveBotY → _caveTopY)
            int minGenY = Mathf.Max(2, _caveBotY);
            int maxGenY = Mathf.Min(Height - h - 2, _caveTopY + 4);
            if (maxGenY <= minGenY) continue;
            int y = _rng.Next(minGenY, maxGenY);

            var room = new RectInt(x, y, w, h);

            // Marge 4 = gap de 4 cases → contour A | 2 rock | contour B = parois 2 épaisseurs
            if (Overlaps(room, _epx, EntryLocalY, 4)) continue;
            if (!_isTwoWay && Overlaps(room, _xpx, ExitLocalY, 4)) continue;

            bool ov = false;
            foreach (var r in Rooms) if (r.Overlaps(Expand(room, 4))) { ov = true; break; }
            if (!ov) return room;
        }
        return null;
    }

    private bool Overlaps(RectInt room, int px, int py, int margin) =>
        room.x <= px + margin && room.x + room.width >= px - margin &&
        room.y <= py + margin && room.y + room.height >= py - margin;

    /// <summary>
    /// Niveau d'érosion par coin : 0 = intact, 1 = 1 case, 3 = 3 cases.
    /// Tiré une fois par coin avec probabilités : 15% intact, 75% une case, 10% trois cases.
    /// </summary>
    private int[] RollCornerErosion()
    {
        var levels = new int[4]; // BL, BR, TL, TR
        for (int i = 0; i < 4; i++)
        {
            int roll = _rng.Next(100);
            if (roll < 15) levels[i] = 0; // 15% : coin intact
            else if (roll < 90) levels[i] = 1; // 75% : 1 case retirée
            else levels[i] = 3; // 10% : 3 cases retirées
        }
        return levels;
    }

    private void CarveRoom(RectInt room, bool isLarge = false)
    {
        var type = isLarge ? CaveCellType.LargeRoom : CaveCellType.SmallRoom;
        int[] erosion = RollCornerErosion(); // [BL, BR, TL, TR]

        for (int x = room.x; x < room.x + room.width; x++)
            for (int y = room.y; y < room.y + room.height; y++)
            {
                if (IsErodedCorner(room, x, y, erosion))
                    continue; // roche = forme organique arrondie

                SetFloor(x, y);
                if (InBounds(x, y)) _cellType[x, y] = type;
            }
        Rooms.Add(room);
    }

    /// <summary>
    /// Vérifie si (x,y) doit être érodé selon le niveau de chaque coin.
    /// Niveau 0 → rien. Niveau 1 → coin exact (dx=0,dy=0).
    /// Niveau 3 → coin + 2 adjacents : (0,0), (0,1), (1,0).
    /// </summary>
    private bool IsErodedCorner(RectInt room, int x, int y, int[] erosion)
    {
        int lx = x - room.x;
        int rx = room.x + room.width - 1 - x;
        int by = y - room.y;
        int ty = room.y + room.height - 1 - y;

        int dx = Mathf.Min(lx, rx);
        int dy = Mathf.Min(by, ty);

        // Identifier quel coin est le plus proche
        // BL=0, BR=1, TL=2, TR=3
        int corner;
        if (lx <= rx)
            corner = (by <= ty) ? 0 : 2; // gauche → BL ou TL
        else
            corner = (by <= ty) ? 1 : 3; // droite → BR ou TR

        int level = erosion[corner];
        if (level == 0) return false;                     // coin intact
        if (dx == 0 && dy == 0) return true;              // coin exact → toujours si level ≥ 1
        if (level >= 3 && dx + dy <= 1) return true;      // 3 cases : (0,1) et (1,0)
        return false;
    }

    // =========================================================================
    // IMPASSES — identique doc 17
    // =========================================================================

    /// <summary>
    /// Impasse exploratrice — cherche les zones vides hors de la forme principale,
    /// s'allonge quand elle a de l'espace libre devant elle.
    /// Sélection par score d'espace libre → les impasses divergent naturellement.
    /// </summary>
    private void CarveDeadEnd(CaveConfig config, List<Vector2Int> existing)
    {
        int minFree = config.deadEndMinFreeSpace; // espace libre min (défaut 6)

        // ── 1) Scanner les candidats : murs-contour qui donnent sur de l'espace vide ──
        var candidates = new List<(int x, int y, Vector2Int dir, int free)>();
        int minY = Mathf.Max(2, _caveBotY);
        int maxY = Mathf.Min(Height - 2, _caveTopY + 4);

        for (int x = 2; x < Width - 2; x++)
            for (int y = minY; y < maxY; y++)
            {
                if (_floor[x, y]) continue;
                if (!HasAdjacentFloor(x, y)) continue;

                // Exclure zones portails
                if (Mathf.Abs(x - _epx) < 2 && Mathf.Abs(y - EntryLocalY) < 3) continue;
                if (!_isTwoWay && Mathf.Abs(x - _xpx) < 2 && Mathf.Abs(y - ExitLocalY) < 3) continue;

                // Distance min entre impasses (Manhattan ≥ 10 pour vraie isolation)
                bool tooClose = false;
                foreach (var ep in existing)
                    if (Mathf.Abs(ep.x - x) + Mathf.Abs(ep.y - y) < 10) { tooClose = true; break; }
                if (tooClose) continue;

                // Pas dans/collé aux salles
                bool nearRoom = false;
                foreach (var r in Rooms)
                    if (x >= r.x - 2 && x < r.x + r.width + 2 &&
                        y >= r.y - 2 && y < r.y + r.height + 2)
                    { nearRoom = true; break; }
                if (nearRoom) continue;

                // Direction qui s'éloigne du sol existant
                var dir = GetAwayDirection(x, y);

                // Compter l'espace libre dans cette direction (max 30 cases)
                int free = 0;
                int sx = x + dir.x, sy = y + dir.y;
                while (free < 30 && InBounds(sx, sy) && !_floor[sx, sy])
                {
                    free++;
                    sx += dir.x;
                    sy += dir.y;
                }

                // Seuil relevé : les impasses doivent avoir assez d'espace pour s'isoler
                if (free >= minFree)
                    candidates.Add((x, y, dir, free));
            }

        if (candidates.Count == 0) return;

        // ── 2) Sélection pondérée : favoriser les candidats avec le plus d'espace ──
        //    Trier par espace libre décroissant, piocher dans le top 30%
        candidates.Sort((a, b) => b.free.CompareTo(a.free));
        int poolSize = Mathf.Max(1, candidates.Count * 30 / 100);
        var pick = candidates[_rng.Next(poolSize)];

        // ── 3) Longueur adaptative : plus d'espace → impasse plus longue ──
        int baseLen = config.deadEndLength;
        int adaptiveMax = Mathf.Min(baseLen + pick.free / 2, pick.free - 1);
        adaptiveMax = Mathf.Max(baseLen, adaptiveMax); // jamais plus court que baseLen
        int lenMin = Mathf.Max(4, baseLen - 2);
        int len = _rng.Next(lenMin, adaptiveMax + 1);
        int w = config.deadEndWidth; // largeur propre aux impasses (défaut 3)

        // ── 4) Creuser l'impasse sinueuse dans l'espace libre ──
        int cx = pick.x, cy = pick.y;
        var curDir = pick.dir;
        // D'abord, avancer de 1-2 cases dans la direction pour créer un couloir
        // de connexion étroit avant d'élargir → effet « entrée puis salle »
        int neckLen = Mathf.Min(2, len / 3);
        int neckW = Mathf.Max(2, w - 1);

        for (int i = 0; i < len; i++)
        {
            // Stop si on entre dans du sol existant ou hors bornes
            if (!InBounds(cx, cy) || (_floor[cx, cy] && i > 0)) break;

            // Largeur : étroite au début (cou), pleine ensuite
            int curW = (i < neckLen) ? neckW : w;

            // Creuser la largeur centrée sur la ligne de progression
            int halfW = curW / 2;
            for (int t = -halfW; t < curW - halfW; t++)
            {
                int fx, fy;
                if (curDir.x != 0) { fx = cx; fy = cy + t; }
                else { fx = cx + t; fy = cy; }
                if (InBounds(fx, fy))
                {
                    SetFloor(fx, fy);
                    _cellType[fx, fy] = CaveCellType.DeadEnd;
                }
            }

            // Avancer
            cx += curDir.x;
            cy += curDir.y;

            // Déviation latérale douce (20% de chance — moins que 30 pour rester lisible)
            if (i >= neckLen && _rng.Next(100) < 20)
            {
                if (curDir.x != 0)
                    cy += _rng.Next(2) == 0 ? 1 : -1;
                else
                    cx += _rng.Next(2) == 0 ? 1 : -1;
            }

            cx = Mathf.Clamp(cx, 2, Width - 3);
            cy = Mathf.Clamp(cy, 2, Height - 3);
        }
        existing.Add(new Vector2Int(pick.x, pick.y));
    }

    // =========================================================================
    // CONNECTIVITÉ — identique doc 17
    // =========================================================================

    private void EnsureConnectivity()
    {
        var start = new Vector2Int(_epx, EntryLocalY + 1);
        if (!IsFloor(start.x, start.y))
        {
            bool found = false;
            for (int dy = 1; dy <= 5 && !found; dy++)
                for (int dx = -2; dx <= _pw + 1 && !found; dx++)
                    if (IsFloor(_epx + dx, EntryLocalY + dy))
                    { start = new Vector2Int(_epx + dx, EntryLocalY + dy); found = true; }
        }

        var visited = new bool[Width, Height];
        var queue = new Queue<Vector2Int>();
        if (InBounds(start.x, start.y)) { queue.Enqueue(start); visited[start.x, start.y] = true; }

        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var d in _dirs4)
            {
                int nx = p.x + d.x, ny = p.y + d.y;
                if (!InBounds(nx, ny) || visited[nx, ny] || !_floor[nx, ny]) continue;
                visited[nx, ny] = true; queue.Enqueue(new Vector2Int(nx, ny));
            }
        }

        // Vérifier connectivité de la sortie (seulement si deux portails)
        if (!_isTwoWay)
        {
            var exitSol = new Vector2Int(_xpx, ExitLocalY + 1);
            if (InBounds(exitSol.x, exitSol.y) && IsFloor(exitSol.x, exitSol.y)
                && !visited[exitSol.x, exitSol.y])
            {
                Debug.LogWarning("[CaveLayout] Sortie inaccessible — connexion forcée.");
                CarveCorridor(start, exitSol, 2);
            }
        }

        foreach (var room in Rooms)
        {
            int cx = room.x + room.width / 2, cy = room.y + room.height / 2;
            if (InBounds(cx, cy) && IsFloor(cx, cy) && !visited[cx, cy])
            {
                Debug.LogWarning($"[CaveLayout] Salle isolée ({cx},{cy}) — connexion.");
                CarveCorridor(start, new Vector2Int(cx, cy), 2);
            }
        }
    }

    // =========================================================================
    // BORDS — identique doc 17
    // =========================================================================

    private void EnforceBorders()
    {
        for (int y = 0; y < Height; y++) { _floor[0, y] = false; _floor[Width - 1, y] = false; }
        for (int x = 0; x < Width; x++) { _floor[x, 0] = false; _floor[x, Height - 1] = false; }
    }

    // =========================================================================
    // SETFLOOR — identique doc 17
    // =========================================================================

    private void SetFloor(int x, int y)
    {
        if (!InBounds(x, y)) return;
        // Protection portail entrée — px-1..px+1 à py ET py-1 = hors-limite
        if (y == EntryLocalY && x >= _epx - 1 && x <= _epx + 1) return;
        if (y == EntryLocalY - 1 && x >= _epx - 1 && x <= _epx + 1) return;
        // Protection portail sortie (seulement si deux portails)
        if (!_isTwoWay)
        {
            if (y == ExitLocalY && x >= _xpx - 1 && x <= _xpx + 1) return;
            if (y == ExitLocalY - 1 && x >= _xpx - 1 && x <= _xpx + 1) return;
        }
        _floor[x, y] = true;
    }

    // =========================================================================
    // ENFORCE WALL THICKNESS — chaque mur doit être edge, inner ou corner.
    //
    // Configurations VALIDES pour un mur :
    //   • edge   (1 sol cardinal)                    → edgeN/S/E/W
    //   • inner  (2 sols cardinaux ADJACENTS : S+E, S+W, N+E, N+W)  → innerNW/NE/SW/SE
    //   • corner (0 sol cardinal, 1+ diag)           → cornerNW/NE/SW/SE
    //   • roche  (0 sol cardinal, 0 diag)            → wallFull (fog)
    //
    // Configurations INVALIDES (mur trop fin) :
    //   • 2 sols cardinaux OPPOSÉS (N+S ou E+W)      → ex-corridor
    //   • 3 sols cardinaux                            → ex-deadEnd
    //   • 4 sols cardinaux                            → mur isolé dans le sol
    //
    // Correction : reconvertir en mur la case sol voisine la plus sûre.
    //
    // PROTECTIONS ANTI-BLOCAGE :
    //   1) Ne jamais remplir un couloir ou une impasse (CellType)
    //   2) Ne jamais remplir une case avec < 3 voisins sol cardinaux
    //   3) Ne jamais remplir un segment de passage (sols opposés N+S ou E+W)
    //   4) Si aucun candidat sûr → on laisse le mur tel quel (fallback wallFull)
    // =========================================================================

    private void EnforceWallThickness()
    {
        bool changed = true;
        int maxPass = 50;
        while (changed && maxPass-- > 0)
        {
            changed = false;
            for (int y = 1; y < Height - 1; y++)
                for (int x = 1; x < Width - 1; x++)
                {
                    if (_floor[x, y]) continue;

                    // ── Vérifier si ce mur est valide pour nos 3 pièces ──────
                    if (IsValidWallConfig(x, y)) continue;

                    int bestX = -1, bestY = -1;
                    int bestScore = 999;

                    foreach (var d in _dirs4)
                    {
                        int nx = x + d.x, ny = y + d.y;
                        if (!InBounds(nx, ny) || !_floor[nx, ny]) continue;
                        if (IsPortalProtected(nx, ny)) continue;

                        // ── Protection 1 : ne jamais boucher un couloir ou impasse ──
                        var ct = _cellType[nx, ny];
                        if (ct == CaveCellType.Corridor || ct == CaveCellType.DeadEnd)
                            continue;

                        int score = CountCardinalFloors(nx, ny);

                        // ── Protection 2 : minimum 3 voisins sol (intérieur de salle) ──
                        if (score < 3) continue;

                        // ── Protection 3 : pas un segment de passage (sols opposés) ──
                        if (IsPassageSegment(nx, ny)) continue;

                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestX = nx; bestY = ny;
                        }
                    }

                    if (bestX >= 0)
                    {
                        // Épaissir : reconvertir le sol voisin en mur
                        _floor[bestX, bestY] = false;
                        _cellType[bestX, bestY] = CaveCellType.None;
                        changed = true;
                    }
                    else
                    {
                        // Aucun candidat sûr → fusionner : le mur devient sol
                        // Plutôt qu'un mur fin impossible à afficher,
                        // on ouvre le passage (épaisseur 0 = pas de mur).
                        _floor[x, y] = true;
                        _cellType[x, y] = CaveCellType.Corridor;
                        changed = true;
                    }
                }
        }
    }

    /// <summary>
    /// Un mur est valide s'il correspond à edge, inner ou corner :
    ///   0 sol cardinal               → roche ou corner (valide)
    ///   1 sol cardinal               → edge (valide)
    ///   2 sols cardinaux adjacents   → inner (valide)
    ///   2 sols cardinaux opposés     → INVALIDE (ex-corridor, mur trop fin)
    ///   3+ sols cardinaux            → INVALIDE (ex-deadEnd / mur isolé)
    /// </summary>
    private bool IsValidWallConfig(int x, int y)
    {
        bool n = InBounds(x, y + 1) && _floor[x, y + 1];
        bool s = InBounds(x, y - 1) && _floor[x, y - 1];
        bool e = InBounds(x + 1, y) && _floor[x + 1, y];
        bool w = InBounds(x - 1, y) && _floor[x - 1, y];

        int cf = (n ? 1 : 0) + (s ? 1 : 0) + (e ? 1 : 0) + (w ? 1 : 0);

        if (cf <= 1) return true;   // roche, corner, ou edge
        if (cf >= 3) return false;  // 3-4 sols = invalide

        // cf == 2 : valide seulement si adjacents (pas opposés)
        return !((n && s) || (e && w));
    }

    /// <summary>Nombre de voisins cardinaux qui sont sol.</summary>
    private int CountCardinalFloors(int x, int y)
    {
        int c = 0;
        if (InBounds(x, y + 1) && _floor[x, y + 1]) c++;
        if (InBounds(x, y - 1) && _floor[x, y - 1]) c++;
        if (InBounds(x + 1, y) && _floor[x + 1, y]) c++;
        if (InBounds(x - 1, y) && _floor[x - 1, y]) c++;
        return c;
    }

    /// <summary>
    /// La case sol a-t-elle des sols sur deux côtés opposés ? (N+S ou E+W)
    /// Si oui, c'est un segment de passage — le remplir bloquerait la circulation.
    /// </summary>
    private bool IsPassageSegment(int x, int y)
    {
        bool n = InBounds(x, y + 1) && _floor[x, y + 1];
        bool s = InBounds(x, y - 1) && _floor[x, y - 1];
        bool e = InBounds(x + 1, y) && _floor[x + 1, y];
        bool w = InBounds(x - 1, y) && _floor[x - 1, y];
        return (n && s) || (e && w);
    }

    /// <summary>La case est-elle dans la zone protégée d'un portail ?</summary>
    private bool IsPortalProtected(int x, int y)
    {
        if (x >= _epx - 1 && x <= _epx + 1 &&
            y >= EntryLocalY - 1 && y <= EntryLocalY + 1)
            return true;
        if (!_isTwoWay &&
            x >= _xpx - 1 && x <= _xpx + 1 &&
            y >= ExitLocalY - 1 && y <= ExitLocalY + 1)
            return true;
        return false;
    }

    // =========================================================================
    // HELPERS — identique doc 17
    // =========================================================================

    private bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    private bool HasAdjacentFloor(int x, int y)
    {
        foreach (var d in _dirs4)
        { int nx = x + d.x, ny = y + d.y; if (InBounds(nx, ny) && _floor[nx, ny]) return true; }
        return false;
    }

    private Vector2Int GetAwayDirection(int x, int y)
    {
        foreach (var d in _dirs4)
        { int nx = x + d.x, ny = y + d.y; if (InBounds(nx, ny) && _floor[nx, ny]) return new Vector2Int(-d.x, -d.y); }
        return Vector2Int.up;
    }

    private RectInt Expand(RectInt r, int m) =>
        new RectInt(r.x - m, r.y - m, r.width + m * 2, r.height + m * 2);

    private static readonly Vector2Int[] _dirs4 =
        { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
}
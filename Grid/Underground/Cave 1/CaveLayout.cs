using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// CaveLayout — Génération structurelle cave, fidèle au chapitre 2.
///
/// MODIFICATION ADDITIVE : Height = assez grand pour les Y surface.
/// EntryLocalY = entryPortalY directement (pas centré Height/2).
/// Chaque cave occupe sa propre zone Y — pas de chevauchement.
/// </summary>
public class CaveLayout
{
    public int Width { get; }
    public int Height { get; }

    private readonly bool[,] _floor;

    public readonly List<RectInt> Rooms = new();
    public RectInt LargeRoom { get; private set; }

    public Vector2Int EntryPos { get; private set; }
    public Vector2Int ExitPos { get; private set; }
    public int EntryLocalY { get; private set; }
    public int ExitLocalY { get; private set; }

    private System.Random _rng;
    private int _epx, _xpx;
    private int _pw;

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

        // ── MODIFIÉ : Height assez grand pour les Y surface ───────────────────
        int maxY = Mathf.Max(entryPortalY, exitPortalY);
        Height = Mathf.Max(maxY + config.height / 2 + 10,
                           GridManager.Instance?.Height ?? config.height);

        _floor = new bool[Width, Height];
        _rng = config.seed >= 0 ? new System.Random(config.seed) : new System.Random();
        _pw = Mathf.Clamp(config.portalWidth, 1, 3);

        _epx = Mathf.Clamp(entryPortalX, 2, Width - _pw - 2);
        _xpx = Mathf.Clamp(exitPortalX, 2, Width - _pw - 2);

        // ── MODIFIÉ : Y locaux = Y surface directement ────────────────────────
        // Cave 1 (entryY=30, exitY=60) → EntryLocalY=30, ExitLocalY=60
        // Cave 2 (entryY=121, isTwoWay) → EntryLocalY=121, ExitLocalY=129
        // Pas de centrage Height/2 → chaque cave à sa propre zone Y
        const int marge = 4;
        int deltaY = isTwoWay ? 8 : Mathf.Abs(exitPortalY - entryPortalY);
        int baseY = Mathf.Min(entryPortalY, exitPortalY);

        if (isTwoWay)
        {
            EntryLocalY = Mathf.Clamp(baseY, marge, Height - marge - 1);
            ExitLocalY = Mathf.Clamp(EntryLocalY + 8, EntryLocalY + 4, Height - marge - 1);
        }
        else
        {
            EntryLocalY = Mathf.Clamp(baseY, marge, Height - deltaY - marge - 1);
            ExitLocalY = Mathf.Clamp(baseY + deltaY, EntryLocalY + 8, Height - marge - 1);
        }

        // ── TOUT LE RESTE IDENTIQUE AU DOC 17 ────────────────────────────────

        EntryPos = new Vector2Int(_epx, EntryLocalY);
        ExitPos = new Vector2Int(_xpx, ExitLocalY);

        CarvePortalJunction(_epx, EntryLocalY, config);
        CarvePortalJunction(_xpx, ExitLocalY, config);

        if (config.alwaysLargeRoom)
        {
            int lw = config.largeRoomWidth;
            int lh = config.largeRoomHeight;
            int lrx = Mathf.Clamp(Width / 2 - lw / 2, 1, Width - lw - 1);
            int mid = (EntryLocalY + ExitLocalY) / 2;
            int lry = Mathf.Clamp(mid - lh / 2, EntryLocalY + 4, ExitLocalY - lh - 1);
            lry = Mathf.Max(lry, 2);
            LargeRoom = new RectInt(lrx, lry, lw, lh);
            CarveRoom(LargeRoom);
        }

        for (int i = 0; i < config.smallRoomCount; i++)
        {
            var room = TryPlaceSmallRoom(config, 40);
            if (room.HasValue) CarveRoom(room.Value);
        }

        ConnectAll(config);

        var deadEndPositions = new List<Vector2Int>();
        for (int i = 0; i < config.deadEndCount; i++)
            CarveDeadEnd(config, deadEndPositions);

        EnsureConnectivity();
        EnforceBorders();

        StampPortalEdgeS(_epx, EntryLocalY);
        StampPortalEdgeS(_xpx, ExitLocalY);

        Debug.Log($"[CaveLayout] {Width}×{Height} {Rooms.Count} salles | " +
                  $"Entry=({EntryPos.x},{EntryPos.y}) Exit=({ExitPos.x},{ExitPos.y})");
    }

    // =========================================================================
    // CARVE PORTAL JUNCTION — identique doc 17
    // =========================================================================

    private void CarvePortalJunction(int px, int py, CaveConfig config)
    {
        int jw = Mathf.Max(4, config.corridorMaxWidth + 2);

        for (int dx = -1; dx <= 1; dx++)
        {
            SetFloor(px + dx, py + 1);
            SetFloor(px + dx, py + 2);
        }

        for (int i = 2; i <= jw; i++)
        {
            SetFloor(px - i, py + 1);
            SetFloor(px - i, py + 2);
            SetFloor(px - i, py + 3);
        }

        for (int i = 2; i <= jw; i++)
        {
            SetFloor(px + i, py + 1);
            SetFloor(px + i, py + 2);
            SetFloor(px + i, py + 3);
        }

        for (int i = -jw; i <= jw; i++)
            SetFloor(px + i, py + 3);
    }

    // =========================================================================
    // STAMP PORTAL edgeS — identique doc 17
    // =========================================================================

    private void StampPortalEdgeS(int px, int py)
    {
        if (!InBounds(px, py)) return;

        _floor[px, py] = false;
        if (InBounds(px - 1, py)) _floor[px - 1, py] = false;
        if (InBounds(px + 1, py)) _floor[px + 1, py] = false;

        for (int dy = -1; dy >= -3; dy--)
            if (InBounds(px, py + dy))
                _floor[px, py + dy] = false;

        if (InBounds(px, py + 1) && py + 1 < Height - 1)
            _floor[px, py + 1] = true;
    }

    // =========================================================================
    // CONNEXION — identique doc 17
    // =========================================================================

    private void ConnectAll(CaveConfig config)
    {
        var pts = new List<Vector2Int>();

        int entryLateralX = _epx < Width / 2 ? _epx + 3 : _epx - 3;
        entryLateralX = Mathf.Clamp(entryLateralX, 2, Width - 3);
        pts.Add(new Vector2Int(entryLateralX, EntryLocalY + 2));

        foreach (var r in Rooms)
            pts.Add(new Vector2Int(r.x + r.width / 2, r.y + r.height / 2));

        int exitLateralX = _xpx < Width / 2 ? _xpx + 3 : _xpx - 3;
        exitLateralX = Mathf.Clamp(exitLateralX, 2, Width - 3);
        pts.Add(new Vector2Int(exitLateralX, ExitLocalY + 2));

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

        while ((cx != to.x || cy != to.y) && steps++ < maxSteps)
        {
            int w = Mathf.Clamp(thick, 2, 4);
            int half = (w - 1) / 2;
            for (int dx = -half; dx <= half; dx++)
                for (int dy = -half; dy <= half; dy++)
                    SetFloor(cx + dx, cy + dy);

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

            cx = Mathf.Clamp(cx, 1, Width - thick - 1);
            cy = Mathf.Clamp(cy, 1, Height - thick - 1);
        }

        int fw = Mathf.Clamp(thick, 2, 4);
        int fhalf = (fw - 1) / 2;
        for (int dx = -fhalf; dx <= fhalf; dx++)
            for (int dy = -fhalf; dy <= fhalf; dy++)
                SetFloor(to.x + dx, to.y + dy);
    }

    // =========================================================================
    // SALLES — identique doc 17
    // =========================================================================

    private RectInt? TryPlaceSmallRoom(CaveConfig config, int maxAttempts)
    {
        for (int a = 0; a < maxAttempts; a++)
        {
            int w = _rng.Next(config.smallRoomMin, config.smallRoomMax + 1);
            int h = _rng.Next(config.smallRoomMin, config.smallRoomMax + 1);
            int x = _rng.Next(1, Width - w - 1);
            // Contraindre entre les portails (zone de génération active)
            int minGenY = Mathf.Max(2, EntryLocalY - 4);
            int maxGenY = Mathf.Min(Height - h - 2, ExitLocalY + 4);
            if (maxGenY <= minGenY) continue;
            int y = _rng.Next(minGenY, maxGenY);

            var room = new RectInt(x, y, w, h);

            if (Overlaps(room, _epx, EntryLocalY, 3)) continue;
            if (Overlaps(room, _xpx, ExitLocalY, 3)) continue;

            bool ov = false;
            foreach (var r in Rooms) if (r.Overlaps(Expand(room, 2))) { ov = true; break; }
            if (!ov) return room;
        }
        return null;
    }

    private bool Overlaps(RectInt room, int px, int py, int margin) =>
        room.x <= px + margin && room.x + room.width >= px - margin &&
        room.y <= py + margin && room.y + room.height >= py - margin;

    private void CarveRoom(RectInt room)
    {
        for (int x = room.x; x < room.x + room.width; x++)
            for (int y = room.y; y < room.y + room.height; y++)
                SetFloor(x, y);
        Rooms.Add(room);
    }

    // =========================================================================
    // IMPASSES — identique doc 17
    // =========================================================================

    private void CarveDeadEnd(CaveConfig config, List<Vector2Int> existing)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            int x = _rng.Next(2, Width - 2);
            // Contraindre entre les portails
            int minGenY = Mathf.Max(2, EntryLocalY - 4);
            int maxGenY = Mathf.Min(Height - 2, ExitLocalY + 4);
            if (maxGenY <= minGenY) continue;
            int y = _rng.Next(minGenY, maxGenY);

            if (_floor[x, y]) continue;
            if (!HasAdjacentFloor(x, y)) continue;

            if (Mathf.Abs(x - _epx) < 2 && Mathf.Abs(y - EntryLocalY) < 3) continue;
            if (Mathf.Abs(x - _xpx) < 2 && Mathf.Abs(y - ExitLocalY) < 3) continue;

            bool tooClose = false;
            foreach (var ep in existing)
                if (Mathf.Abs(ep.x - x) + Mathf.Abs(ep.y - y) < 5) { tooClose = true; break; }
            if (tooClose) continue;

            var dir = GetAwayDirection(x, y);
            int len = _rng.Next(3, config.deadEndLength + 1);
            int w = config.corridorMinWidth;

            for (int i = 0; i < len; i++)
            {
                int nx = x + dir.x * i, ny = y + dir.y * i;
                for (int t = 0; t < w; t++)
                {
                    if (dir.x != 0) SetFloor(nx, ny + t);
                    else SetFloor(nx + t, ny);
                }
            }
            existing.Add(new Vector2Int(x, y));
            return;
        }
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

        var exitSol = new Vector2Int(_xpx, ExitLocalY + 1);
        if (InBounds(exitSol.x, exitSol.y) && IsFloor(exitSol.x, exitSol.y)
            && !visited[exitSol.x, exitSol.y])
        {
            Debug.LogWarning("[CaveLayout] Sortie inaccessible — connexion forcée.");
            CarveCorridor(start, exitSol, 2);
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
        if (y == EntryLocalY && x >= _epx - 1 && x <= _epx + 1) return;
        if (y == EntryLocalY - 1 && x == _epx) return;
        if (y == ExitLocalY && x >= _xpx - 1 && x <= _xpx + 1) return;
        if (y == ExitLocalY - 1 && x == _xpx) return;
        _floor[x, y] = true;
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
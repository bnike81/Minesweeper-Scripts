using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// CaveLayout — Génère le plan sol/mur de la grotte.
///
/// Phase 1 du WFC : placement structurel.
///   1. Placer les portails (entrée bas, sortie haut)
///   2. Placer la grande salle (village goblin) au centre
///   3. Placer les petites salles aléatoirement
///   4. Connecter toutes les salles par des couloirs (2-4 cases de large)
///   5. Ajouter des impasses pour le loot
///   6. Vérifier la connectivité (flood fill)
///
/// Résultat : bool[width, height] où true = sol marchable, false = mur
/// </summary>
public class CaveLayout
{
    public int Width { get; }
    public int Height { get; }

    // true = sol (marchable), false = mur
    private readonly bool[,] _floor;

    // Salles placées (pour connexion et placement ennemi/village)
    public readonly List<RectInt> Rooms = new();
    public RectInt LargeRoom { get; private set; }

    // Portails
    public Vector2Int EntryPos { get; private set; }
    public Vector2Int ExitPos { get; private set; }

    private System.Random _rng;

    public bool IsFloor(int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Height && _floor[x, y];

    public bool[,] GetFloorMap() => _floor;

    // =========================================================================
    // GÉNÉRATION
    // =========================================================================

    public CaveLayout(CaveConfig config, Vector2Int entryPortal, Vector2Int exitPortal)
    {
        // Dimensions = même largeur que GridManager, hauteur limitée au max de la grille
        int maxHeight = GridManager.Instance?.Height ?? config.height;
        Width = GridManager.Instance?.Width ?? config.width;
        Height = Mathf.Min(config.height, maxHeight);
        _floor = new bool[Width, Height];
        _rng = config.seed >= 0
            ? new System.Random(config.seed)
            : new System.Random();

        // Positions des portails (forcées sur les bords)
        EntryPos = new Vector2Int(
            Mathf.Clamp(entryPortal.x, 1, Width - config.portalWidth - 1), 0);
        ExitPos = new Vector2Int(
            Mathf.Clamp(exitPortal.x, 1, Width - config.portalWidth - 1), Height - 1);

        // ── 1. Creuser les portails (1 case de large, connectés par un couloir) ─
        // Entrée (bas) : 1 case porte à y=0, puis couloir de 3 cases vers l'intérieur
        // La case (EntryPos.x, 0) est le sprite porte (portalEntry)
        // Les cases autour sont des murs qui ferment le contour
        CarveRect(EntryPos.x, 0, 1, 4);
        // Élargir le couloir d'entrée à 2 cases minimum
        CarveRect(EntryPos.x, 1, 2, 3);

        // Sortie (haut) : 1 case porte à y=Height-1, couloir de 3 cases
        CarveRect(ExitPos.x, Height - 4, 2, 3);
        CarveRect(ExitPos.x, Height - 1, 1, 1); // porte = 1 case

        // ── 2. Grande salle (village goblin) — toujours au centre ────────────
        if (config.alwaysLargeRoom)
        {
            int lrx = (Width - config.largeRoomWidth) / 2;
            int lry = Height / 2 - config.largeRoomHeight / 2;
            LargeRoom = new RectInt(lrx, lry, config.largeRoomWidth, config.largeRoomHeight);
            CarveRoom(LargeRoom);
        }

        // ── 3. Petites salles ────────────────────────────────────────────────
        for (int i = 0; i < config.smallRoomCount; i++)
        {
            var room = TryPlaceSmallRoom(config, 30);
            if (room.HasValue)
                CarveRoom(room.Value);
        }

        // ── 4. Connecter les salles ──────────────────────────────────────────
        ConnectAll(config);

        // ── 5. Impasses ──────────────────────────────────────────────────────
        // Impasses espacées (distance min 6 entre chaque)
        var deadEndPositions = new List<Vector2Int>();
        for (int i = 0; i < config.deadEndCount; i++)
            CarveDeadEnd(config, deadEndPositions, minDistance: 6);

        // ── 6. Post-traitement : formes organiques ─────────────────────────
        RoundRoomCorners();
        WidenNarrowPassages();
        SmoothWalls();

        // ── 7. Vérifier connectivité (après post-traitement) ─────────────
        EnsureConnectivity();

        Debug.Log($"[CaveLayout] Grotte {Width}×{Height} générée : " +
                  $"{Rooms.Count} salles, entrée={EntryPos}, sortie={ExitPos}");
    }

    // =========================================================================
    // SALLE
    // =========================================================================

    private RectInt? TryPlaceSmallRoom(CaveConfig config, int maxAttempts)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int w = _rng.Next(config.smallRoomMin, config.smallRoomMax + 1);
            int h = _rng.Next(config.smallRoomMin, config.smallRoomMax + 1);
            int x = _rng.Next(1, Width - w - 1);
            int y = _rng.Next(4, Height - h - 4); // éviter les bords portails

            var room = new RectInt(x, y, w, h);

            // Vérifier qu'elle ne chevauche pas une salle existante (marge 2)
            bool overlap = false;
            foreach (var r in Rooms)
            {
                if (r.Overlaps(Expand(room, 2)))
                { overlap = true; break; }
            }
            if (overlap) continue;

            return room;
        }
        return null;
    }

    private void CarveRoom(RectInt room)
    {
        CarveRect(room.x, room.y, room.width, room.height);
        Rooms.Add(room);
    }

    // =========================================================================
    // COULOIRS — connexion entre salles
    // =========================================================================

    private void ConnectAll(CaveConfig config)
    {
        // Points à connecter : entrée, sortie, centre de chaque salle
        var points = new List<Vector2Int>();
        points.Add(new Vector2Int(EntryPos.x + 1, 2));  // juste au-dessus du portail entrée
        foreach (var room in Rooms)
            points.Add(new Vector2Int(room.x + room.width / 2, room.y + room.height / 2));
        points.Add(new Vector2Int(ExitPos.x + 1, Height - 3)); // juste en-dessous du portail sortie

        // Connecter séquentiellement (entrée → salle 1 → salle 2 → ... → sortie)
        // Trier par Y pour un parcours logique du bas vers le haut
        points.Sort((a, b) => a.y.CompareTo(b.y));

        for (int i = 0; i < points.Count - 1; i++)
        {
            int corridorW = _rng.Next(config.corridorMinWidth, config.corridorMaxWidth + 1);
            CarveCorridor(points[i], points[i + 1], corridorW);
        }
    }

    /// <summary>
    /// Creuse un couloir pas-à-pas entre deux points.
    /// Utilise des déplacements diagonaux (x±1,y±1) pour des courbes douces.
    /// L'épaisseur varie entre 2 et corridorWidth le long du tracé.
    /// </summary>
    private void CarveCorridor(Vector2Int from, Vector2Int to, int corridorWidth)
    {
        int cx = from.x, cy = from.y;
        int thick = Mathf.Max(2, corridorWidth);

        int steps = 0;
        int maxSteps = (Width + Height) * 2; // sécurité anti-boucle

        while ((cx != to.x || cy != to.y) && steps < maxSteps)
        {
            steps++;

            // Creuser un carré d'épaisseur variable à la position courante
            int w = Mathf.Max(2, thick + _rng.Next(-1, 2)); // varier ±1
            w = Mathf.Min(w, 4); // cap à 4
            for (int dx = 0; dx < w; dx++)
                for (int dy = 0; dy < w; dy++)
                    SetFloor(cx + dx, cy + dy);

            // Direction vers la cible
            int diffX = to.x - cx;
            int diffY = to.y - cy;

            // Choix du prochain pas — favorise les diagonales pour des courbes douces
            if (diffX != 0 && diffY != 0 && _rng.Next(100) < 60)
            {
                // Pas diagonal (60% de chance quand on peut)
                cx += (diffX > 0) ? 1 : -1;
                cy += (diffY > 0) ? 1 : -1;
            }
            else if (Mathf.Abs(diffY) > Mathf.Abs(diffX))
            {
                // Plus loin en Y → avancer en Y (+ léger zigzag X)
                cy += (diffY > 0) ? 1 : -1;
                if (_rng.Next(100) < 25 && diffX != 0)
                    cx += (diffX > 0) ? 1 : -1;
            }
            else
            {
                // Plus loin en X → avancer en X (+ léger zigzag Y)
                cx += (diffX > 0) ? 1 : -1;
                if (_rng.Next(100) < 25 && diffY != 0)
                    cy += (diffY > 0) ? 1 : -1;
            }

            cx = Mathf.Clamp(cx, 0, Width - thick);
            cy = Mathf.Clamp(cy, 0, Height - thick);
        }
    }

    private void CarveHLine(int x1, int x2, int y, int thickness)
    {
        int minX = Mathf.Min(x1, x2);
        int maxX = Mathf.Max(x1, x2);
        for (int x = minX; x <= maxX; x++)
            for (int t = 0; t < thickness; t++)
                SetFloor(x, y + t);
    }

    private void CarveVLine(int x, int y1, int y2, int thickness)
    {
        int minY = Mathf.Min(y1, y2);
        int maxY = Mathf.Max(y1, y2);
        for (int y = minY; y <= maxY; y++)
            for (int t = 0; t < thickness; t++)
                SetFloor(x + t, y);
    }

    // =========================================================================
    // IMPASSES
    // =========================================================================

    private void CarveDeadEnd(CaveConfig config, List<Vector2Int> existing, int minDistance)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            int x = _rng.Next(2, Width - 2);
            int y = _rng.Next(5, Height - 5);

            if (_floor[x, y]) continue;
            if (!HasAdjacentFloor(x, y)) continue;

            // Vérifier distance avec les autres impasses
            bool tooClose = false;
            foreach (var ep in existing)
            {
                if (Mathf.Abs(ep.x - x) + Mathf.Abs(ep.y - y) < minDistance)
                { tooClose = true; break; }
            }
            if (tooClose) continue;

            // Direction : s'éloigner du sol le plus proche
            var dir = GetAwayDirection(x, y);
            int len = _rng.Next(3, config.deadEndLength + 1);
            int w = config.corridorMinWidth;

            for (int i = 0; i < len; i++)
            {
                int nx = x + dir.x * i;
                int ny = y + dir.y * i;
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
    // CONNECTIVITÉ
    // =========================================================================

    private void EnsureConnectivity()
    {
        // Flood fill depuis l'entrée
        var visited = new bool[Width, Height];
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(EntryPos);
        visited[EntryPos.x, EntryPos.y] = true;

        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var d in _dirs4)
            {
                int nx = p.x + d.x, ny = p.y + d.y;
                if (nx < 0 || nx >= Width || ny < 0 || ny >= Height) continue;
                if (visited[nx, ny] || !_floor[nx, ny]) continue;
                visited[nx, ny] = true;
                queue.Enqueue(new Vector2Int(nx, ny));
            }
        }

        // Vérifier que la sortie est accessible
        if (!visited[ExitPos.x, ExitPos.y])
        {
            Debug.LogWarning("[CaveLayout] Sortie non accessible ! Connexion forcée...");
            CarveCorridor(EntryPos, ExitPos, 2);
        }

        // Vérifier que toutes les salles sont accessibles
        foreach (var room in Rooms)
        {
            int cx = room.x + room.width / 2;
            int cy = room.y + room.height / 2;
            if (!visited[cx, cy])
            {
                Debug.LogWarning($"[CaveLayout] Salle ({cx},{cy}) non accessible ! Connexion...");
                CarveCorridor(EntryPos, new Vector2Int(cx, cy), 2);
            }
        }
    }

    // =========================================================================
    // POST-TRAITEMENT — formes organiques
    // =========================================================================

    /// <summary>
    /// Arrondit les coins des salles en retirant 1-2 cases à chaque angle.
    /// Donne un aspect caverne naturelle plutôt que rectangulaire.
    /// </summary>
    private void RoundRoomCorners()
    {
        foreach (var room in Rooms)
        {
            int x1 = room.x, y1 = room.y;
            int x2 = room.x + room.width - 1, y2 = room.y + room.height - 1;

            // Retirer 1 case à chaque coin SEULEMENT si pas de couloir adjacent.
            // Un couloir adjacent = case sol HORS de la salle à côté du coin.
            if (!HasCorridorNearCorner(x1, y1, -1, -1) && _rng.Next(100) < 80)
                _floor[x1, y1] = false;
            if (!HasCorridorNearCorner(x2, y1, +1, -1) && _rng.Next(100) < 80)
                _floor[x2, y1] = false;
            if (!HasCorridorNearCorner(x1, y2, -1, +1) && _rng.Next(100) < 80)
                _floor[x1, y2] = false;
            if (!HasCorridorNearCorner(x2, y2, +1, +1) && _rng.Next(100) < 80)
                _floor[x2, y2] = false;
        }
    }

    /// <summary>Vérifie si un couloir passe près d'un coin de salle.</summary>
    private bool HasCorridorNearCorner(int cx, int cy, int dx, int dy)
    {
        // Vérifier les 3 cases extérieures au coin
        int nx = cx + dx, ny = cy + dy;
        if (IsFloor(nx, cy)) return true;  // à côté horizontalement
        if (IsFloor(cx, ny)) return true;  // à côté verticalement
        if (IsFloor(nx, ny)) return true;  // en diagonale
        return false;
    }

    /// <summary>
    /// Nettoie les murs : supprime les cassures de 1 case et les murs isolés.
    ///  - Mur avec 3+ voisins sol → devient sol (cassure dans le mur comblée)
    ///  - Sol avec 0-1 voisin sol → devient mur (pixel isolé supprimé)
    ///  - Mur isolé entouré de sol → devient sol
    /// Répète jusqu'à stabilité (max 5 passes).
    /// </summary>
    private void SmoothWalls()
    {
        for (int pass = 0; pass < 5; pass++)
        {
            var snap = (bool[,])_floor.Clone();
            bool changed = false;

            for (int x = 1; x < Width - 1; x++)
                for (int y = 1; y < Height - 1; y++)
                {
                    int floorN = CountFloorNeighbors4(snap, x, y);

                    if (!snap[x, y])
                    {
                        // MUR avec 3+ voisins sol → cassure de 1 case → combler
                        if (floorN >= 3)
                        {
                            _floor[x, y] = true;
                            changed = true;
                        }
                    }
                    else
                    {
                        // SOL avec 0 ou 1 voisin sol → pixel isolé → retirer
                        if (floorN <= 1)
                        {
                            _floor[x, y] = false;
                            changed = true;
                        }
                    }
                }

            if (!changed) break;
        }
    }

    private int CountFloorNeighbors4(bool[,] grid, int x, int y)
    {
        int n = 0;
        if (x > 0 && grid[x - 1, y]) n++;
        if (x < Width - 1 && grid[x + 1, y]) n++;
        if (y > 0 && grid[x, y - 1]) n++;
        if (y < Height - 1 && grid[x, y + 1]) n++;
        return n;
    }

    /// <summary>
    /// Élimine les passages de 1 case de large (goulots d'étranglement).
    /// Tout passage doit faire minimum 2 cases de large.
    /// </summary>
    private void WidenNarrowPassages()
    {
        // Plusieurs passes pour gérer les cas en cascade
        for (int pass = 0; pass < 3; pass++)
        {
            var snapshot = (bool[,])_floor.Clone();
            bool changed = false;

            for (int x = 1; x < Width - 1; x++)
                for (int y = 1; y < Height - 1; y++)
                {
                    if (!snapshot[x, y]) continue;

                    // Passage horizontal étroit : murs au-dessus ET en-dessous
                    bool wallAbove = !snapshot[x, y + 1];
                    bool wallBelow = !snapshot[x, y - 1];
                    if (wallAbove && wallBelow)
                    {
                        // Élargir vers le haut (ou le bas si hors limites)
                        if (y + 1 < Height) { SetFloor(x, y + 1); changed = true; }
                        else if (y - 1 >= 0) { SetFloor(x, y - 1); changed = true; }
                    }

                    // Passage vertical étroit : murs à gauche ET à droite
                    bool wallLeft = !snapshot[x - 1, y];
                    bool wallRight = !snapshot[x + 1, y];
                    if (wallLeft && wallRight)
                    {
                        // Élargir vers la droite (ou la gauche)
                        if (x + 1 < Width) { SetFloor(x + 1, y); changed = true; }
                        else if (x - 1 >= 0) { SetFloor(x - 1, y); changed = true; }
                    }
                }

            if (!changed) break; // stable, on arrête
        }
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private void CarveRect(int x, int y, int w, int h)
    {
        for (int ix = x; ix < x + w; ix++)
            for (int iy = y; iy < y + h; iy++)
                SetFloor(ix, iy);
    }

    private void SetFloor(int x, int y)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
            _floor[x, y] = true;
    }

    private bool HasAdjacentFloor(int x, int y)
    {
        foreach (var d in _dirs4)
        {
            int nx = x + d.x, ny = y + d.y;
            if (nx >= 0 && nx < Width && ny >= 0 && ny < Height && _floor[nx, ny])
                return true;
        }
        return false;
    }

    private Vector2Int GetAwayDirection(int x, int y)
    {
        // Direction opposée au premier sol trouvé
        foreach (var d in _dirs4)
        {
            int nx = x + d.x, ny = y + d.y;
            if (nx >= 0 && nx < Width && ny >= 0 && ny < Height && _floor[nx, ny])
                return new Vector2Int(-d.x, -d.y);
        }
        return Vector2Int.up;
    }

    private RectInt Expand(RectInt r, int margin) =>
        new RectInt(r.x - margin, r.y - margin,
                    r.width + margin * 2, r.height + margin * 2);

    private static readonly Vector2Int[] _dirs4 =
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
    };
}
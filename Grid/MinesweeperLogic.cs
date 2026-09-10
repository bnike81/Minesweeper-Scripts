using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// MinesweeperLogic — Logique pure du démineur.
/// Aucune dépendance Unity (pas de MonoBehaviour).
/// Testable de façon isolée.
/// Gère : placement dangers, calcul adjacences, flood fill, chord click.
/// </summary>
public static class MinesweeperLogic
{
    // ─── Directions adjacentes (8 directions) ─────────────────────────────────
    private static readonly (int dx, int dy)[] Directions =
    {
        (-1, -1), (0, -1), (1, -1),
        (-1,  0),          (1,  0),
        (-1,  1), (0,  1), (1,  1)
    };

    // ─── Calcul de tous les chiffres d'adjacence de la grille ─────────────────
    /// <summary>
    /// Pour chaque case non-dangereuse, compte les dangers adjacents.
    /// Met à jour Content = Number si count > 0, sinon Empty.
    /// </summary>
    public static void ComputeAllAdjacencies(Cell[,] grid, int width, int height)
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var cell = grid[x, y];
                if (cell.CountsAsAdjacentDanger) continue;

                int count = CountAdjacentDangers(grid, x, y, width, height);
                cell.AdjacentDangerCount = count;
                cell.Content = count > 0 ? CellContent.Number : CellContent.Empty;
            }
        }
    }

    // ─── Compte les dangers adjacents à une case ──────────────────────────────
    public static int CountAdjacentDangers(Cell[,] grid, int x, int y, int width, int height)
    {
        int count = 0;
        foreach (var (dx, dy) in Directions)
        {
            int nx = x + dx, ny = y + dy;
            if (IsInBounds(nx, ny, width, height) && grid[nx, ny].CountsAsAdjacentDanger)
                count++;
        }
        return count;
    }

    // ─── Adjacences partielles (optimisé pour ExtendGrid) ────────────────────
    /// <summary>
    /// Recalcule les adjacences uniquement dans la plage Y donnée.
    /// Utilisé par ExtendGrid pour ne recalculer que la frontière.
    /// </summary>
    public static void ComputeAdjacenciesInRange(
        Cell[,] grid, int width, int height, int yStart, int yEnd)
    {
        for (int x = 0; x < width; x++)
            for (int y = yStart; y < yEnd; y++)
            {
                if (y >= height) break;
                var cell = grid[x, y];
                if (cell == null || cell.CountsAsAdjacentDanger) continue;
                int count = CountAdjacentDangers(grid, x, y, width, height);
                cell.AdjacentDangerCount = count;
                cell.Content = count > 0 ? CellContent.Number : CellContent.Empty;
            }
    }

    // ─── Flood Fill — Révélation en cascade ───────────────────────────────────
    // Collections statiques pré-allouées → zéro allocation mémoire à chaque clic
    private static readonly List<Cell> _revealedBuffer = new List<Cell>(512);
    private static readonly Queue<(int x, int y)> _toProcessBuffer = new Queue<(int x, int y)>();
    private static readonly HashSet<(int, int)> _visitedBuffer = new HashSet<(int, int)>();

    /// <summary>
    /// Révèle en cascade toutes les cases vides connectées.
    /// Retourne la liste de toutes les cases révélées.
    /// </summary>
    public static List<Cell> FloodFill(Cell[,] grid, int startX, int startY, int width, int height)
    {
        // Réutiliser les collections — pas de new allocation à chaque appel
        _revealedBuffer.Clear();
        _toProcessBuffer.Clear();
        _visitedBuffer.Clear();

        var revealed = _revealedBuffer;
        var toProcess = _toProcessBuffer;
        var visited = _visitedBuffer;

        toProcess.Enqueue((startX, startY));
        visited.Add((startX, startY));

        while (toProcess.Count > 0)
        {
            var (x, y) = toProcess.Dequeue();
            var cell = grid[x, y];

            if (cell.IsFlagged) continue;
            if (cell.IsRevealed) continue;
            if (cell.IsMountainReserved) continue; // montagne = pas de révélation par flood
            if (cell.CountsAsAdjacentDanger) continue;

            cell.Reveal();
            revealed.Add(cell);

            if (cell.IsEmpty)
            {
                foreach (var (dx, dy) in Directions)
                {
                    int nx = x + dx, ny = y + dy;
                    if (IsInBounds(nx, ny, width, height) && !visited.Contains((nx, ny)))
                    {
                        visited.Add((nx, ny));
                        toProcess.Enqueue((nx, ny));
                    }
                }
            }
        }

        return revealed;
    }

    // ─── Chord Click ──────────────────────────────────────────────────────────
    /// <summary>
    /// Si le nombre de flags adjacents == chiffre affiché,
    /// révèle toutes les cases adjacentes non-flaggées.
    /// Retourne null si chord non applicable.
    /// </summary>
    public static List<Cell> TryChordClick(Cell[,] grid, int x, int y, int width, int height)
    {
        var cell = grid[x, y];
        if (!cell.IsRevealed || !cell.IsNumber) return null;

        int flagCount = CountAdjacentFlags(grid, x, y, width, height);
        if (flagCount != cell.AdjacentDangerCount) return null;

        // Réutiliser le buffer statique
        _revealedBuffer.Clear();
        var revealed = _revealedBuffer;
        foreach (var (dx, dy) in Directions)
        {
            int nx = x + dx, ny = y + dy;
            if (!IsInBounds(nx, ny, width, height)) continue;

            var neighbor = grid[nx, ny];
            if (neighbor.IsHidden && !neighbor.IsFlagged)
            {
                if (neighbor.IsEmpty || neighbor.IsNumber)
                {
                    var cascadeRevealed = FloodFill(grid, nx, ny, width, height);
                    revealed.AddRange(cascadeRevealed);
                }
                else
                {
                    neighbor.Reveal();
                    revealed.Add(neighbor);
                }
            }
        }
        return revealed;
    }

    // ─── Compte les flags adjacents ───────────────────────────────────────────
    public static int CountAdjacentFlags(Cell[,] grid, int x, int y, int width, int height)
    {
        int count = 0;
        foreach (var (dx, dy) in Directions)
        {
            int nx = x + dx, ny = y + dy;
            if (IsInBounds(nx, ny, width, height) && grid[nx, ny].IsFlagged)
                count++;
        }
        return count;
    }

    // ─── Vérification victoire ────────────────────────────────────────────────
    public static bool CheckVictory(Cell[,] grid, int width, int height)
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var cell = grid[x, y];
                if (!cell.CountsAsAdjacentDanger && !cell.IsRevealed)
                    return false;
            }
        }
        return true;
    }

    // ─── Obtenir les voisins ──────────────────────────────────────────────────
    // Buffer statique pré-alloué → zéro allocation à chaque appel
    private static readonly List<Cell> _neighborsBuffer = new List<Cell>(8);

    public static List<Cell> GetNeighbors(Cell[,] grid, int x, int y, int width, int height)
    {
        _neighborsBuffer.Clear();
        foreach (var (dx, dy) in Directions)
        {
            int nx = x + dx, ny = y + dy;
            if (IsInBounds(nx, ny, width, height))
                _neighborsBuffer.Add(grid[nx, ny]);
        }
        return _neighborsBuffer;
    }

    // ─── Révèle tout (fin de partie / debug) ──────────────────────────────────
    public static void RevealAll(Cell[,] grid, int width, int height)
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                grid[x, y].ForceReveal();
    }

    // ─── Utilitaire ───────────────────────────────────────────────────────────
    public static bool IsInBounds(int x, int y, int width, int height)
        => x >= 0 && x < width && y >= 0 && y < height;

    public static int CountTotal(Cell[,] grid, int width, int height, System.Func<Cell, bool> predicate)
    {
        int count = 0;
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (predicate(grid[x, y])) count++;
        return count;
    }
}
// ================================================================
// FloodFillBatcher.cs
// Fix #2 — Étale la révélation des cases sur plusieurs frames
//
// SETUP :
//   1. Crée un GameObject vide → nomme-le "FloodFillBatcher"
//   2. Ajoute ce script dessus
//   3. Dans GridManager : remplace l'appel direct au flood fill
//      par StartCoroutine(FloodFillBatcher.Instance.Run(...))
//
// DEUX MODES :
//   • Run()          → si ton flood fill part d'une case de départ (BFS)
//   • RunFromList()  → si ton flood fill calcule d'abord une liste de cases
// ================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FloodFillBatcher : MonoBehaviour
{
    // ── Singleton ──────────────────────────────────────────────
    public static FloodFillBatcher Instance { get; private set; }

    // ── Inspector ──────────────────────────────────────────────
    [Header("Performance")]
    [Tooltip("Nombre de cases révélées par frame. Augmente si la grille est lente, diminue si des saccades persistent.")]
    [Range(5, 100)]
    [SerializeField] private int casesParFrame = 20;

    [Tooltip("Taille initiale des collections BFS (évite les réallocations).")]
    [SerializeField] private int estimatedGridSize = 300;

    // ── Événements ────────────────────────────────────────────
    /// <summary>Déclenché quand le flood fill est terminé.</summary>
    public event Action OnFloodFillComplete;

    /// <summary>Déclenché à chaque frame (utile pour des effets de progression).</summary>
    public event Action<int> OnCellRevealed;   // paramètre = index de la case révélée

    // ── État ──────────────────────────────────────────────────
    public bool IsRunning { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ══════════════════════════════════════════════════════════
    //  MODE 1 — BFS depuis une case de départ (le plus courant)
    // ══════════════════════════════════════════════════════════
    /// <summary>
    /// Lance un flood fill BFS en coroutine, par lots de <see cref="casesParFrame"/> cases/frame.
    ///
    /// Exemple d'appel dans GridManager :
    /// <code>
    ///   StartCoroutine(FloodFillBatcher.Instance.Run(
    ///       startCell:    clickedCellCoords,
    ///       canReveal:    coords => !_grid[coords].IsRevealed &amp;&amp; !_grid[coords].IsMine,
    ///       getNeighbors: coords => GetAdjacentCells(coords),
    ///       revealCell:   coords => RevealCell(coords)
    ///   ));
    /// </code>
    /// </summary>
    public IEnumerator Run(
        Vector2Int startCell,
        Func<Vector2Int, bool> canReveal,
        Func<Vector2Int, IEnumerable<Vector2Int>> getNeighbors,
        Action<Vector2Int> revealCell)
    {
        if (IsRunning)
        {
            Debug.LogWarning("[FloodFillBatcher] Un flood fill est déjà en cours — ignoré.");
            yield break;
        }

        IsRunning = true;

        var visited = new HashSet<Vector2Int>(estimatedGridSize);
        var queue = new Queue<Vector2Int>(estimatedGridSize);
        int totalRevealed = 0;
        int countThisFrame = 0;

        visited.Add(startCell);
        queue.Enqueue(startCell);

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();

            if (canReveal(cell))
            {
                revealCell(cell);
                totalRevealed++;
                countThisFrame++;
                OnCellRevealed?.Invoke(totalRevealed);

                // Ajoute les voisins valides dans la file
                foreach (var neighbor in getNeighbors(cell))
                {
                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // ── Pause toutes les N cases ──────────────────────
            if (countThisFrame >= casesParFrame)
            {
                countThisFrame = 0;
                yield return null;   // reprend à la frame suivante
            }
        }

        IsRunning = false;
        OnFloodFillComplete?.Invoke();

        Debug.Log($"[FloodFillBatcher] Flood fill terminé : {totalRevealed} cases révélées.");
    }

    // ══════════════════════════════════════════════════════════
    //  MODE 2 — Liste pré-calculée (si ton flood fill calcule
    //            d'abord toutes les cases avant de les révéler)
    // ══════════════════════════════════════════════════════════
    /// <summary>
    /// Révèle une liste de cases pré-calculée, par lots.
    ///
    /// Exemple d'appel dans GridManager :
    /// <code>
    ///   List&lt;Vector2Int&gt; cellsToReveal = ComputeFloodFill(startCell);
    ///   StartCoroutine(FloodFillBatcher.Instance.RunFromList(cellsToReveal, RevealCell));
    /// </code>
    /// </summary>
    public IEnumerator RunFromList(
        List<Vector2Int> cellsToReveal,
        Action<Vector2Int> revealCell)
    {
        if (IsRunning)
        {
            Debug.LogWarning("[FloodFillBatcher] Un flood fill est déjà en cours — ignoré.");
            yield break;
        }

        if (cellsToReveal == null || cellsToReveal.Count == 0)
        {
            OnFloodFillComplete?.Invoke();
            yield break;
        }

        IsRunning = true;

        for (int i = 0; i < cellsToReveal.Count; i++)
        {
            revealCell(cellsToReveal[i]);
            OnCellRevealed?.Invoke(i + 1);

            if ((i + 1) % casesParFrame == 0)
                yield return null;
        }

        IsRunning = false;
        OnFloodFillComplete?.Invoke();

        Debug.Log($"[FloodFillBatcher] RunFromList terminé : {cellsToReveal.Count} cases révélées.");
    }

    // ══════════════════════════════════════════════════════════
    //  UTILITAIRES
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Arrête un flood fill en cours (ex : si le joueur quitte la scène).
    /// </summary>
    public void Stop()
    {
        StopAllCoroutines();
        IsRunning = false;
    }

    /// <summary>
    /// Helper : retourne les 4 voisins cardinaux d'une case.
    /// Copie ou adapte cette méthode dans GridManager si tu n'en as pas déjà une.
    /// </summary>
    public static IEnumerable<Vector2Int> GetCardinalNeighbors(Vector2Int cell)
    {
        yield return cell + Vector2Int.up;
        yield return cell + Vector2Int.down;
        yield return cell + Vector2Int.left;
        yield return cell + Vector2Int.right;
    }

    /// <summary>
    /// Helper : retourne les 8 voisins (cardinaux + diagonales).
    /// </summary>
    public static IEnumerable<Vector2Int> GetAllNeighbors(Vector2Int cell)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                yield return new Vector2Int(cell.x + dx, cell.y + dy);
            }
    }
}
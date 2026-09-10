using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ArcherWatcher - Surveille les archers cachés et déclenche leur révélation
/// quand le héros entre dans leur portée de tir.
///
/// FONCTIONNEMENT :
///   • Souscrit à OnCellsRevealed (événement de batch).
///   • Pour chaque case révélée, scan les cases voisines non-révélées.
///   • Si une case non-révélée = archer ET héros dans la portée → ForceRevealArcher.
///   • ForceRevealArcher marque la case révélée, spawne l'EnemyInstance,
///     qui déclenche Initialize → SubscribeArcherEvents → StartBehaviour
///     → CheckArcherTrigger → l'archer attaque.
///
/// SETUP SCÈNE :
///   Créer un GameObject "ArcherWatcher" et y attacher ce script.
///   Aucun autre setup nécessaire.
/// </summary>
public class ArcherWatcher : MonoBehaviour
{
    public static ArcherWatcher Instance { get; private set; }

    [Header("Portée")]
    [Tooltip("Distance max (Chebyshev) à laquelle l'archer détecte une case révélée. " +
             "Doit correspondre à _archerRange dans EnemyBehaviour.")]
    [SerializeField, Range(2, 6)] private int _detectionRange = 4;

    // Évite de traiter le même archer deux fois dans un même événement
    private readonly HashSet<(int, int)> _checked = new HashSet<(int, int)>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnCellsRevealed>(OnCellsRevealed);
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnCellsRevealed>(OnCellsRevealed);
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
    }

    private void OnRunStarted(OnRunStarted e)
    {
        _checked.Clear();
    }

    private void OnCellsRevealed(OnCellsRevealed evt)
    {
        var gm = GridManager.Instance;
        var hero = HeroController.Instance;
        if (gm == null || hero == null || evt.Cells == null) return;
        if (!hero.HasAppeared) return;

        // FIX "Collection was modified" :
        // ForceRevealArcher() publie un nouveau OnCellsRevealed pendant qu'on itère evt.Cells.
        // Un snapshot ToArray() immunise notre boucle contre toute modification de la liste source.
        var cells = evt.Cells.ToArray();

        foreach (var c in cells)
        {
            for (int dx = -_detectionRange; dx <= _detectionRange; dx++)
                for (int dy = -_detectionRange; dy <= _detectionRange; dy++)
                {
                    int nx = c.X + dx;
                    int ny = c.Y + dy;

                    if (nx < 0 || ny < 0 || nx >= gm.Width || ny >= gm.Height) continue;
                    if (_checked.Contains((nx, ny))) continue;

                    var cell = gm.GetCell(nx, ny);
                    if (cell == null || cell.IsRevealed) continue;
                    if (cell.Content != CellContent.Enemy_BanditArcher) continue;

                    _checked.Add((nx, ny));

                    int heroDist = Mathf.Max(
                        Mathf.Abs(hero.GridPosition.x - nx),
                        Mathf.Abs(hero.GridPosition.y - ny));

                    if (heroDist < 1 || heroDist > _detectionRange) continue;

                    MainGrid.Instance?.ForceRevealArcher(nx, ny);
                }
        }

        _checked.Clear();
    }
}
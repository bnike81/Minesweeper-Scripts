using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ArenaAutoCombat — AI simple pour automatiser le héros dans l arène.
///
/// Fonctionne en MODE LIBRE (TurnState.Inactive) et en COMBAT (HeroTurn).
/// Mode libre : le héros marche vers l ennemi le plus proche pour entrer
///              dans son perception radius et déclencher le combat.
/// Combat     : le héros attaque / MoveToAndAttack selon la distance.
///
/// Diagnostic : logs toutes les secondes l etat hero + TurnManager pour
///              reperer les blocages.
///
/// TOUCHES :
///   A → Toggle auto-combat ON/OFF
///   P → Pause / Resume
///   D → Afficher immediatement un snapshot de diagnostic
/// </summary>
public class ArenaAutoCombat : MonoBehaviour
{
    [Header("Options")]
    [Tooltip("Activer au Start sans attendre la touche A.")]
    public bool autoStartEnabled = false;

    [Tooltip("Délai entre chaque décision de l AI (secondes).")]
    public float decisionInterval = 0.4f;

    [Tooltip("Afficher chaque décision en console.")]
    public bool verbose = true;

    [Header("Diagnostic")]
    [Tooltip("Log de l etat hero + TurnManager toutes les X secondes.")]
    public float diagnosticInterval = 1.0f;

    [Tooltip("Afficher le diagnostic en continu, meme sans AutoCombat.")]
    public bool continuousDiagnostic = true;

    private bool _enabled;
    private bool _paused;
    private Coroutine _loop;
    private float _lastDiagnostic;

    private void Start()
    {
        if (autoStartEnabled)
            SetEnabled(true);
        LogKeyBindings();
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb[Key.A].wasPressedThisFrame)
            {
                SetEnabled(!_enabled);
                Debug.Log($"<color=#FFDD44>[AutoCombat]</color> {(_enabled ? "ON" : "OFF")}");
            }
            if (kb[Key.P].wasPressedThisFrame && _enabled)
            {
                _paused = !_paused;
                Debug.Log($"<color=#FFDD44>[AutoCombat]</color> {(_paused ? "PAUSE" : "RESUME")}");
            }
            if (kb[Key.D].wasPressedThisFrame)
            {
                LogDiagnostic("manual");
            }
        }

        // Diagnostic periodique
        if (continuousDiagnostic && Time.time - _lastDiagnostic >= diagnosticInterval)
        {
            _lastDiagnostic = Time.time;
            LogDiagnostic("periodic");
        }
    }

    public void SetEnabled(bool on)
    {
        _enabled = on;
        _paused = false;

        if (_loop != null) { StopCoroutine(_loop); _loop = null; }
        if (_enabled) _loop = StartCoroutine(AutoLoop());
    }

    private IEnumerator AutoLoop()
    {
        var wait = new WaitForSeconds(decisionInterval);

        while (_enabled)
        {
            if (_paused) { yield return null; continue; }

            // Hero doit pouvoir agir
            var hero = HeroController.Instance;
            var state = HeroState.Instance;
            var combat = HeroCombat.Instance;
            var tm = TurnManager.Instance;
            if (hero == null || state == null || combat == null)
            {
                yield return null; continue;
            }

            // Attendre que le hero soit libre de bouger
            if (!state.CanMove || combat.IsAnimating)
            {
                yield return null; continue;
            }

            // Trouver l ennemi le plus proche
            var target = FindClosestEnemy(hero.GridPosition);
            if (target == null)
            {
                if (verbose) Debug.Log("<color=#FFDD44>[AutoCombat]</color> Plus d ennemi — arrêt");
                SetEnabled(false);
                yield break;
            }

            int dist = Mathf.Max(
                Mathf.Abs(target.GridX - hero.GridPosition.x),
                Mathf.Abs(target.GridY - hero.GridPosition.y));

            TurnState tState = tm != null ? tm.State : TurnState.Inactive;

            if (verbose) Debug.Log($"<color=#FFDD44>[AutoCombat]</color> " +
                $"hero={hero.GridPosition} target=({target.GridX},{target.GridY}) " +
                $"dist={dist} state={tState}");

            // === Decision selon etat + distance ===

            // En combat (HeroTurn) : attaque directe a dist 1 ou 2
            if (tState == TurnState.HeroTurn)
            {
                if (dist == 1)
                    combat.RequestAttack(target);
                else if (dist == 2)
                    hero.MoveToAndAttack(target);
                else
                    MoveTowardsEnemy(hero, target); // approche en combat (rare)
            }
            // En mode libre (Inactive) : se rapprocher pour entrer dans le perception radius
            else if (tState == TurnState.Inactive)
            {
                MoveTowardsEnemy(hero, target);
            }
            // Tour ennemi : on attend
            else
            {
                yield return null; continue;
            }

            // Attendre que l action soit traitee
            yield return wait;
        }
    }

    /// <summary>Déplace le hero vers une case adjacente à l ennemi.</summary>
    private void MoveTowardsEnemy(HeroController hero, EnemyInstance target)
    {
        var adjacent = FindApproachCell(hero.GridPosition, target);
        if (!adjacent.HasValue)
        {
            Debug.LogWarning("<color=#FF8800>[AutoCombat]</color> Pas de case adjacente libre");
            return;
        }

        var grid = ActiveGrid.Current;
        if (grid == null) return;

        // Utiliser reflection pour PathfindAndMove (private)
        var method = typeof(HeroController).GetMethod(
            "PathfindAndMove",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (method == null)
        {
            Debug.LogWarning("<color=#FF8800>[AutoCombat]</color> PathfindAndMove introuvable");
            return;
        }

        var coro = (IEnumerator)method.Invoke(
            hero, new object[] { hero.GridPosition, adjacent.Value, grid });
        hero.StartCoroutine(coro);
    }

    private EnemyInstance FindClosestEnemy(Vector2Int heroPos)
    {
        EnemyInstance best = null;
        int bestDist = int.MaxValue;
        foreach (var ei in EnemyInstance.All)
        {
            if (ei == null || ei.IsDead) continue;
            int d = Mathf.Max(
                Mathf.Abs(ei.GridX - heroPos.x),
                Mathf.Abs(ei.GridY - heroPos.y));
            if (d < bestDist)
            {
                bestDist = d;
                best = ei;
            }
        }
        return best;
    }

    private Vector2Int? FindApproachCell(Vector2Int heroPos, EnemyInstance target)
    {
        var grid = ActiveGrid.Current;
        if (grid == null) return null;

        var enemyPos = new Vector2Int(target.GridX, target.GridY);
        Vector2Int[] dirs = {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right,
            new Vector2Int(1,1), new Vector2Int(1,-1),
            new Vector2Int(-1,1), new Vector2Int(-1,-1)
        };

        Vector2Int? best = null;
        int bestDist = int.MaxValue;

        foreach (var d in dirs)
        {
            var n = enemyPos + d;
            if (!grid.IsInBounds(n.x, n.y)) continue;
            if (grid.IsBlocked(n.x, n.y) && n != heroPos) continue;

            int dH = Mathf.Max(Mathf.Abs(n.x - heroPos.x), Mathf.Abs(n.y - heroPos.y));
            if (dH < bestDist)
            {
                bestDist = dH;
                best = n;
            }
        }
        return best;
    }

    // =========================================================================
    // DIAGNOSTIC
    // =========================================================================

    /// <summary>Log l etat du hero et du TurnManager — utile pour trouver blocages.</summary>
    private void LogDiagnostic(string trigger)
    {
        var hero = HeroController.Instance;
        var state = HeroState.Instance;
        var combat = HeroCombat.Instance;
        var tm = TurnManager.Instance;

        string sb = $"<color=#AAAAFF>[Diag/{trigger}]</color> ";

        if (hero == null) { sb += "Hero=null"; Debug.Log(sb); return; }
        sb += $"hero.pos={hero.GridPosition} ";

        if (state == null) sb += "HeroState=null ";
        else sb += $"UILocked={state.IsUILocked} Moving={state.IsMoving} " +
                   $"Calc={state.IsCalculating} Jumping={state.IsJumping} " +
                   $"CanMove={state.CanMove} Appeared={state.HasAppeared} ";

        if (combat != null)
            sb += $"combat.Anim={combat.IsAnimating} combat.InCombat={combat.IsInCombat} ";

        if (tm != null)
            sb += $"tm.State={tm.State} ";

        int alive = 0;
        foreach (var ei in EnemyInstance.All)
            if (ei != null && !ei.IsDead) alive++;
        sb += $"enemies_alive={alive}";

        Debug.Log(sb);
    }

    private void LogKeyBindings()
    {
        Debug.Log("<color=#FFDD44>[AutoCombat]</color> Touches : A=toggle, P=pause, D=diagnostic manuel");
    }
}
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// TurnManager v13 FINAL — Cycle tour par tour STRICT + Watchdog Global.
///
/// Fonctionnalites :
///   - _ignoreNextHeroAction : fix double attaque a la revelation
///   - WaitHeroFullyIdle / WaitEnemyActionDone / WaitHeroFlinchDone
///   - ForceUnlockHero + Watchdog Global (Inactive + HeroTurn, pas EnemyTurn)
///   - CleanupEnemyTurn + filet EnsureUnlockAfterDelay
///
/// VERSION PROPRE : variables legacy _heroTurnEnteredAt/_heroTurnWatchdogLogged
/// supprimees, remplacees par _blockedSince/_watchdogLogged qui servent au
/// watchdog unifie.
///
/// JOUR 10 : migration FSM-first — utilise BeginUILock/EndUILock/EndCalc
/// (helpers du Jour 9) au lieu des setters booleens directs.
/// Le log de diagnostic fin de tour inclut CurrentPhase.
/// </summary>
public class TurnManager : MonoBehaviour
{
    public static TurnManager Instance { get; private set; }

    // =========================================================================
    // Inspector
    // =========================================================================

    [Header("=== Debug ===")]
    [SerializeField] private bool _logTurns = true;

    [Header("=== Pauses de transition (secondes) ===")]
    [Tooltip("Pause entre alerte et attaque")]
    [SerializeField, Range(0f, 2f)] private float _transitionAlertToAttack = 0.5f;

    [Tooltip("Pause entre la fin du flinch hero et l ennemi suivant")]
    [SerializeField, Range(0f, 2f)] private float _transitionHurtToNext = 0.5f;

    [Tooltip("Pause entre la fin du tour ennemi et le debut du tour hero")]
    [SerializeField, Range(0f, 1f)] private float _transitionToHeroTurn = 0.2f;

    [Header("=== Watchdog ===")]
    [Tooltip("Delai en Inactive avant force unlock si bloque (secondes)")]
    [SerializeField, Range(1f, 5f)] private float _watchdogInactiveDelay = 2f;

    [Tooltip("Delai en HeroTurn avant force unlock si bloque (secondes)")]
    [SerializeField, Range(0.5f, 3f)] private float _watchdogHeroTurnDelay = 1.5f;

    // =========================================================================
    // Etat
    // =========================================================================

    private TurnState _state = TurnState.Inactive;
    public TurnState State => _state;
    public bool IsActive => _state != TurnState.Inactive;
    public bool IsHeroTurn => _state == TurnState.HeroTurn;

    public EnemyInstance CurrentAttacker { get; private set; }

    private readonly List<EnemyInstance> _activeEnemies = new List<EnemyInstance>();
    private readonly HashSet<EnemyInstance> _previousActive = new HashSet<EnemyInstance>();
    private readonly List<EnemyInstance> _pendingNewEnemies = new List<EnemyInstance>();
    private readonly HashSet<EnemyInstance> _introducedEnemies = new HashSet<EnemyInstance>();

    private bool _ignoreNextHeroAction = false;
    private Coroutine _pendingActivation;
    private bool _enemyTurnRunning;

    // Watchdog unifie (Inactive + HeroTurn)
    private float _blockedSince = -1f;
    private bool _watchdogLogged = false;

    // =========================================================================
    // Lifecycle
    // =========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Debug.Log("[TurnManager] Awake — Instance OK");
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnCellsRevealed>(OnCellsRevealed);
        EventBus.Subscribe<OnHeroActionComplete>(OnHeroActionDone);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnCellsRevealed>(OnCellsRevealed);
        EventBus.Unsubscribe<OnHeroActionComplete>(OnHeroActionDone);
    }

    // =========================================================================
    // Watchdog Global — detecte les blocages en Inactive ET HeroTurn
    // =========================================================================

    private void Update()
    {
        // Pas de watchdog pendant EnemyTurn (le hero DOIT etre locked)
        if (_state == TurnState.EnemyTurn) return;

        var hs = HeroState.Instance;
        if (hs == null) return;

        // Hero peut agir → pas de blocage
        if (hs.CanMove) { ResetWatchdog(); return; }

        // Action legitime en cours → pas de blocage
        if (hs.IsMoving || hs.IsJumping) { ResetWatchdog(); return; }

        var hc = HeroCombat.Instance;
        if (hc != null && hc.IsAnimating) { ResetWatchdog(); return; }
        if (hc != null && hc.IsFlinching) { ResetWatchdog(); return; }

        // Blocage detecte : demarrer le compte a rebours
        if (_blockedSince < 0f) _blockedSince = Time.time;

        float threshold = (_state == TurnState.HeroTurn)
            ? _watchdogHeroTurnDelay
            : _watchdogInactiveDelay;

        if (Time.time - _blockedSince < threshold) return;

        if (!_watchdogLogged)
        {
            Debug.LogWarning($"<color=#FFAA00>[TurnManager]</color> WATCHDOG — " +
                             $"hero bloque depuis {Time.time - _blockedSince:F1}s " +
                             $"(state={_state} Phase={hs.CurrentPhase} " +
                             $"IsUILocked={hs.IsUILocked} IsCalculating={hs.IsCalculating})");
            _watchdogLogged = true;
        }

        ForceUnlockHero($"watchdog {_state}");
        ResetWatchdog();
    }

    private void ResetWatchdog()
    {
        _blockedSince = -1f;
        _watchdogLogged = false;
    }

    // =========================================================================
    // Activation — sur revelation d ennemi
    // =========================================================================

    private void OnCellsRevealed(OnCellsRevealed evt)
    {
        var gmc = GameModeConfig.Instance;
        if (gmc != null && !gmc.CombatEnabled) return;

        if (_state != TurnState.Inactive)
        {
            StartCoroutine(QueueNewEnemiesFromReveal());
            return;
        }

        if (_pendingActivation == null)
            _pendingActivation = StartCoroutine(DelayedActivationCheck());
    }

    private IEnumerator QueueNewEnemiesFromReveal()
    {
        yield return null;

        var hero = HeroController.Instance;
        if (hero == null) yield break;
        var heroPos = hero.GridPosition;

        foreach (var ei in EnemyInstance.All)
        {
            if (ei == null || ei.IsDead) continue;
            if (_activeEnemies.Contains(ei)) continue;
            if (_pendingNewEnemies.Contains(ei)) continue;
            if (_introducedEnemies.Contains(ei)) continue;

            var data = EnemyDatabase.Instance?.Get(ei.EnemyType);
            if (data == null) continue;
            if (data.behavior != EnemyBehaviorType.Melee) continue;

            int dist = Mathf.Abs(heroPos.x - ei.GridX)
                     + Mathf.Abs(heroPos.y - ei.GridY);
            if (dist > data.perceptionRadius) continue;

            Log("Nouvel ennemi en combat : " + (data.displayName ?? ei.EnemyType.ToString()));
            _pendingNewEnemies.Add(ei);
        }
    }

    private IEnumerator DelayedActivationCheck()
    {
        yield return null;
        _pendingActivation = null;

        if (_state != TurnState.Inactive) yield break;

        if (!HasActiveEnemy())
        {
            Log("Check activation → aucun ennemi melee actif");
            yield break;
        }

        yield return StartCoroutine(WaitHeroFullyIdle());

        _ignoreNextHeroAction = true;
        Log("Flag _ignoreNextHeroAction ACTIVE (revelation en cours)");

        bool heroAdjacent = IsHeroAdjacentToActiveEnemy();
        if (heroAdjacent) Log("Heros ADJACENT → ennemis jouent d abord");

        yield return StartCoroutine(
            ActivateCombatWithIndicators(enemyTurnFirst: heroAdjacent));
    }

    private IEnumerator ActivateCombatWithIndicators(bool enemyTurnFirst = false)
    {
        Log("Combat active — " + _activeEnemies.Count + " ennemi(s)"
            + (enemyTurnFirst ? " (ennemis d abord)" : ""));

        _previousActive.Clear();
        foreach (var ei in _activeEnemies)
            if (ei != null && !ei.IsDead) _previousActive.Add(ei);

        if (enemyTurnFirst)
        {
            yield return StartCoroutine(EnemyTurnSequence(playAlertFirst: true));
        }
        else
        {
            for (int i = 0; i < _activeEnemies.Count; i++)
            {
                var ei = _activeEnemies[i];
                if (ei == null || ei.IsDead) continue;
                var beh = ei.GetComponent<EnemyBehaviour>();
                if (beh == null) continue;

                _introducedEnemies.Add(ei);

                var co = new List<Coroutine>();
                co.Add(StartCoroutine(beh.ShowAlertIndicatorAndWait()));
                co.Add(StartCoroutine(beh.PlayAlertPose()));
                foreach (var c in co) yield return c;

                if (i < _activeEnemies.Count - 1)
                    yield return WaitCache.Get(0.2f);
            }

            SetState(TurnState.HeroTurn);
        }
    }

    // =========================================================================
    // Cycle de tours
    // =========================================================================

    private void OnHeroActionDone(OnHeroActionComplete evt)
    {
        if (_ignoreNextHeroAction)
        {
            _ignoreNextHeroAction = false;
            Log("OnHeroActionComplete IGNORE (deplacement de revelation)");
            return;
        }

        if (_state == TurnState.Inactive)
        {
            var gmc = GameModeConfig.Instance;
            if (gmc != null && !gmc.CombatEnabled) return;
            if (_pendingActivation != null) return;

            if (HasActiveEnemy())
            {
                Log("Reactivation combat — " + _activeEnemies.Count + " ennemi(s)");
                StartCoroutine(ActivateCombatWithIndicators(enemyTurnFirst: true));
            }
            return;
        }

        if (_state != TurnState.HeroTurn) return;
        if (_enemyTurnRunning) return;

        var hs = HeroState.Instance;
        if (hs != null) hs.BeginUILock("TurnManager.OnHeroActionDone");

        Log("Action hero terminee → lancement tour ennemi");
        StartCoroutine(EnemyTurnSequence(playAlertFirst: false));
    }

    private IEnumerator EnemyTurnSequence(bool playAlertFirst = false)
    {
        SetState(TurnState.EnemyTurn);

        var safety = StartCoroutine(EnsureUnlockAfterDelay(15f));
        var hs = HeroState.Instance;
        if (hs != null) hs.BeginUILock("TurnManager.EnemyTurnSequence start");

        _enemyTurnRunning = true;

        yield return StartCoroutine(WaitHeroFullyIdle());

        RefreshActiveEnemies();

        // Integrer les nouveaux ennemis reveles mid-combat
        if (_pendingNewEnemies.Count > 0)
        {
            Log("Introduction de " + _pendingNewEnemies.Count + " nouvel(aux) ennemi(s)");

            for (int p = 0; p < _pendingNewEnemies.Count; p++)
            {
                var newEi = _pendingNewEnemies[p];
                if (newEi == null || newEi.IsDead) continue;

                var newBeh = newEi.GetComponent<EnemyBehaviour>();
                if (newBeh != null)
                {
                    var co = new List<Coroutine>();
                    co.Add(StartCoroutine(newBeh.ShowAlertIndicatorAndWait()));
                    co.Add(StartCoroutine(newBeh.PlayAlertPose()));
                    foreach (var c in co) yield return c;

                    if (_transitionAlertToAttack > 0f)
                        yield return new WaitForSeconds(_transitionAlertToAttack);
                }

                if (!_activeEnemies.Contains(newEi))
                    _activeEnemies.Add(newEi);
                _introducedEnemies.Add(newEi);
            }
            _pendingNewEnemies.Clear();
        }

        // Perte de vue avant action
        var lostSight = new List<EnemyInstance>();
        foreach (var prev in _previousActive)
        {
            if (prev == null || prev.IsDead) continue;
            if (!_activeEnemies.Contains(prev))
                lostSight.Add(prev);
        }
        if (lostSight.Count > 0)
        {
            Log("Perte de vue — " + lostSight.Count + " ennemi(s)");
            var co = new List<Coroutine>();
            foreach (var ei in lostSight)
            {
                var beh = ei.GetComponent<EnemyBehaviour>();
                if (beh != null) co.Add(StartCoroutine(beh.ShowLostIndicatorAndWait()));
                _introducedEnemies.Remove(ei);
            }
            foreach (var c in co) yield return c;
        }

        _previousActive.Clear();
        foreach (var ei in _activeEnemies)
            if (ei != null && !ei.IsDead) _previousActive.Add(ei);

        Log("Tour ennemi — " + _activeEnemies.Count + " ennemi(s)"
            + (playAlertFirst ? " (avec alerte)" : ""));

        if (_activeEnemies.Count == 0)
        {
            Log("Plus d ennemi → mode libre");
            if (safety != null) StopCoroutine(safety);
            CleanupEnemyTurn(hs);
            SetState(TurnState.Inactive);
            yield break;
        }

        // Chaque ennemi agit en sequence stricte
        for (int i = 0; i < _activeEnemies.Count; i++)
        {
            var ei = _activeEnemies[i];
            if (ei == null || ei.IsDead) continue;

            var beh = ei.GetComponent<EnemyBehaviour>();
            if (beh == null) continue;

            // Alerte si pas encore introduit
            if (playAlertFirst && !_introducedEnemies.Contains(ei))
            {
                _introducedEnemies.Add(ei);

                var alertCo = new List<Coroutine>();
                alertCo.Add(StartCoroutine(beh.ShowAlertIndicatorAndWait()));
                alertCo.Add(StartCoroutine(beh.PlayAlertPose()));
                foreach (var c in alertCo) yield return c;

                if (_transitionAlertToAttack > 0f)
                    yield return new WaitForSeconds(_transitionAlertToAttack);
            }

            CurrentAttacker = ei;

            yield return StartCoroutine(beh.TakeTurn());

            yield return StartCoroutine(WaitEnemyActionDone(ei));
            yield return StartCoroutine(WaitHeroFlinchDone());

            CurrentAttacker = null;

            if (i < _activeEnemies.Count - 1)
            {
                if (_transitionHurtToNext > 0f)
                    yield return new WaitForSeconds(_transitionHurtToNext);
            }
        }

        if (safety != null) StopCoroutine(safety);

        yield return StartCoroutine(WaitHeroFlinchDone());

        // JOUR 10 : log diagnostic enrichi avec Phase=
        var hc = HeroCombat.Instance;
        Log("Fin tour ennemi"
            + " Phase=" + (hs != null ? hs.CurrentPhase.ToString() : "?")
            + " UILocked=" + (hs != null ? hs.IsUILocked.ToString() : "?")
            + " Moving=" + (hs != null ? hs.IsMoving.ToString() : "?")
            + " Flinch=" + (hc != null ? hc.IsFlinching.ToString() : "?")
            + " CanMove=" + (hs != null ? hs.CanMove.ToString() : "?"));

        EventBus.Publish(new OnEnemyTurnComplete());
        EnemyRoamingSystem.Instance?.NotifyCombatTurnComplete();

        // Refresh post-tour
        RefreshActiveEnemies();

        var lostAfterTurn = new List<EnemyInstance>();
        foreach (var prev in _previousActive)
        {
            if (prev == null || prev.IsDead) continue;
            if (!_activeEnemies.Contains(prev))
                lostAfterTurn.Add(prev);
        }
        if (lostAfterTurn.Count > 0)
        {
            var postCo = new List<Coroutine>();
            foreach (var ei in lostAfterTurn)
            {
                var beh = ei.GetComponent<EnemyBehaviour>();
                if (beh != null) postCo.Add(StartCoroutine(beh.ShowLostIndicatorAndWait()));
                _introducedEnemies.Remove(ei);
            }
            foreach (var c in postCo) yield return c;
        }

        _previousActive.Clear();
        foreach (var ei in _activeEnemies)
            if (ei != null && !ei.IsDead) _previousActive.Add(ei);

        if (_transitionToHeroTurn > 0f)
            yield return new WaitForSeconds(_transitionToHeroTurn);

        CleanupEnemyTurn(hs);

        if (_activeEnemies.Count == 0)
        {
            Log("Plus d ennemi → mode libre");
            _introducedEnemies.Clear();
            SetState(TurnState.Inactive);
        }
        else
        {
            SetState(TurnState.HeroTurn);
        }
    }

    // =========================================================================
    // Attentes strictes
    // =========================================================================

    private IEnumerator WaitHeroFullyIdle()
    {
        var hs = HeroState.Instance;
        var hc = HeroCombat.Instance;
        float timeout = 5f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            bool busy = false;

            if (hs != null && hs.IsMoving) busy = true;
            if (hc != null && hc.IsFlinching) busy = true;
            if (hc != null && hc.IsAnimating) busy = true;

            if (!busy)
            {
                foreach (var ei in EnemyInstance.All)
                {
                    if (ei != null && !ei.IsDead && ei.IsAnimating)
                    { busy = true; break; }
                }
            }

            if (!busy) yield break;

            elapsed += Time.deltaTime;
            yield return null;
        }

        Log("WARN: WaitHeroFullyIdle timeout (5s)");
    }

    private IEnumerator WaitEnemyActionDone(EnemyInstance ei)
    {
        if (ei == null || ei.IsDead) yield break;

        yield return null;

        float timeout = 3f;
        float elapsed = 0f;

        while (ei != null && !ei.IsDead && elapsed < timeout)
        {
            bool atOriginalPos = Vector3.Distance(ei.transform.position, ei.OriginalPos) < 0.01f;
            if (atOriginalPos) yield break;

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (elapsed >= timeout)
            Log("WARN: WaitEnemyActionDone timeout (3s) pour " + ei.EnemyType);
    }

    private IEnumerator WaitHeroFlinchDone()
    {
        var hc = HeroCombat.Instance;
        if (hc == null || !hc.IsFlinching) yield break;

        Log("Attente fin flinch hero...");
        float timeout = 2f;
        float elapsed = 0f;
        while (hc != null && hc.IsFlinching && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (elapsed >= timeout)
            Log("WARN: flinch timeout (2s)");
    }

    // =========================================================================
    // Helpers : unlock + cleanup
    // =========================================================================

    /// <summary>
    /// JOUR 10 : utilise les helpers EndUILock et EndCalc (Jour 9) au lieu
    /// des setters booleens directs, pour tracer la raison dans les logs FSM.
    /// </summary>
    private void ForceUnlockHero(string reason)
    {
        var hs = HeroState.Instance;
        if (hs == null) return;

        bool wasLocked = hs.IsUILocked;
        bool wasCalc = hs.IsCalculating;

        hs.EndUILock("TurnManager.ForceUnlockHero " + reason);
        hs.EndCalc("TurnManager.ForceUnlockHero " + reason);

        if (wasLocked || wasCalc)
        {
            Debug.Log($"<color=#00FF88>[TurnManager]</color> ForceUnlockHero [{reason}] " +
                      $"IsUILocked={wasLocked}→false IsCalculating={wasCalc}→false");
        }
    }

    /// <summary>
    /// JOUR 10 : utilise EndUILock (Jour 9) au lieu de IsUILocked = false.
    /// </summary>
    private void CleanupEnemyTurn(HeroState hs)
    {
        CurrentAttacker = null;
        _enemyTurnRunning = false;

        if (hs != null) hs.EndUILock("TurnManager.CleanupEnemyTurn");

        var hc = HeroCombat.Instance;
        if (hc != null && hc.IsFlinching)
        {
            Log("WARN: HeroCombat en flinch — ForceReset");
            hc.ForceReset();
        }
    }

    // =========================================================================
    // Gestion des ennemis actifs
    // =========================================================================

    private void RefreshActiveEnemies()
    {
        _activeEnemies.Clear();

        var hero = HeroController.Instance;
        if (hero == null) return;
        var heroPos = hero.GridPosition;

        foreach (var ei in EnemyInstance.All)
        {
            if (ei == null || ei.IsDead) continue;

            var data = EnemyDatabase.Instance?.Get(ei.EnemyType);
            if (data == null) continue;
            if (data.behavior != EnemyBehaviorType.Melee) continue;

            int dist = Mathf.Abs(heroPos.x - ei.GridX)
                     + Mathf.Abs(heroPos.y - ei.GridY);
            if (dist > data.perceptionRadius) continue;

            _activeEnemies.Add(ei);
        }
    }

    private bool HasActiveEnemy()
    {
        RefreshActiveEnemies();
        return _activeEnemies.Count > 0;
    }

    private bool IsHeroAdjacentToActiveEnemy()
    {
        var hero = HeroController.Instance;
        if (hero == null) return false;
        var heroPos = hero.GridPosition;

        foreach (var ei in _activeEnemies)
        {
            if (ei == null || ei.IsDead) continue;
            int cheby = Mathf.Max(
                Mathf.Abs(heroPos.x - ei.GridX),
                Mathf.Abs(heroPos.y - ei.GridY));
            if (cheby <= 1) return true;
        }
        return false;
    }

    // =========================================================================
    // Changement d etat
    // =========================================================================

    private void SetState(TurnState newState)
    {
        if (newState == _state) return;
        Log($"Etat: {_state} → {newState}");
        _state = newState;

        if (newState == TurnState.HeroTurn)
        {
            ForceUnlockHero("entree HeroTurn");
            ResetWatchdog();
        }
        else if (newState == TurnState.Inactive)
        {
            ForceUnlockHero("entree Inactive");
            ResetWatchdog();
        }
    }

    // =========================================================================
    // Filet de securite
    // =========================================================================

    private IEnumerator EnsureUnlockAfterDelay(float maxWait)
    {
        yield return new WaitForSeconds(maxWait);
        if (_enemyTurnRunning)
        {
            Debug.LogWarning("[TurnManager] Filet securite : force reset apres " + maxWait + "s");
            CleanupEnemyTurn(HeroState.Instance);
            _pendingNewEnemies.Clear();
            SetState(TurnState.HeroTurn);
        }
    }

    private void Log(string msg)
    {
        if (_logTurns) Debug.Log("[TurnManager] " + msg);
    }
}
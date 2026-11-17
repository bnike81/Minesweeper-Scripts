using UnityEngine;

/// <summary>
/// HeroState v13 — Etat du heros, routage vers HeroStateMachine (FSM).
///
/// INTEGRATIONS :
///   Jour 1-2 : FSM initiale + setters symetriques
///   Jour 3   : SetLockedPhase + TryTransition
///   Jour 4   : UILocked etendu, Flinching integre
///   Jour 5   : Phase composite Revealing
///   Jour 6   : Jumping active (dans HeroJump)
///   Jour 7   : Trapped active (dans TrapInstance)
///   Jour 8   : Dead active (OnPlayerDied event)
///   Jour 9   : API FSM-first (helpers BeginX/EndX)
/// </summary>
public class HeroState : MonoBehaviour
{
    public static HeroState Instance { get; private set; }

    public HeroStateMachine SM { get; private set; }

    // =========================================================================
    // v13 Jour 5 : detection phase composite
    // =========================================================================

    /// <summary>
    /// Vrai quand on est dans une phase "composite" qui englobe plusieurs
    /// actions internes (ex: Revealing). Les setters legacy ne transitionnent
    /// pas pendant ces phases — seul l appelant du flow controle les
    /// transitions explicitement.
    /// </summary>
    private bool InCompositePhase
        => SM != null && SM.CurrentPhase == HeroPhase.Revealing;

    // =========================================================================
    // Flags legacy
    // =========================================================================

    private bool _hasAppeared;
    public bool HasAppeared
    {
        get => _hasAppeared;
        set
        {
            _hasAppeared = value;
            if (SM == null) return;
            if (value && SM.CurrentPhase == HeroPhase.NotAppeared)
                SM.TryTransitionTo(HeroPhase.Idle, "HasAppeared=true");
        }
    }

    private bool _isMoving;
    public bool IsMoving
    {
        get => _isMoving;
        set
        {
            _isMoving = value;
            if (SM == null || InCompositePhase) return;
            if (value) SM.TryTransitionTo(HeroPhase.Moving, "setter IsMoving=true");
            else if (SM.CurrentPhase == HeroPhase.Moving)
                SM.TryTransitionTo(HeroPhase.Idle, "setter IsMoving=false");
        }
    }

    private bool _isCalculating;
    public bool IsCalculating
    {
        get => _isCalculating;
        set
        {
            _isCalculating = value;
            if (SM == null || InCompositePhase) return;
            if (value) SM.TryTransitionTo(HeroPhase.Calculating, "setter IsCalc=true");
            else if (SM.CurrentPhase == HeroPhase.Calculating)
                SM.TryTransitionTo(HeroPhase.Idle, "setter IsCalc=false");
        }
    }

    private bool _isUILocked;
    public bool IsUILocked
    {
        get => _isUILocked;
        set
        {
            _isUILocked = value;
            if (SM == null || InCompositePhase) return;
            SetLockedPhase(value, HeroPhase.UILocked, "setter IsUILocked=" + value);
        }
    }

    public bool IsJumping => HeroJump.Instance != null && HeroJump.Instance.IsJumping;

    public bool CollectOnArrival { get; set; } = false;

    public bool IsBlocked => IsUILocked || IsJumping;

    public bool CanMove => HasAppeared && !IsBlocked && !IsMoving && !IsCalculating;

    // =========================================================================
    // v13 Jour 3 : Helpers phases contextuelles
    // =========================================================================

    public bool TryTransition(HeroPhase phase, string reason = "")
    {
        if (SM == null) return false;
        return SM.TryTransitionTo(phase, reason);
    }

    // =========================================================================
    // v13 Jour 9 : API FSM-first (helpers explicites)
    //
    // Pour les nouveaux call-sites : utiliser ces helpers au lieu des setters
    // booleens. Plus lisible, et le reason trace apparait dans les logs.
    //
    // Les setters legacy (IsMoving, IsCalculating, IsUILocked) restent valides
    // et equivalent en comportement — c est juste du sucre syntaxique.
    // =========================================================================

    public HeroPhase CurrentPhase => SM != null ? SM.CurrentPhase : HeroPhase.NotAppeared;

    public bool IsInPhase(HeroPhase phase) => SM != null && SM.CurrentPhase == phase;

    // --- Move ---

    public bool BeginMove(string reason = "")
    {
        _isMoving = true;
        if (SM == null || InCompositePhase) return false;
        return SM.TryTransitionTo(HeroPhase.Moving,
            string.IsNullOrEmpty(reason) ? "BeginMove" : "BeginMove " + reason);
    }

    public bool EndMove(string reason = "")
    {
        _isMoving = false;
        if (SM == null || InCompositePhase) return false;
        if (SM.CurrentPhase != HeroPhase.Moving) return false;
        return SM.TryTransitionTo(HeroPhase.Idle,
            string.IsNullOrEmpty(reason) ? "EndMove" : "EndMove " + reason);
    }

    // --- Calculating (pathfinding) ---

    public bool BeginCalc(string reason = "")
    {
        _isCalculating = true;
        if (SM == null || InCompositePhase) return false;
        return SM.TryTransitionTo(HeroPhase.Calculating,
            string.IsNullOrEmpty(reason) ? "BeginCalc" : "BeginCalc " + reason);
    }

    public bool EndCalc(string reason = "")
    {
        _isCalculating = false;
        if (SM == null || InCompositePhase) return false;
        if (SM.CurrentPhase != HeroPhase.Calculating) return false;
        return SM.TryTransitionTo(HeroPhase.Idle,
            string.IsNullOrEmpty(reason) ? "EndCalc" : "EndCalc " + reason);
    }

    // --- UILocked ---

    public bool BeginUILock(string reason = "")
    {
        return SetLockedPhase(true, HeroPhase.UILocked,
            string.IsNullOrEmpty(reason) ? "BeginUILock" : "BeginUILock " + reason);
    }

    public bool EndUILock(string reason = "")
    {
        return SetLockedPhase(false, HeroPhase.UILocked,
            string.IsNullOrEmpty(reason) ? "EndUILock" : "EndUILock " + reason);
    }

    // =========================================================================
    // SetLockedPhase — utilise par HeroCombat (Attacking), TrapInstance (Trapped)
    // et les helpers BeginUILock/EndUILock (UILocked).
    // Retourne bool (true si transition OK).
    // =========================================================================

    public bool SetLockedPhase(bool locked, HeroPhase whenLocked, string reason = "")
    {
        _isUILocked = locked;

        if (SM == null) return false;

        if (locked)
        {
            return SM.TryTransitionTo(whenLocked, reason);
        }
        else
        {
            if (SM.CurrentPhase == whenLocked)
                return SM.TryTransitionTo(HeroPhase.Idle, reason);
            return false;
        }
    }

    // =========================================================================
    // Lifecycle
    // =========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        SM = new HeroStateMachine();
        SM.OnPhaseChanged += OnPhaseChanged;
        Debug.Log("<color=#66FFCC>[HSM]</color> HeroStateMachine initialisee");
    }

    private void OnPhaseChanged(HeroPhase prev, HeroPhase next)
    {
        if (next == HeroPhase.Idle)
        {
            _isMoving = false;
            _isCalculating = false;
            _isUILocked = false;
        }
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
        EventBus.Subscribe<OnPlayerDied>(HandlePlayerDied);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
        EventBus.Unsubscribe<OnPlayerDied>(HandlePlayerDied);
    }

    private void OnRunStarted(OnRunStarted e) => ResetAll();

    /// <summary>
    /// v13 Jour 8 : le hero est mort — transition vers phase Dead (terminale).
    /// Les flags legacy sont forces a false, la FSM bloque toute transition
    /// ulterieure. Seul OnRunStarted → ResetAll remet le hero a Idle.
    /// </summary>
    private void HandlePlayerDied(OnPlayerDied e)
    {
        _isMoving = false;
        _isCalculating = false;
        _isUILocked = false;

        if (SM != null)
            SM.TryTransitionTo(HeroPhase.Dead, "OnPlayerDied event");

        Debug.Log("<color=#FF3366>[HeroState]</color> Hero mort — phase Dead active");
    }

    public void ResetAll()
    {
        _isMoving = false;
        _isCalculating = false;
        _isUILocked = false;
        CollectOnArrival = false;

        SM?.Reset();

        HeroCombat.Instance?.ForceReset();

        Debug.Log("<color=#AAFFFF>[HeroState]</color> Reset complet");
    }

    [ContextMenu("Debug State")]
    public void DebugState()
    {
        Debug.Log("[HeroState] HasAppeared=" + HasAppeared
            + " IsMoving=" + IsMoving
            + " IsCalc=" + IsCalculating
            + " IsUILocked=" + IsUILocked
            + " IsJumping=" + IsJumping
            + " CanMove=" + CanMove
            + " | SM.Phase=" + (SM != null ? SM.CurrentPhase.ToString() : "null"));
    }

    private float _blockTimer = 0f;

    private void Update()
    {
        if (!HasAppeared) return;

        SM?.TickWatchdog();

        if (IsBlocked || IsMoving || IsCalculating)
        {
            var tm = TurnManager.Instance;
            if (tm != null && tm.IsActive && !tm.IsHeroTurn)
            {
                _blockTimer = 0f;
                return;
            }

            _blockTimer += Time.deltaTime;
            if (_blockTimer > 3f)
            {
                Debug.LogWarning("<color=#FFAA00>[HeroState]</color> Blocage detecte - reset auto !");
                ResetAll();
                _blockTimer = 0f;
            }
        }
        else
        {
            _blockTimer = 0f;
        }
    }
}
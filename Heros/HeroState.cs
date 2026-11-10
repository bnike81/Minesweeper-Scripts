using UnityEngine;

/// <summary>
/// HeroState v8 — Source unique de verite pour tous les etats du heros.
///
/// FIX v8 :
///   1. Le timer de securite (3s) IGNORE le blocage pendant le tour ennemi.
///      Quand TurnManager est actif et ce n est pas HeroTurn, le blocage
///      via IsUILocked est NORMAL (les ennemis jouent). Le timer ne doit
///      pas couper ce verrouillage.
///   2. ResetAll() appelle HeroCombat.ForceReset() pour nettoyer les flags
///      IsAnimating, IsInCombat, IsFlinching si le reset de securite se
///      declenche. Sans ca, le hero peut rester bloque en etat de combat.
/// </summary>
public class HeroState : MonoBehaviour
{
    public static HeroState Instance { get; private set; }

    // -------------------------------------------------------------------------
    // Etats
    // -------------------------------------------------------------------------

    /// <summary>Le heros a fait son premier clic et est visible</summary>
    public bool HasAppeared { get; set; } = false;

    /// <summary>En cours de deplacement A*</summary>
    public bool IsMoving { get; set; } = false;

    /// <summary>En cours de calcul A* (coroutine)</summary>
    public bool IsCalculating { get; set; } = false;

    /// <summary>Bloque par une UI (panel item, dialogue...)</summary>
    public bool IsUILocked { get; set; } = false;

    /// <summary>En cours de saut</summary>
    public bool IsJumping => HeroJump.Instance != null && HeroJump.Instance.IsJumping;

    /// <summary>Clic intentionnel sur un item pour le collecter</summary>
    public bool CollectOnArrival { get; set; } = false;

    /// <summary>Bloque si n importe quelle action exclusive est active</summary>
    public bool IsBlocked => IsUILocked || IsJumping;

    /// <summary>Peut recevoir un clic de deplacement</summary>
    public bool CanMove => HasAppeared && !IsBlocked && !IsMoving && !IsCalculating;

    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
    }

    private void OnRunStarted(OnRunStarted e) => ResetAll();

    // -------------------------------------------------------------------------
    // Reset d urgence
    // -------------------------------------------------------------------------

    /// <summary>
    /// FIX v8 : reset complet incluant HeroCombat.
    /// Si HeroCombat a des flags actifs (IsAnimating, IsInCombat, IsFlinching),
    /// ForceReset() les nettoie aussi. Sans ca, le hero peut rester visuellement
    /// bloque en posture de combat apres un reset de securite.
    /// </summary>
    public void ResetAll()
    {
        IsMoving = false;
        IsCalculating = false;
        IsUILocked = false;
        CollectOnArrival = false;

        // FIX v8 : nettoyer aussi l etat de combat du hero
        HeroCombat.Instance?.ForceReset();

        Debug.Log("<color=#AAFFFF>[HeroState]</color> Reset complet");
    }

    // -------------------------------------------------------------------------
    // Debug
    // -------------------------------------------------------------------------

    [ContextMenu("Debug State")]
    public void DebugState()
    {
        Debug.Log("[HeroState] HasAppeared=" + HasAppeared
            + " IsMoving=" + IsMoving
            + " IsCalc=" + IsCalculating
            + " IsUILocked=" + IsUILocked
            + " IsJumping=" + IsJumping
            + " CanMove=" + CanMove);
    }

    // Securite : si bloque plus de 3s sans raison, reset auto
    private float _blockTimer = 0f;

    /// <summary>
    /// FIX v8 : le timer de securite ne compte PAS pendant le tour ennemi.
    /// Quand TurnManager gere le combat et que c est le tour des ennemis,
    /// IsUILocked=true est VOULU — le hero doit attendre. Le timer ne doit
    /// pas couper ce verrouillage intentionnel.
    ///
    /// Avant v8, avec 2+ ennemis le tour pouvait depasser 3s, declenchant
    /// ResetAll() qui debloquait le hero PENDANT les attaques ennemies.
    /// </summary>
    private void Update()
    {
        if (!HasAppeared) return;

        if (IsBlocked || IsMoving || IsCalculating)
        {
            // ── FIX v8 : ne PAS compter le timer pendant le tour ennemi ──
            // Le blocage est NORMAL et VOULU quand les ennemis jouent.
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
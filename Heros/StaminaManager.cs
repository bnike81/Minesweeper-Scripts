using UnityEngine;

/// <summary>
/// StaminaManager — Gestion de l endurance du heros.
///
/// REGLES :
///   - Premier deplacement du tour = gratuit (pas de cout).
///   - Chaque case supplementaire = 1 pt.
///   - Attaque = 1 pt.
///   - Revelation = gratuit.
///   - Regeneration : +1 pt par tour ennemi en combat,
///     graduel (timer) en mode libre.
///   - Pas de reset au debut du combat.
///   - Si endurance insuffisante, l action s arrete.
///
/// FUTUR : bonus equipement (bottes = +free steps, amulette = +max),
///         items de restauration, amelioration regen.
///
/// SETUP : GO propre dans la scene Hero.
/// </summary>
public class StaminaManager : MonoBehaviour
{
    public static StaminaManager Instance { get; private set; }

    // =========================================================================
    // Inspector
    // =========================================================================

    [Header("=== Endurance ===")]
    [SerializeField, Range(1, 30)] private int _maxStamina = 10;

    [Header("=== Regeneration mode libre ===")]
    [Tooltip("Secondes entre chaque point regenere en mode libre")]
    [SerializeField, Range(5f, 120f)] private float _freeRegenInterval = 30f;

    [Header("=== Equipement bonus ===")]
    [Tooltip("Cases de deplacement gratuit de base (avant bonus equipement)")]
    [SerializeField, Range(1, 5)] private int _baseFreeSteps = 1;

    // =========================================================================
    // Etat
    // =========================================================================

    private int _current;
    private int _stepsThisTurn;
    private float _freeRegenTimer;

    // =========================================================================
    // Proprietes publiques
    // =========================================================================

    public int Current => _current;
    public int Max => _maxStamina;
    public bool IsExhausted => _current <= 0;

    /// <summary>Nombre de cases gratuites (base + bonus equipement).</summary>
    public int FreeSteps
    {
        get
        {
            int bonus = 0;
            // FUTUR : lire bonus depuis Equipment (bottes, etc.)
            // bonus = Equipment.Instance?.StaminaFreeStepBonus ?? 0;
            return _baseFreeSteps + bonus;
        }
    }

    // =========================================================================
    // Init
    // =========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _current = _maxStamina;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnEnemyTurnComplete>(OnEnemyTurnDone);
        EventBus.Subscribe<OnTurnStateChanged>(OnTurnChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnEnemyTurnComplete>(OnEnemyTurnDone);
        EventBus.Unsubscribe<OnTurnStateChanged>(OnTurnChanged);
    }

    // =========================================================================
    // Cycle de tour
    // =========================================================================

    /// <summary>Appele au debut du tour heros pour reset le compteur de pas.</summary>
    public void BeginHeroTurn()
    {
        _stepsThisTurn = 0;
    }

    /// <summary>Regenere 1 pt apres le tour ennemi (en combat).</summary>
    private void OnEnemyTurnDone(OnEnemyTurnComplete e)
    {
        Regen(1);
    }

    private void OnTurnChanged(OnTurnStateChanged e)
    {
        if (e.NewState == TurnState.HeroTurn)
            BeginHeroTurn();
    }

    // =========================================================================
    // Consommation
    // =========================================================================

    /// <summary>
    /// Calcule le cout en endurance pour un deplacement de N cases.
    /// Les FreeSteps premieres cases sont gratuites.
    /// </summary>
    public int MoveCost(int cellCount)
    {
        int alreadyFree = Mathf.Min(_stepsThisTurn, FreeSteps);
        int freeRemaining = FreeSteps - alreadyFree;
        int paidCells = Mathf.Max(0, cellCount - freeRemaining);
        return paidCells; // 1 pt par case payante
    }

    /// <summary>
    /// Combien de cases le heros peut parcourir avec son endurance actuelle.
    /// </summary>
    public int MaxAffordableSteps()
    {
        int freeRemaining = Mathf.Max(0, FreeSteps - _stepsThisTurn);
        return freeRemaining + _current; // gratuit + payant
    }

    /// <summary>
    /// Consomme l endurance pour un deplacement de N cases.
    /// Retourne le nombre de cases reellement parcourues (peut etre < cellCount).
    /// </summary>
    public int ConsumeMove(int cellCount)
    {
        int affordable = MaxAffordableSteps();
        int actual = Mathf.Min(cellCount, affordable);

        int freeRemaining = Mathf.Max(0, FreeSteps - _stepsThisTurn);
        int freePart = Mathf.Min(actual, freeRemaining);
        int paidPart = actual - freePart;

        _stepsThisTurn += actual;
        Spend(paidPart);

        return actual;
    }

    /// <summary>Consomme 1 pt pour une attaque. Retourne false si pas assez.</summary>
    public bool ConsumeAttack()
    {
        if (_current < 1) return false;
        Spend(1);
        return true;
    }

    /// <summary>Verifie si le heros peut attaquer (1 pt minimum).</summary>
    public bool CanAttack() => _current >= 1;

    // =========================================================================
    // Interne
    // =========================================================================

    private void Spend(int amount)
    {
        if (amount <= 0) return;
        _current = Mathf.Max(0, _current - amount);
        Publish();
    }

    private void Regen(int amount)
    {
        if (amount <= 0) return;
        _current = Mathf.Min(_maxStamina, _current + amount);
        Publish();
    }

    private void Publish()
    {
        EventBus.Publish(new OnStaminaChanged
        { Current = _current, Max = _maxStamina });
    }

    // =========================================================================
    // Regeneration mode libre (Update)
    // =========================================================================

    private void Update()
    {
        // Regen graduelle uniquement en mode libre (Inactive)
        if (TurnManager.Instance != null
            && TurnManager.Instance.State != TurnState.Inactive) return;

        if (_current >= _maxStamina) return;

        _freeRegenTimer += Time.deltaTime;
        if (_freeRegenTimer >= _freeRegenInterval)
        {
            _freeRegenTimer = 0f;
            Regen(1);
        }
    }
}
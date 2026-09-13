using UnityEngine;

/// <summary>
/// HeroState - Source unique de verite pour tous les etats du heros.
/// Tous les scripts lisent et ecrivent ici.
/// Un Reset() d'urgence remet tout a zero.
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
    // Reset d urgence - appelez ca si tout se bloque
    // -------------------------------------------------------------------------

    public void ResetAll()
    {
        IsMoving = false;
        IsCalculating = false;
        IsUILocked = false;
        CollectOnArrival = false;
        Debug.Log("<color=#AAFFFF>[HeroState]</color> Reset complet");
    }

    // -------------------------------------------------------------------------
    // Debug - affiche l etat dans la console
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

    // Securite : si bloque plus de 5s sans raison, reset auto
    private float _blockTimer = 0f;

    private void Update()
    {
        if (!HasAppeared) return;

        if (IsBlocked || IsMoving || IsCalculating)
        {
            _blockTimer += Time.deltaTime;
            if (_blockTimer > 3f)   // était 8f — 3s suffit pour débloquer sans gêner le gameplay
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
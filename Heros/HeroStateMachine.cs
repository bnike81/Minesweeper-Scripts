using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// HeroStateMachine — Machine a etats formelle pour le heros.
///
/// Remplace les 8+ flags booleens croises de HeroState par un enum unique
/// qui garantit qu une seule phase est active a la fois. Toute transition
/// passe par TryTransitionTo qui :
///   - Valide la legalite (certaines transitions sont interdites)
///   - Log avec raison pour traceabilite
///   - Detecte les blocages (watchdog interne)
///   - Publie un event global pour les listeners
///
/// USAGE :
///   var sm = HeroState.Instance.SM;
///   sm.TryTransitionTo(HeroPhase.Moving, "clic sur case libre");
///   if (sm.CurrentPhase == HeroPhase.Idle) { ... }
///
/// JOUR 1 : fondation en parallele de HeroState. Les flags existants
/// (IsMoving, etc.) co-existent — ils sont mis a jour en parallele du SM.
/// JOUR 2 : les call-sites passent a TryTransitionTo.
/// JOUR 3 : on retire les flags redondants.
/// </summary>
public class HeroStateMachine
{
    // =========================================================================
    // Etat courant
    // =========================================================================

    public HeroPhase CurrentPhase { get; private set; } = HeroPhase.NotAppeared;
    public HeroPhase PreviousPhase { get; private set; } = HeroPhase.NotAppeared;
    public float TimeInCurrentPhase => Time.time - _phaseEnteredAt;

    private float _phaseEnteredAt;
    private string _lastTransitionReason = "init";

    // =========================================================================
    // Transitions legales
    // =========================================================================

    /// <summary>
    /// Dictionnaire des transitions autorisees.
    /// Clé = phase actuelle, Valeur = phases destination autorisees.
    /// </summary>
    private static readonly Dictionary<HeroPhase, HashSet<HeroPhase>> _legalTransitions
        = new Dictionary<HeroPhase, HashSet<HeroPhase>>
        {
            // Depuis NotAppeared : seulement vers Idle (premier clic)
            [HeroPhase.NotAppeared] = new HashSet<HeroPhase> { HeroPhase.Idle, HeroPhase.Dead },

            // Depuis Idle : n importe quelle action
            [HeroPhase.Idle] = new HashSet<HeroPhase>
        {
            HeroPhase.Calculating,
            HeroPhase.Moving,
            HeroPhase.Jumping,
            HeroPhase.Revealing,
            HeroPhase.Attacking,
            HeroPhase.UILocked,
            HeroPhase.Flinching,
            HeroPhase.Trapped,
            HeroPhase.Dead
        },

            // Depuis Calculating : vers Moving (chemin trouve) ou Idle (echec)
            [HeroPhase.Calculating] = new HashSet<HeroPhase>
        {
            HeroPhase.Idle,
            HeroPhase.Moving,
            HeroPhase.Jumping,
            HeroPhase.Dead
        },

            // Depuis Moving : vers Idle (arrive) ou interruptions (combat, flinch...)
            [HeroPhase.Moving] = new HashSet<HeroPhase>
        {
            HeroPhase.Idle,
            HeroPhase.Calculating,
            HeroPhase.Jumping,
            HeroPhase.Attacking,
            HeroPhase.Flinching,
            HeroPhase.Trapped,
            HeroPhase.Dead
        },

            [HeroPhase.Jumping] = new HashSet<HeroPhase>
        {
            HeroPhase.Idle,
            HeroPhase.Flinching,   // hit recu pendant le saut
            HeroPhase.Dead
        },

            // Depuis Revealing (phase composite) : tout est possible, y compris
            // revenir en Calculating/Moving pendant le flow interne de ApproachAndReveal
            [HeroPhase.Revealing] = new HashSet<HeroPhase>
        {
            HeroPhase.Idle,
            HeroPhase.Calculating,
            HeroPhase.Moving,
            HeroPhase.Jumping,
            HeroPhase.Attacking,
            HeroPhase.Flinching,
            HeroPhase.Trapped,
            HeroPhase.Dead
        },

            // Depuis Attacking : vers Idle (fin anim)
            [HeroPhase.Attacking] = new HashSet<HeroPhase>
        {
            HeroPhase.Idle, HeroPhase.Flinching, HeroPhase.Dead
        },

            // Depuis Flinching : vers Idle (fin anim hurt)
            [HeroPhase.Flinching] = new HashSet<HeroPhase>
        {
            HeroPhase.Idle, HeroPhase.Dead
        },

            // Depuis UILocked : vers Idle OU toute phase d action qui peut
            // s enchainer (ex: UI fermee puis attaque, pickup puis move, etc.)
            [HeroPhase.UILocked] = new HashSet<HeroPhase>
        {
            HeroPhase.Idle,
            HeroPhase.Attacking,
            HeroPhase.Moving,
            HeroPhase.Calculating,
            HeroPhase.Jumping,
            HeroPhase.Flinching,
            HeroPhase.Trapped,
            HeroPhase.Dead
        },

            // Depuis Trapped : vers Idle (sortie piege)
            [HeroPhase.Trapped] = new HashSet<HeroPhase>
        {
            HeroPhase.Idle, HeroPhase.Flinching, HeroPhase.Dead
        },

            // Depuis Dead : pas de retour (sauf reset Run → NotAppeared ou Idle)
            [HeroPhase.Dead] = new HashSet<HeroPhase>
        {
            HeroPhase.NotAppeared, HeroPhase.Idle
        },
        };

    // =========================================================================
    // Config
    // =========================================================================

    public bool Verbose { get; set; } = true;

    /// <summary>Delai max pour chaque phase (watchdog). 0 = pas de limite.</summary>
    public Dictionary<HeroPhase, float> MaxDuration { get; } = new Dictionary<HeroPhase, float>
    {
        [HeroPhase.Calculating] = 2f,    // pathfinder doit finir vite
        [HeroPhase.Moving] = 10f,        // long trajet possible
        [HeroPhase.Jumping] = 1.5f,
        [HeroPhase.Revealing] = 5f,
        [HeroPhase.Attacking] = 3f,
        [HeroPhase.Flinching] = 1f,
        [HeroPhase.UILocked] = 60f,      // dialogues peuvent durer
        [HeroPhase.Trapped] = 30f,
    };

    // =========================================================================
    // API publique
    // =========================================================================

    /// <summary>
    /// Tente de transitionner vers une nouvelle phase.
    /// Retourne true si transition legale et effectuee, false sinon.
    /// </summary>
    public bool TryTransitionTo(HeroPhase next, string reason = "")
    {
        if (next == CurrentPhase)
        {
            // Pas un vrai changement — renouvelle juste le timestamp
            _phaseEnteredAt = Time.time;
            return true;
        }

        // Verifier legalite
        if (!_legalTransitions.TryGetValue(CurrentPhase, out var allowed)
            || !allowed.Contains(next))
        {
            if (Verbose)
                Debug.LogWarning($"<color=#FF6666>[HSM]</color> " +
                                 $"REJETE {CurrentPhase} → {next} ({reason})");
            return false;
        }

        // Transition valide
        PreviousPhase = CurrentPhase;
        CurrentPhase = next;
        _phaseEnteredAt = Time.time;
        _lastTransitionReason = reason;

        if (Verbose)
            Debug.Log($"<color=#66FFCC>[HSM]</color> {PreviousPhase} → {CurrentPhase} ({reason})");

        OnPhaseChanged?.Invoke(PreviousPhase, CurrentPhase);
        return true;
    }

    /// <summary>
    /// Force une transition sans validation (a utiliser seulement pour reset).
    /// </summary>
    public void ForceTransitionTo(HeroPhase next, string reason = "force")
    {
        PreviousPhase = CurrentPhase;
        CurrentPhase = next;
        _phaseEnteredAt = Time.time;
        _lastTransitionReason = reason;

        if (Verbose)
            Debug.Log($"<color=#FFCC66>[HSM]</color> FORCE {PreviousPhase} → {CurrentPhase} ({reason})");

        OnPhaseChanged?.Invoke(PreviousPhase, CurrentPhase);
    }

    public bool IsInPhase(HeroPhase phase) => CurrentPhase == phase;

    public bool IsInAnyPhase(params HeroPhase[] phases)
    {
        foreach (var p in phases) if (CurrentPhase == p) return true;
        return false;
    }

    // =========================================================================
    // Helpers remplacant les anciens flags
    // =========================================================================

    public bool HasAppeared => CurrentPhase != HeroPhase.NotAppeared;
    public bool IsIdle => CurrentPhase == HeroPhase.Idle;
    public bool IsMoving => CurrentPhase == HeroPhase.Moving;
    public bool IsCalculating => CurrentPhase == HeroPhase.Calculating;
    public bool IsJumping => CurrentPhase == HeroPhase.Jumping;
    public bool IsAttacking => CurrentPhase == HeroPhase.Attacking;
    public bool IsFlinching => CurrentPhase == HeroPhase.Flinching;
    public bool IsRevealing => CurrentPhase == HeroPhase.Revealing;
    public bool IsUILocked => CurrentPhase == HeroPhase.UILocked;
    public bool IsTrapped => CurrentPhase == HeroPhase.Trapped;
    public bool IsDead => CurrentPhase == HeroPhase.Dead;

    /// <summary>
    /// Le heros est "libre" — peut recevoir une action du joueur.
    /// Equivalent de CanMove dans l ancien HeroState.
    /// </summary>
    public bool CanAct => CurrentPhase == HeroPhase.Idle;

    /// <summary>Alias pour compat avec code existant.</summary>
    public bool CanMove => CanAct;

    // =========================================================================
    // Evenement
    // =========================================================================

    /// <summary>Appele a chaque changement de phase : (previous, current).</summary>
    public System.Action<HeroPhase, HeroPhase> OnPhaseChanged;

    // =========================================================================
    // Watchdog — a appeler dans HeroState.Update
    // =========================================================================

    /// <summary>
    /// Verifie si la phase actuelle dure trop longtemps et force un retour
    /// a Idle si necessaire. A appeler chaque frame.
    /// </summary>
    public void TickWatchdog()
    {
        if (!MaxDuration.TryGetValue(CurrentPhase, out float maxDur)) return;
        if (maxDur <= 0f) return;
        if (TimeInCurrentPhase < maxDur) return;

        Debug.LogWarning($"<color=#FFAA00>[HSM]</color> WATCHDOG — phase {CurrentPhase} " +
                         $"dure depuis {TimeInCurrentPhase:F1}s (max {maxDur}s) — force Idle");
        ForceTransitionTo(HeroPhase.Idle, "watchdog timeout");
    }

    /// <summary>Reset complet (RunStarted, Game Over, etc.).</summary>
    public void Reset()
    {
        if (Verbose)
            Debug.Log("<color=#AAFFFF>[HSM]</color> Reset — phase → NotAppeared");
        ForceTransitionTo(HeroPhase.NotAppeared, "reset");
    }
}
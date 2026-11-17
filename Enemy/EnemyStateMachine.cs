// ============================================================================
// EnemyStateMachine — Jour 11
//
// FSM par ennemi. Chaque EnemyInstance possede la sienne.
// Memes principes que HeroStateMachine : transitions legales, event
// OnPhaseChanged, watchdog optionnel par phase.
//
// Les call-sites ne sont PAS obliges de migrer immediatement. Dans un premier
// temps, la FSM est passive : elle refletera l etat des flags legacy
// (_isDead, _isAnimating, _isAttacking). Dans un second temps, les call-sites
// peuvent progressivement utiliser SM.TryTransitionTo(...) directement.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

public class EnemyStateMachine
{
    // =========================================================================
    // Etat courant
    // =========================================================================

    public EnemyPhase CurrentPhase { get; private set; } = EnemyPhase.NotSpawned;

    public event Action<EnemyPhase, EnemyPhase> OnPhaseChanged;

    private readonly string _id;

    public EnemyStateMachine(string id = "Enemy")
    {
        _id = id;
    }

    // =========================================================================
    // Transitions legales
    // =========================================================================

    private readonly Dictionary<EnemyPhase, HashSet<EnemyPhase>> _legalTransitions
        = new Dictionary<EnemyPhase, HashSet<EnemyPhase>>
        {
            [EnemyPhase.NotSpawned] = new HashSet<EnemyPhase>
        {
            EnemyPhase.Idle,
            EnemyPhase.Dead
        },

            [EnemyPhase.Idle] = new HashSet<EnemyPhase>
        {
            EnemyPhase.Alert,
            EnemyPhase.Moving,
            EnemyPhase.Attacking,
            EnemyPhase.Flinching,
            EnemyPhase.LostSight,
            EnemyPhase.Dead
        },

            [EnemyPhase.Alert] = new HashSet<EnemyPhase>
        {
            EnemyPhase.Idle,
            EnemyPhase.Moving,
            EnemyPhase.Attacking,
            EnemyPhase.Flinching,
            EnemyPhase.Dead
        },

            [EnemyPhase.Moving] = new HashSet<EnemyPhase>
        {
            EnemyPhase.Idle,
            EnemyPhase.Alert,
            EnemyPhase.Attacking,
            EnemyPhase.Flinching,
            EnemyPhase.Dead
        },

            [EnemyPhase.Attacking] = new HashSet<EnemyPhase>
        {
            EnemyPhase.Idle,
            EnemyPhase.Flinching,
            EnemyPhase.Dead
        },

            [EnemyPhase.Flinching] = new HashSet<EnemyPhase>
        {
            EnemyPhase.Idle,
            EnemyPhase.Attacking,
            EnemyPhase.Moving,
            EnemyPhase.Alert,
            EnemyPhase.LostSight,
            EnemyPhase.Dead
        },

            [EnemyPhase.LostSight] = new HashSet<EnemyPhase>
        {
            EnemyPhase.Idle,
            EnemyPhase.Alert,
            EnemyPhase.Moving,
            EnemyPhase.Flinching,
            EnemyPhase.Dead
        },

            // Dead est TERMINAL — aucune transition sortante
            [EnemyPhase.Dead] = new HashSet<EnemyPhase>()
        };

    // =========================================================================
    // Durees max par phase (watchdog)
    // =========================================================================

    private readonly Dictionary<EnemyPhase, float> _maxDuration
        = new Dictionary<EnemyPhase, float>
        {
            [EnemyPhase.Alert] = 3f,
            [EnemyPhase.Moving] = 3f,
            [EnemyPhase.Attacking] = 3f,
            [EnemyPhase.Flinching] = 2f,
            [EnemyPhase.LostSight] = 3f
        };

    private float _phaseStartTime = 0f;

    // =========================================================================
    // API
    // =========================================================================

    public bool CanTransitionTo(EnemyPhase phase)
    {
        if (!_legalTransitions.TryGetValue(CurrentPhase, out var allowed))
            return false;
        return allowed.Contains(phase);
    }

    public bool TryTransitionTo(EnemyPhase newPhase, string reason = "")
    {
        if (CurrentPhase == newPhase) return true; // No-op

        if (!CanTransitionTo(newPhase))
        {
            Debug.LogWarning("<color=#FFAA00>[ESM " + _id + "]</color> REJETE "
                + CurrentPhase + " → " + newPhase
                + (string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")"));
            return false;
        }

        var prev = CurrentPhase;
        CurrentPhase = newPhase;
        _phaseStartTime = Time.time;

        Debug.Log("<color=#FFCC66>[ESM " + _id + "]</color> " + prev + " → " + newPhase
            + (string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")"));

        OnPhaseChanged?.Invoke(prev, newPhase);
        return true;
    }

    public void ForceTransitionTo(EnemyPhase newPhase, string reason = "")
    {
        if (CurrentPhase == newPhase) return;

        var prev = CurrentPhase;
        CurrentPhase = newPhase;
        _phaseStartTime = Time.time;

        Debug.Log("<color=#FF6666>[ESM " + _id + "]</color> FORCE " + prev + " → " + newPhase
            + (string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")"));

        OnPhaseChanged?.Invoke(prev, newPhase);
    }

    public void Reset()
    {
        ForceTransitionTo(EnemyPhase.NotSpawned, "Reset");
    }

    // =========================================================================
    // Watchdog
    // =========================================================================

    public void TickWatchdog()
    {
        if (!_maxDuration.TryGetValue(CurrentPhase, out float maxDur))
            return;

        float elapsed = Time.time - _phaseStartTime;
        if (elapsed > maxDur)
        {
            Debug.LogWarning("<color=#FF8800>[ESM " + _id + "]</color> WATCHDOG "
                + CurrentPhase + " trop long (" + elapsed.ToString("F1") + "s > "
                + maxDur + "s) — force Idle");
            ForceTransitionTo(EnemyPhase.Idle, "watchdog " + CurrentPhase);
        }
    }

    // =========================================================================
    // Helpers lecture
    // =========================================================================

    public bool IsIdle => CurrentPhase == EnemyPhase.Idle;
    public bool IsDead => CurrentPhase == EnemyPhase.Dead;
    public bool IsBusy =>
        CurrentPhase == EnemyPhase.Moving
        || CurrentPhase == EnemyPhase.Attacking
        || CurrentPhase == EnemyPhase.Flinching
        || CurrentPhase == EnemyPhase.Alert;
    public bool CanAct => IsIdle; // Pour l IA : ne peut agir que si Idle
}
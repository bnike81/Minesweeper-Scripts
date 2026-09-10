using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EventBus - Systeme d'evenements central.
/// </summary>
public static class EventBus
{
    private static readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public static void Subscribe<T>(Action<T> handler)
    {
        var type = typeof(T);
        if (!_handlers.ContainsKey(type)) _handlers[type] = new List<Delegate>();
        _handlers[type].Add(handler);
    }

    public static void Unsubscribe<T>(Action<T> handler)
    {
        var type = typeof(T);
        if (_handlers.ContainsKey(type)) _handlers[type].Remove(handler);
    }

    public static void Clear()
    {
        _handlers.Clear();
    }

    public static void Publish<T>(T evt)
    {
        var type = typeof(T);
        if (!_handlers.TryGetValue(type, out var list) || list.Count == 0) return;

        // ToArray() pour snapshot — évite les problèmes si un handler se désubscrit
        var snapshot = list.ToArray();
        foreach (var h in snapshot)
        {
            try { ((Action<T>)h)(evt); }
            catch (Exception e)
            {
                Debug.LogError("[EventBus] Erreur handler " + typeof(T).Name + ": " + e);
            }
        }
    }
}

// ---- Etat du jeu ------------------------------------------------------------
public struct OnRunStarted { }
public struct OnRunEnded { public bool Victory; }
public struct OnGameOver { public string Reason; }
public struct OnVictory { }
public struct OnFirstClick { public int X, Y; }
public struct OnGameStateChanged { public GameState OldState, NewState; }

// ---- Grille -----------------------------------------------------------------
public struct OnGridGenerated { public int Width, Height; }
public struct OnGridDataReady { public int Width, Height; }
public struct OnGridExtended { public int NewHeight, Level; }
public struct OnCellRevealed { public int X, Y; public CellContent Content; }
public struct OnCellsRevealed
{
    public System.Collections.Generic.List<Cell> Cells;
    // true = publication récapitulative de fin de batch (liste complète).
    // Fog/Loot/WorldObject l'ignorent (déjà traité en batches).
    // Les archers ne traitent QUE celle-ci (une seule vérification par révélation).
    public bool IsSummary;
}
public struct OnCellFlagged { public int X, Y; public bool IsFlagged; }
public struct OnBossRevealed { public int X, Y; }
public struct OnItemCollected { public string ItemId; }

// ---- Joueur -----------------------------------------------------------------
public struct OnPlayerDamaged { public int Damage, CurrentHP, MaxHP; }
public struct OnPlayerHealed { public int Amount, CurrentHP, MaxHP; }
public struct OnPlayerDied { }
public struct OnXPGained { public int Amount; public int TotalXP; }
public struct OnLevelUp { public int OldLevel, NewLevel; }
public struct OnHUDRefreshRequest { }

// ---- Notifications ----------------------------------------------------------
public struct OnNotification { public string Message; public NotificationType Type; }

// ---- Inventaire -------------------------------------------------------------
public struct OnInventoryChanged { }
public struct OnGoldChanged { public int Amount, Total; }
public struct OnItemPickedUp { public ItemID ItemID; public int Quantity; public Vector3 WorldPos; }

// ---- Equipement -------------------------------------------------------------
public struct OnEquipmentChanged { }

// Publie avant placement dangers lors d'une extension de grille
public struct OnGridExtending { public int OldHeight, NewHeight, Level, Width; }
using UnityEngine;

/// <summary>
/// PlayerStats — Gère PV, XP et niveau du joueur.
/// Reçoit les événements de dégâts/XP via EventBus.
/// Publie les événements de level up, mort, soin.
/// </summary>
public class PlayerStats : MonoBehaviour
{
    public static PlayerStats Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("═══ Stats de Base ═══")]
    [Tooltip("PV maximaux de départ")]
    [SerializeField] private int _maxHP = 5;

    [Tooltip("PV actuels au démarrage (0 = max)")]
    [SerializeField] private int _startHP = 0;

    [Header("═══ XP & Niveaux ═══")]
    [Tooltip("Courbe XP : XP requis = base × level^exponent")]
    [SerializeField] private int _xpBase = 30;

    [SerializeField] private float _xpExponent = 1.4f;

    [Tooltip("Niveau maximum atteignable")]
    [SerializeField] private int _maxLevel = 10;

    [Header("═══ État Courant (Lecture seule) ═══")]
    [SerializeField, ReadOnly] private int _currentHP;
    [SerializeField, ReadOnly] private int _currentMaxHP;
    [SerializeField, ReadOnly] private int _currentXP;
    [SerializeField, ReadOnly] private int _currentLevel;
    [SerializeField, ReadOnly] private int _xpToNextLevel;
    [SerializeField, ReadOnly] private int _totalDamageTaken;
    [SerializeField, ReadOnly] private int _flagsAvailable;
    [SerializeField, ReadOnly] private int _bonusFlags;

    // ─── Propriétés ───────────────────────────────────────────────────────────
    public int CurrentHP => _currentHP;
    public int MaxHP => _currentMaxHP;
    public int CurrentXP => _currentXP;
    public int CurrentLevel => _currentLevel;
    public int XPToNextLevel => _xpToNextLevel;
    public int FlagsAvailable => _flagsAvailable;
    public bool IsAlive => _currentHP > 0;
    public float HPPercent => _currentMaxHP > 0 ? (float)_currentHP / _currentMaxHP : 0f;
    public float XPPercent => _xpToNextLevel > 0 ? (float)_currentXP / _xpToNextLevel : 1f;

    // ─── Lifecycle ────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
        EventBus.Subscribe<OnPlayerDamaged>(OnDamaged);
        EventBus.Subscribe<OnPlayerHealed>(OnHealed);
        EventBus.Subscribe<OnXPGained>(OnXPGained);
        EventBus.Subscribe<OnItemCollected>(OnItemCollected);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
        EventBus.Unsubscribe<OnPlayerDamaged>(OnDamaged);
        EventBus.Unsubscribe<OnPlayerHealed>(OnHealed);
        EventBus.Unsubscribe<OnXPGained>(OnXPGained);
        EventBus.Unsubscribe<OnItemCollected>(OnItemCollected);
    }

    // ─── Initialisation ───────────────────────────────────────────────────────
    private void OnRunStarted(OnRunStarted evt) => ResetStats();

    public void ResetStats()
    {
        _currentMaxHP = _maxHP;
        _currentHP = _startHP > 0 ? Mathf.Min(_startHP, _maxHP) : _maxHP;
        _currentXP = 0;
        _currentLevel = 1;
        _totalDamageTaken = 0;

        // Mode Debug — appliquer XP exponent dev
        var gmc = GameModeConfig.Instance;
        if (gmc != null && gmc.IsDebug)
            _xpExponent = gmc.DebugXpExponent;
        _flagsAvailable = 3; // Flags de base
        _bonusFlags = 0;

        ComputeXPToNextLevel();
        EventBus.Publish(new OnHUDRefreshRequest());

    }

    // ─── Dégâts ───────────────────────────────────────────────────────────────
    private void OnDamaged(OnPlayerDamaged evt)
    {
        if (!IsAlive) return;
        // Ignore les events deja traites (HP reel fourni = nos HP actuels)
        if (evt.CurrentHP > 0 && evt.MaxHP > 0) return;

        _currentHP = Mathf.Max(0, _currentHP - evt.Damage);
        _totalDamageTaken += evt.Damage;

        EventBus.Publish(new OnNotification
        {
            Message = "-" + evt.Damage + " PV",
            Type = NotificationType.Damage
        });


        if (_currentHP <= 0) TriggerDeath();
        else EventBus.Publish(new OnHUDRefreshRequest());
    }

    // ─── Soins ────────────────────────────────────────────────────────────────
    private void OnHealed(OnPlayerHealed evt)
    {
        if (!IsAlive) return;

        int oldHP = _currentHP;
        _currentHP = Mathf.Min(_currentMaxHP, _currentHP + evt.Amount);
        int actualHeal = _currentHP - oldHP;

        if (actualHeal > 0)
        {
            EventBus.Publish(new OnPlayerHealed
            {
                Amount = actualHeal,
                CurrentHP = _currentHP,
                MaxHP = _currentMaxHP
            });
            EventBus.Publish(new OnNotification
            {
                Message = $"+{actualHeal} PV",
                Type = NotificationType.Item
            });
            EventBus.Publish(new OnHUDRefreshRequest());
        }
    }

    public void Heal(int amount)
    {
        if (amount <= 0) return;
        int oldHP = _currentHP;
        _currentHP = Mathf.Min(_currentMaxHP, _currentHP + amount);
        int actual = _currentHP - oldHP;
        if (actual <= 0) return;

        EventBus.Publish(new OnPlayerHealed
        {
            Amount = actual,
            CurrentHP = _currentHP,
            MaxHP = _currentMaxHP
        });
        EventBus.Publish(new OnHUDRefreshRequest());
    }

    public void AddBonusFlags(int amount)
    {
        _bonusFlags += amount;
        _flagsAvailable += amount;
        EventBus.Publish(new OnHUDRefreshRequest());
    }

    public void IncreaseMaxHP(int amount)
    {
        _currentMaxHP += amount;
        _currentHP += amount; // Soin en prime pour la fontaine
        EventBus.Publish(new OnHUDRefreshRequest());
    }

    // XP & Level Up
    private void OnXPGained(OnXPGained evt)
    {
        if (_currentLevel >= _maxLevel) return;

        _currentXP += evt.Amount;

        // NOTE : on ne republie PAS OnXPGained ici pour eviter la boucle infinie
        // Le HUD se met a jour via OnHUDRefreshRequest en fin de methode

        // Verifier level up — grouper les niveaux pour éviter cascade ExtendGrid
        int levelBefore = _currentLevel;
        while (_currentXP >= _xpToNextLevel && _currentLevel < _maxLevel)
        {
            _currentXP -= _xpToNextLevel;
            _currentLevel++;
            ComputeXPToNextLevel();
        }

        // Publier UN seul OnLevelUp au niveau final (pas un par niveau)
        if (_currentLevel > levelBefore)
        {
            EventBus.Publish(new OnLevelUp { OldLevel = levelBefore, NewLevel = _currentLevel });
            EventBus.Publish(new OnNotification
            {
                Message = _currentLevel > levelBefore + 1
                    ? $"NIVEAU {_currentLevel} ! (+{_currentLevel - levelBefore})"
                    : $"NIVEAU {_currentLevel} !",
                Type = NotificationType.LevelUp
            });
        }

        EventBus.Publish(new OnHUDRefreshRequest());
    }

    private void ComputeXPToNextLevel()
    {
        _xpToNextLevel = Mathf.RoundToInt(_xpBase * Mathf.Pow(_currentLevel, _xpExponent));
    }

    // ─── Collecte d'objets ────────────────────────────────────────────────────
    private void OnItemCollected(OnItemCollected evt)
    {
        switch (evt.ItemId)
        {
            case nameof(CellContent.Treasure_Campfire):
                Heal(1);
                break;

            case nameof(CellContent.Treasure_Flower):
                _bonusFlags++;
                _flagsAvailable++;
                EventBus.Publish(new OnNotification { Message = "+1 Drapeau !", Type = NotificationType.Item });
                EventBus.Publish(new OnHUDRefreshRequest());
                break;

            case nameof(CellContent.Treasure_Fountain):
                IncreaseMaxHP(1);
                EventBus.Publish(new OnNotification { Message = "PV Max +1 !", Type = NotificationType.Item });
                break;

            case nameof(CellContent.Treasure_Scroll):
                // GridManager gèrera la révélation 3×3
                EventBus.Publish(new OnNotification { Message = "Parchemin ! Zone révélée.", Type = NotificationType.Item });
                break;

            case nameof(CellContent.Treasure_Chest):
                // Loot aléatoire : sera géré par TreasureManager (Phase 5)
                EventBus.Publish(new OnNotification { Message = "Coffre découvert !", Type = NotificationType.Item });
                break;
        }
    }

    // ─── Mort ─────────────────────────────────────────────────────────────────
    private void TriggerDeath()
    {
        EventBus.Publish(new OnPlayerDied());
        GameManager.Instance?.TriggerGameOver();
    }

    // ─── Debug ────────────────────────────────────────────────────────────────
    [ContextMenu("Test: Infliger 1 dégât")]
    private void DebugDamage()
        => EventBus.Publish(new OnPlayerDamaged { Damage = 1, CurrentHP = _currentHP, MaxHP = _currentMaxHP });

    [ContextMenu("Test: Soigner 1 PV")]
    private void DebugHeal() => Heal(1);

    [ContextMenu("Test: +20 XP")]
    private void DebugXP()
        => EventBus.Publish(new OnXPGained { Amount = 20, TotalXP = _currentXP + 20 });
}
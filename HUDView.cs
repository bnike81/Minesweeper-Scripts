using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// HUDView — Affiche PV, XP, Niveau, Flags en temps réel.
/// S'abonne à OnHUDRefreshRequest via EventBus.
/// </summary>
public class HUDView : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("═══ PV ═══")]
    [SerializeField] private Image _hpBar;
    [SerializeField] private TextMeshProUGUI _hpText;
    [SerializeField] private Color _hpColorFull = new Color(0.2f, 0.9f, 0.3f);
    [SerializeField] private Color _hpColorLow = new Color(0.9f, 0.2f, 0.2f);
    [Tooltip("Seuil pour passer en couleur danger")]
    [SerializeField, Range(0f, 1f)] private float _hpLowThreshold = 0.35f;

    [Header("═══ XP ═══")]
    [SerializeField] private Image _xpBar;
    [SerializeField] private TextMeshProUGUI _xpText;
    [SerializeField] private Color _xpBarColor = new Color(0.3f, 0.6f, 1f);

    [Header("═══ Niveau ═══")]
    [SerializeField] private TextMeshProUGUI _levelText;

    [Header("═══ Flags ═══")]
    [SerializeField] private TextMeshProUGUI _flagsText;
    [SerializeField] private GameObject _flagIcon;

    [Header("═══ Notification ═══")]
    [SerializeField] private GameObject _notificationPanel;
    [SerializeField] private TextMeshProUGUI _notificationText;
    [SerializeField] private float _notificationDuration = 2f;
    [SerializeField] private Color _notifColorDamage = new Color(1f, 0.3f, 0.3f);
    [SerializeField] private Color _notifColorXP = new Color(0.9f, 0.9f, 0.2f);
    [SerializeField] private Color _notifColorLevelUp = new Color(1f, 0.8f, 0f);
    [SerializeField] private Color _notifColorItem = new Color(0.3f, 1f, 0.6f);

    // ─── Lifecycle ────────────────────────────────────────────────────────────
    private void OnEnable()
    {
        EventBus.Subscribe<OnHUDRefreshRequest>(OnRefresh);
        EventBus.Subscribe<OnNotification>(OnNotification);
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnHUDRefreshRequest>(OnRefresh);
        EventBus.Unsubscribe<OnNotification>(OnNotification);
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
    }

    private void Start()
    {
        if (_notificationPanel != null) _notificationPanel.SetActive(false);
        RefreshAll();
    }

    // ─── Handlers ─────────────────────────────────────────────────────────────
    private void OnRefresh(OnHUDRefreshRequest evt) => RefreshAll();
    private void OnRunStarted(OnRunStarted evt) => RefreshAll();

    // ─── Refresh ──────────────────────────────────────────────────────────────
    private void RefreshAll()
    {
        var stats = PlayerStats.Instance;
        if (stats == null) return;

        RefreshHP(stats);
        RefreshXP(stats);
        RefreshLevel(stats);
        RefreshFlags(stats);
    }

    private void RefreshHP(PlayerStats stats)
    {
        if (_hpBar != null)
        {
            _hpBar.fillAmount = stats.HPPercent;
            _hpBar.color = Color.Lerp(_hpColorLow, _hpColorFull,
                Mathf.InverseLerp(0, _hpLowThreshold, stats.HPPercent));
        }
        if (_hpText != null &&
            (stats.CurrentHP != _lastHP || stats.MaxHP != _lastMaxHP))
        {
            _lastHP = stats.CurrentHP;
            _lastMaxHP = stats.MaxHP;
            _hpText.SetText("{0} / {1}", stats.CurrentHP, stats.MaxHP);
        }
    }

    // Caches pour éviter les allocations string inutiles
    private int _lastHP = -1, _lastMaxHP = -1;
    private int _lastXP = -1, _lastXPNext = -1;
    private int _lastLevel = -1;
    private int _lastFlags = -1;

    private void RefreshXP(PlayerStats stats)
    {
        if (_xpBar != null)
        {
            _xpBar.fillAmount = stats.XPPercent;
            _xpBar.color = _xpBarColor;
        }
        // Seulement si les valeurs changent — zéro allocation sinon
        if (_xpText != null &&
            (stats.CurrentXP != _lastXP || stats.XPToNextLevel != _lastXPNext))
        {
            _lastXP = stats.CurrentXP;
            _lastXPNext = stats.XPToNextLevel;
            _xpText.SetText("{0} / {1} XP", stats.CurrentXP, stats.XPToNextLevel);
        }
    }

    private void RefreshLevel(PlayerStats stats)
    {
        if (_levelText != null && stats.CurrentLevel != _lastLevel)
        {
            _lastLevel = stats.CurrentLevel;
            _levelText.SetText("Niv. {0}", stats.CurrentLevel);
        }
    }

    private void RefreshFlags(PlayerStats stats)
    {
        if (_flagsText != null && stats.FlagsAvailable != _lastFlags)
        {
            _lastFlags = stats.FlagsAvailable;
            _flagsText.SetText("🚩 {0}", stats.FlagsAvailable);
        }
    }

    // ─── Notifications ────────────────────────────────────────────────────────
    private void OnNotification(OnNotification evt)
    {
        if (_notificationPanel == null || _notificationText == null) return;

        StopAllCoroutines();
        _notificationText.text = evt.Message;
        _notificationText.color = GetNotifColor(evt.Type);
        _notificationPanel.SetActive(true);
        StartCoroutine(HideNotifAfter(_notificationDuration));
    }

    private System.Collections.IEnumerator HideNotifAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_notificationPanel != null) _notificationPanel.SetActive(false);
    }

    private Color GetNotifColor(NotificationType type) => type switch
    {
        NotificationType.Damage => _notifColorDamage,
        NotificationType.XP => _notifColorXP,
        NotificationType.LevelUp => _notifColorLevelUp,
        NotificationType.Item => _notifColorItem,
        _ => Color.white
    };
}
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// XPBarUI - Barre d'experience horizontale.
/// Remplissage doux de gauche a droite + pulse au gain d'XP.
/// Placer juste en dessous de la barre de coeurs.
/// </summary>
public class XPBarUI : MonoBehaviour
{
    public static XPBarUI Instance { get; private set; }

    [Header("=== References ===")]
    [Tooltip("Image Fill pour la barre XP (Fill Method = Horizontal)")]
    [SerializeField] private Image _xpBarFill;
    [Tooltip("Texte affichant XP actuel / XP requis")]
    [SerializeField] private TextMeshProUGUI _xpText;
    [Tooltip("Texte affichant le niveau actuel")]
    [SerializeField] private TextMeshProUGUI _levelText;
    [Tooltip("Background de la barre (pour le pulse)")]
    [SerializeField] private Image _xpBarBackground;

    [Header("=== Couleurs ===")]
    [SerializeField] private Color _colorLow = new Color(0.3f, 0.6f, 1f);
    [SerializeField] private Color _colorHigh = new Color(0.6f, 0.2f, 1f);
    [SerializeField] private Color _colorPulse = Color.white;

    [Header("=== Animation Remplissage ===")]
    [Tooltip("Vitesse de remplissage doux (plus grand = plus rapide)")]
    [SerializeField, Range(0.5f, 10f)] private float _fillSpeed = 3f;

    [Header("=== Animation Pulse ===")]
    [Tooltip("Scale max du pulse au gain XP")]
    [SerializeField, Range(1.02f, 1.2f)] private float _pulseScale = 1.08f;
    [Tooltip("Duree du pulse")]
    [SerializeField, Range(0.1f, 0.4f)] private float _pulseDuration = 0.2f;

    // -------------------------------------------------------------------------

    private float _targetFill = 0f;
    private float _currentFill = 0f;
    private int _currentXP = 0;
    private int _xpToNext = 100;
    private RectTransform _barRect;

    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _barRect = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnXPGained>(OnXPGained);
        EventBus.Subscribe<OnLevelUp>(OnLevelUp);
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
        EventBus.Subscribe<OnHUDRefreshRequest>(OnHUDRefresh);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnXPGained>(OnXPGained);
        EventBus.Unsubscribe<OnLevelUp>(OnLevelUp);
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
        EventBus.Unsubscribe<OnHUDRefreshRequest>(OnHUDRefresh);
    }

    private void Start()
    {
        StartCoroutine(InitNextFrame());
    }

    private IEnumerator InitNextFrame()
    {
        yield return null;
        yield return null;

        // Force le fill a 0 puis laisse le remplissage doux prendre le relais
        _currentFill = 0f;
        if (_xpBarFill != null) _xpBarFill.fillAmount = 0f;

        RefreshFromStats();
    }

    private void Update()
    {
        // Remplissage doux vers la cible
        if (Mathf.Abs(_currentFill - _targetFill) > 0.001f)
        {
            _currentFill = Mathf.MoveTowards(
                _currentFill, _targetFill,
                _fillSpeed * Time.deltaTime);

            if (_xpBarFill != null)
            {
                _xpBarFill.fillAmount = _currentFill;
                _xpBarFill.color = Color.Lerp(_colorLow, _colorHigh, _currentFill);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Handlers
    // -------------------------------------------------------------------------

    private void OnRunStarted(OnRunStarted evt)
    {
        StartCoroutine(InitNextFrame());
    }

    private void OnHUDRefresh(OnHUDRefreshRequest evt) => RefreshFromStats();

    private void OnXPGained(OnXPGained evt)
    {
        RefreshFromStats();
        StartCoroutine(PulseAnimation());
    }

    private void OnLevelUp(OnLevelUp evt)
    {
        // Reset visuel puis remplit vers la nouvelle cible
        _currentFill = 0f;
        if (_xpBarFill != null) _xpBarFill.fillAmount = 0f;
        RefreshFromStats();
        StartCoroutine(LevelUpFlash());
    }

    // -------------------------------------------------------------------------
    // Refresh
    // -------------------------------------------------------------------------

    private void RefreshFromStats()
    {
        var ps = PlayerStats.Instance;
        if (ps == null) return;

        _currentXP = ps.CurrentXP;
        _xpToNext = ps.XPToNextLevel;

        _targetFill = _xpToNext > 0
            ? Mathf.Clamp01((float)_currentXP / _xpToNext)
            : 0f;

        UpdateText();
    }

    // Caches pour éviter allocations string
    private int _lastXPText = -1, _lastXPNextText = -1, _lastLevelText = -1;

    private void UpdateText()
    {
        if (_xpText != null &&
            (_currentXP != _lastXPText || _xpToNext != _lastXPNextText))
        {
            _lastXPText = _currentXP;
            _lastXPNextText = _xpToNext;
            _xpText.SetText("{0} / {1}", _currentXP, _xpToNext);
        }

        if (_levelText != null && PlayerStats.Instance != null)
        {
            int lvl = PlayerStats.Instance.CurrentLevel;
            if (lvl != _lastLevelText)
            {
                _lastLevelText = lvl;
                _levelText.SetText("Niv. {0}", lvl);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Animations
    // -------------------------------------------------------------------------

    private IEnumerator PulseAnimation()
    {
        if (_barRect == null) yield break;

        Vector3 original = _barRect.localScale;
        float elapsed = 0f;
        float half = _pulseDuration * 0.5f;

        // Couleur flash
        if (_xpBarFill != null)
        {
            Color origColor = _xpBarFill.color;
            _xpBarFill.color = _colorPulse;
            yield return new WaitForSeconds(0.05f);
            _xpBarFill.color = origColor;
        }

        // Scale up
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / half);
            _barRect.localScale = Vector3.Lerp(original, original * _pulseScale, t);
            yield return null;
        }

        // Scale down
        elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / half);
            _barRect.localScale = Vector3.Lerp(original * _pulseScale, original, t);
            yield return null;
        }

        _barRect.localScale = original;
    }

    private IEnumerator LevelUpFlash()
    {
        if (_xpBarFill == null) yield break;

        // Flash blanc au level up
        for (int i = 0; i < 3; i++)
        {
            _xpBarFill.color = Color.white;
            yield return new WaitForSeconds(0.08f);
            _xpBarFill.color = _colorLow;
            yield return new WaitForSeconds(0.08f);
        }
    }
}
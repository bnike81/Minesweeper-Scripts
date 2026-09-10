using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// HeartBarUI - Barre de vie en coeurs style RPG.
/// 1 coeur = 1 PV. Anime au gain et a la perte.
/// Placer dans le Canvas en haut a gauche.
/// </summary>
public class HeartBarUI : MonoBehaviour
{
    public static HeartBarUI Instance { get; private set; }

    [Header("=== Sprites ===")]
    [SerializeField] private Sprite _heartFull;
    [SerializeField] private Sprite _heartEmpty;

    [Header("=== Prefab Coeur ===")]
    [Tooltip("Prefab d'un coeur : Image UI 16x16")]
    [SerializeField] private GameObject _heartPrefab;

    [Header("=== Layout ===")]
    [Tooltip("Espacement entre les coeurs")]
    [SerializeField, Range(0f, 8f)] private float _spacing = 2f;
    [Tooltip("Taille d'un coeur en pixels UI")]
    [SerializeField] private Vector2 _heartSize = new Vector2(16f, 16f);

    [Header("=== Animation Gain de coeur ===")]
    [Tooltip("Scale max au moment du gain")]
    [SerializeField, Range(1.1f, 2f)] private float _gainScaleMax = 1.5f;
    [Tooltip("Duree de l'animation de gain")]
    [SerializeField, Range(0.1f, 0.5f)] private float _gainDuration = 0.25f;

    [Header("=== Animation Perte de coeur ===")]
    [Tooltip("Intensite du shake a la perte")]
    [SerializeField, Range(2f, 15f)] private float _lossShakeIntensity = 6f;
    [Tooltip("Duree du shake a la perte")]
    [SerializeField, Range(0.1f, 0.5f)] private float _lossDuration = 0.3f;

    // -------------------------------------------------------------------------

    private List<Image> _hearts = new List<Image>();
    private int _displayedHP = 0;
    private int _displayedMax = 0;
    private RectTransform _barRect;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _barRect = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnHUDRefreshRequest>(OnHUDRefresh);
        EventBus.Subscribe<OnPlayerDamaged>(OnDamaged);
        EventBus.Subscribe<OnPlayerHealed>(OnHealed);
        EventBus.Subscribe<OnLevelUp>(OnLevelUp);
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnHUDRefreshRequest>(OnHUDRefresh);
        EventBus.Unsubscribe<OnPlayerDamaged>(OnDamaged);
        EventBus.Unsubscribe<OnPlayerHealed>(OnHealed);
        EventBus.Unsubscribe<OnLevelUp>(OnLevelUp);
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
    }

    private void Start()
    {
        // Force le refresh au demarrage meme si OnRunStarted est deja passe
        StartCoroutine(InitNextFrame());
    }

    private void OnRunStarted(OnRunStarted evt)
    {
        StartCoroutine(InitNextFrame());
    }

    private IEnumerator InitNextFrame()
    {
        yield return null;
        yield return null; // deux frames pour etre sur
        RefreshFromStats(false);
    }

    // -------------------------------------------------------------------------
    // Handlers
    // -------------------------------------------------------------------------

    private void OnHUDRefresh(OnHUDRefreshRequest evt) => RefreshFromStats(false);

    private void OnDamaged(OnPlayerDamaged evt)
    {
        var ps = PlayerStats.Instance;
        if (ps == null) return;

        int oldHP = _displayedHP;
        // Source unique de verite : PlayerStats
        int realHP = ps.CurrentHP;
        _displayedHP = realHP;

        UpdateHearts();

        // Shake seulement si vraie perte
        int lost = oldHP - realHP;
        if (lost > 0)
            StartCoroutine(LossAnimation(lost));
    }

    private void OnHealed(OnPlayerHealed evt)
    {
        var ps = PlayerStats.Instance;
        if (ps == null) return;

        int oldHP = _displayedHP;
        _displayedHP = ps.CurrentHP;
        UpdateHearts();

        if (_displayedHP > oldHP)
            StartCoroutine(GainAnimation(_displayedHP - oldHP));
    }

    private void OnLevelUp(OnLevelUp evt)
    {
        RefreshFromStats(false);
    }

    // -------------------------------------------------------------------------
    // Refresh
    // -------------------------------------------------------------------------

    private void RefreshFromStats(bool animate)
    {
        var ps = PlayerStats.Instance;
        if (ps == null) return;

        int newMax = ps.MaxHP;
        int newHP = ps.CurrentHP;

        bool needRebuild = newMax != _displayedMax;
        _displayedMax = newMax;
        _displayedHP = newHP;  // Synchronise toujours

        if (needRebuild) RebuildHearts();
        else UpdateHearts();
    }

    // -------------------------------------------------------------------------
    // Construction des coeurs
    // -------------------------------------------------------------------------

    private void RebuildHearts()
    {
        // Supprimer anciens coeurs
        foreach (var h in _hearts)
            if (h != null) Destroy(h.gameObject);
        _hearts.Clear();

        if (_heartPrefab == null || _heartFull == null) return;

        for (int i = 0; i < _displayedMax; i++)
        {
            var go = Instantiate(_heartPrefab, transform);
            go.name = "Heart_" + i;
            var img = go.GetComponent<Image>();
            var rect = go.GetComponent<RectTransform>();

            if (rect != null)
            {
                rect.sizeDelta = _heartSize;
                rect.anchoredPosition = new Vector2(
                    i * (_heartSize.x + _spacing), 0f);
            }

            if (img != null)
            {
                img.sprite = _heartFull;
                img.preserveAspect = true;
            }

            _hearts.Add(img);
        }
    }

    private void UpdateHearts()
    {
        for (int i = 0; i < _hearts.Count; i++)
        {
            if (_hearts[i] == null) continue;
            _hearts[i].sprite = i < _displayedHP ? _heartFull : _heartEmpty;
        }
    }

    // -------------------------------------------------------------------------
    // Animations
    // -------------------------------------------------------------------------

    /// <summary>Anime les coeurs regagnes (agrandissement)</summary>
    private IEnumerator GainAnimation(int count)
    {
        int startIdx = _displayedHP - count;
        for (int i = startIdx; i < Mathf.Min(_displayedHP, _hearts.Count); i++)
        {
            if (i >= 0 && i < _hearts.Count && _hearts[i] != null)
                StartCoroutine(HeartGainPulse(_hearts[i].rectTransform));
        }
        yield break;
    }

    private IEnumerator HeartGainPulse(RectTransform rt)
    {
        if (rt == null) yield break;
        Vector3 original = rt.localScale;
        float elapsed = 0f;
        float half = _gainDuration * 0.5f;

        // Agrandir
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / half);
            rt.localScale = Vector3.Lerp(original, original * _gainScaleMax, t);
            yield return null;
        }
        // Retour
        elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / half);
            rt.localScale = Vector3.Lerp(original * _gainScaleMax, original, t);
            yield return null;
        }
        rt.localScale = original;
    }

    /// <summary>Shake de toute la barre a la perte de coeurs</summary>
    private IEnumerator LossAnimation(int count)
    {
        if (_barRect == null) yield break;

        Vector3 originalPos = _barRect.anchoredPosition3D;
        float elapsed = 0f;

        while (elapsed < _lossDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / _lossDuration;
            float intensity = _lossShakeIntensity * (1f - t);
            _barRect.anchoredPosition3D = originalPos + new Vector3(
                Random.Range(-intensity, intensity),
                Random.Range(-intensity, intensity),
                0f);
            yield return null;
        }

        _barRect.anchoredPosition3D = originalPos;
    }
}
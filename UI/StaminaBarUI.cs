using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// StaminaBarUI - Barre d endurance en points style RPG.
/// 1 point = 1 pt endurance. Anime au gain et a la perte.
/// Meme logique que HeartBarUI.
/// Placer dans le Canvas HUD.
/// </summary>
public class StaminaBarUI : MonoBehaviour
{
    public static StaminaBarUI Instance { get; private set; }

    [Header("=== Sprites ===")]
    [SerializeField] private Sprite _staminaFull;
    [SerializeField] private Sprite _staminaEmpty;

    [Header("=== Prefab Point Endurance ===")]
    [Tooltip("Prefab d un point endurance : Image UI")]
    [SerializeField] private GameObject _staminaPrefab;

    [Header("=== Layout ===")]
    [Tooltip("Espacement entre les points")]
    [SerializeField, Range(0f, 8f)] private float _spacing = 2f;
    [Tooltip("Taille d un point en pixels UI")]
    [SerializeField] private Vector2 _dotSize = new Vector2(16f, 16f);

    [Header("=== Animation Gain ===")]
    [Tooltip("Scale max au moment de la regeneration")]
    [SerializeField, Range(1.1f, 2f)] private float _gainScaleMax = 1.4f;
    [Tooltip("Duree de l animation de gain")]
    [SerializeField, Range(0.1f, 0.5f)] private float _gainDuration = 0.2f;

    [Header("=== Animation Perte ===")]
    [Tooltip("Intensite du shake a la perte")]
    [SerializeField, Range(2f, 15f)] private float _lossShakeIntensity = 4f;
    [Tooltip("Duree du shake a la perte")]
    [SerializeField, Range(0.1f, 0.5f)] private float _lossDuration = 0.2f;

    // -------------------------------------------------------------------------

    private List<Image> _dots = new List<Image>();
    private int _displayedCurrent = 0;
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
        EventBus.Subscribe<OnStaminaChanged>(OnStaminaChanged);
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnStaminaChanged>(OnStaminaChanged);
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
    }

    private void Start()
    {
        StartCoroutine(InitNextFrame());
    }

    private void OnRunStarted(OnRunStarted evt)
    {
        StartCoroutine(InitNextFrame());
    }

    private IEnumerator InitNextFrame()
    {
        yield return null;
        yield return null; // deux frames pour etre sur que StaminaManager.Instance existe
        RefreshFromStamina(false);
    }

    // -------------------------------------------------------------------------
    // Handler
    // -------------------------------------------------------------------------

    private void OnStaminaChanged(OnStaminaChanged evt)
    {
        int oldCurrent = _displayedCurrent;
        int oldMax = _displayedMax;

        _displayedCurrent = evt.Current;
        _displayedMax = evt.Max;

        // Reconstruction si max a change
        if (evt.Max != oldMax)
        {
            RebuildDots();
        }

        UpdateDots();

        // Animations
        if (evt.Current < oldCurrent)
        {
            int lost = oldCurrent - evt.Current;
            StartCoroutine(LossAnimation(lost));
        }
        else if (evt.Current > oldCurrent)
        {
            int gained = evt.Current - oldCurrent;
            StartCoroutine(GainAnimation(gained));
        }
    }

    // -------------------------------------------------------------------------
    // Refresh
    // -------------------------------------------------------------------------

    private void RefreshFromStamina(bool animate)
    {
        var stam = StaminaManager.Instance;
        if (stam == null) return;

        int newMax = stam.Max;
        int newCurrent = stam.Current;

        bool needRebuild = newMax != _displayedMax;
        _displayedMax = newMax;
        _displayedCurrent = newCurrent;

        if (needRebuild) RebuildDots();
        else UpdateDots();
    }

    // -------------------------------------------------------------------------
    // Construction des points
    // -------------------------------------------------------------------------

    private void RebuildDots()
    {
        // Supprimer anciens points
        foreach (var d in _dots)
            if (d != null) Destroy(d.gameObject);
        _dots.Clear();

        if (_staminaPrefab == null || _staminaFull == null) return;

        for (int i = 0; i < _displayedMax; i++)
        {
            var go = Instantiate(_staminaPrefab, transform);
            go.name = "Stamina_" + i;
            var img = go.GetComponent<Image>();
            var rect = go.GetComponent<RectTransform>();

            if (rect != null)
            {
                rect.sizeDelta = _dotSize;
                rect.anchoredPosition = new Vector2(
                    i * (_dotSize.x + _spacing), 0f);
            }

            if (img != null)
            {
                img.sprite = _staminaFull;
                img.preserveAspect = true;
            }

            _dots.Add(img);
        }
    }

    private void UpdateDots()
    {
        for (int i = 0; i < _dots.Count; i++)
        {
            if (_dots[i] == null) continue;
            _dots[i].sprite = i < _displayedCurrent ? _staminaFull : _staminaEmpty;
        }
    }

    // -------------------------------------------------------------------------
    // Animations
    // -------------------------------------------------------------------------

    /// <summary>Anime les points regagnes (agrandissement)</summary>
    private IEnumerator GainAnimation(int count)
    {
        int startIdx = _displayedCurrent - count;
        for (int i = startIdx; i < Mathf.Min(_displayedCurrent, _dots.Count); i++)
        {
            if (i >= 0 && i < _dots.Count && _dots[i] != null)
                StartCoroutine(DotGainPulse(_dots[i].rectTransform));
        }
        yield break;
    }

    private IEnumerator DotGainPulse(RectTransform rt)
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

    /// <summary>Shake de toute la barre a la perte de points</summary>
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
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// EquipmentUI - Bouton equipement dans la colonne droite.
/// Clic -> deploie panneau horizontal 1x4 vers la gauche.
/// </summary>
public class EquipmentUI : MonoBehaviour
{
    public static EquipmentUI Instance { get; private set; }

    [Header("=== Bouton Colonne ===")]
    [SerializeField] private Button _btnEquipment;

    [Header("=== Panneau 1x4 ===")]
    [SerializeField] private GameObject _equipPanel;
    [SerializeField] private EquipSlotUI _slotHelmet;
    [SerializeField] private EquipSlotUI _slotArmor;
    [SerializeField] private EquipSlotUI _slotWeapon;
    [SerializeField] private EquipSlotUI _slotRing;

    [Header("=== Animation ===")]
    [SerializeField, Range(0.1f, 0.5f)] private float _animDuration = 0.2f;

    private bool _panelOpen = false;
    private RectTransform _panelRect;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _panelRect = _equipPanel?.GetComponent<RectTransform>();
        _btnEquipment?.onClick.AddListener(TogglePanel);

        _slotHelmet?.Initialize(EquipSlot.Helmet);
        _slotArmor?.Initialize(EquipSlot.Armor);
        _slotWeapon?.Initialize(EquipSlot.Weapon);
        _slotRing?.Initialize(EquipSlot.Ring);

        if (_equipPanel != null) _equipPanel.SetActive(false);
    }

    private void OnEnable() { EventBus.Subscribe<OnEquipmentChanged>(OnEquipChanged); }
    private void OnDisable() { EventBus.Unsubscribe<OnEquipmentChanged>(OnEquipChanged); }

    private void OnEquipChanged(OnEquipmentChanged evt) => RefreshAll();

    private void RefreshAll()
    {
        _slotHelmet?.Refresh();
        _slotArmor?.Refresh();
        _slotWeapon?.Refresh();
        _slotRing?.Refresh();
    }

    public void TogglePanel()
    {
        _panelOpen = !_panelOpen;
        StopAllCoroutines();
        StartCoroutine(AnimatePanel(_panelOpen));
    }

    private IEnumerator AnimatePanel(bool open)
    {
        if (_equipPanel == null) yield break;

        _equipPanel.SetActive(true);
        float elapsed = 0f;
        float startScale = open ? 0f : 1f;
        float endScale = open ? 1f : 0f;

        if (_panelRect != null)
            _panelRect.localScale = new Vector3(1f, startScale, 1f);

        while (elapsed < _animDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / _animDuration);
            float s = Mathf.Lerp(startScale, endScale, t);
            if (_panelRect != null)
                _panelRect.localScale = new Vector3(1f, s, 1f);
            yield return null;
        }

        if (_panelRect != null)
            _panelRect.localScale = new Vector3(1f, endScale, 1f);

        if (!open) _equipPanel.SetActive(false);
    }
}
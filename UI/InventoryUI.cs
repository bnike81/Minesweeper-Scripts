using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// InventoryUI - Colonne droite fixe + panneau inventaire deployable.
///
/// Hierarchy attendue :
///   Canvas
///     InventoryUI (ce script)
///       RightColumn
///         BtnBackpack   (Button + Image)
///         BtnPurse      (Button + Image)
///       InventoryPanel  (deployable, desactive au depart)
///         SlotsGrid     (GridLayoutGroup 4 colonnes)
///           Slot_0..7   (prefab SlotUI)
///       GoldText        (TextMeshPro)
/// </summary>
public class InventoryUI : MonoBehaviour
{
    public static InventoryUI Instance { get; private set; }

    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------

    [Header("=== References UI ===")]
    [SerializeField] private GameObject _inventoryPanel;
    [SerializeField] private Transform _slotsGrid;
    [SerializeField] private GameObject _slotPrefab;
    [SerializeField] private TextMeshProUGUI _goldText;

    [Header("=== Boutons Colonne ===")]
    [SerializeField] private Button _btnBackpack;
    [SerializeField] private Button _btnPurse;

    [Header("=== Sprites ===")]
    [SerializeField] private Sprite _spriteBackpack;
    [SerializeField] private Sprite _spritePurse;

    [Header("=== Animation Deploiement ===")]
    [Tooltip("Duree d'animation d'ouverture/fermeture en secondes")]
    [SerializeField, Range(0.1f, 0.5f)] private float _animDuration = 0.2f;

    [Header("=== Animation Ramassage ===")]
    [Tooltip("Duree du vol de l'objet vers l'icone")]
    [SerializeField, Range(0.2f, 1f)] private float _flyDuration = 0.4f;
    [SerializeField] private Sprite _coinFlySprite;

    [Header("=== Animation Sac (Bounce) ===")]
    [Tooltip("Duree totale du bounce en secondes")]
    [SerializeField, Range(0.1f, 0.6f)] private float _bounceDuration = 0.3f;
    [Tooltip("Hauteur du bounce (scale Y max)")]
    [SerializeField, Range(1.05f, 1.5f)] private float _bounceScaleY = 1.35f;
    [Tooltip("Ecrasement horizontal au pic (scale X min)")]
    [SerializeField, Range(0.6f, 0.95f)] private float _squishScaleX = 0.75f;
    [Tooltip("Nombre de rebonds")]
    [SerializeField, Range(1, 3)] private int _bounceCount = 2;

    // -------------------------------------------------------------------------
    // Etat
    // -------------------------------------------------------------------------

    private bool _panelOpen = false;
    private SlotUI[] _slotUIs;
    private RectTransform _panelRect;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _panelRect = _inventoryPanel?.GetComponent<RectTransform>();

        // Boutons
        _btnBackpack?.onClick.AddListener(ToggleInventory);
        _btnPurse?.onClick.AddListener(TogglePurse);

        // Creer les slots
        BuildSlots();

        // Panel ferme au depart
        if (_inventoryPanel != null) _inventoryPanel.SetActive(false);

        RefreshAll();
        // Marquer pret apres l'initialisation pour eviter le bounce au demarrage
        StartCoroutine(SetReadyNextFrame());
    }

    private System.Collections.IEnumerator SetReadyNextFrame()
    {
        yield return null;
        _inventoryReady = true;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnInventoryChanged>(OnInventoryChanged);
        EventBus.Subscribe<OnGoldChanged>(OnGoldChanged);
        EventBus.Subscribe<OnItemPickedUp>(OnItemPickedUp);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnInventoryChanged>(OnInventoryChanged);
        EventBus.Unsubscribe<OnGoldChanged>(OnGoldChanged);
        EventBus.Unsubscribe<OnItemPickedUp>(OnItemPickedUp);
    }

    // -------------------------------------------------------------------------
    // Construction des slots
    // -------------------------------------------------------------------------

    private void BuildSlots()
    {
        if (_slotsGrid == null || _slotPrefab == null) return;

        // Nettoyer les anciens slots
        foreach (Transform child in _slotsGrid)
            Destroy(child.gameObject);

        int count = Inventory.Instance != null ? Inventory.Instance.SlotCount : 8;
        _slotUIs = new SlotUI[count];

        for (int i = 0; i < count; i++)
        {
            var go = Instantiate(_slotPrefab, _slotsGrid);
            go.name = "Slot_" + i;
            var slot = go.GetComponent<SlotUI>();
            if (slot != null)
            {
                slot.Initialize(i);
                _slotUIs[i] = slot;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Refresh
    // -------------------------------------------------------------------------

    private bool _inventoryReady = false;

    private void OnInventoryChanged(OnInventoryChanged evt)
    {
        RefreshSlots();
        // Ne bounce pas au demarrage, seulement apres un vrai ajout
        if (!_inventoryReady) return;
        if (_btnBackpack != null)
            StartCoroutine(BounceIcon(_btnBackpack.GetComponent<RectTransform>()));
    }
    private void OnGoldChanged(OnGoldChanged evt)
    {
        RefreshGold();
        // Bounce bourse uniquement si gain de pieces (pas au reset)
        if (!_inventoryReady) return;
        if (evt.Amount > 0 && _btnPurse != null)
            StartCoroutine(BounceIcon(_btnPurse.GetComponent<RectTransform>()));
    }

    private void RefreshAll() { RefreshSlots(); RefreshGold(); }

    private void RefreshSlots()
    {
        if (_slotUIs == null || Inventory.Instance == null) return;
        var slots = Inventory.Instance.GetAllSlots();
        for (int i = 0; i < _slotUIs.Length && i < slots.Length; i++)
            _slotUIs[i]?.Refresh(slots[i]);
    }

    private void RefreshGold()
    {
        if (_goldText == null || Inventory.Instance == null) return;
        _goldText.text = Inventory.Instance.Gold.ToString();
    }

    // -------------------------------------------------------------------------
    // Toggle inventaire
    // -------------------------------------------------------------------------

    public void ToggleInventory()
    {
        _panelOpen = !_panelOpen;
        StopAllCoroutines();
        StartCoroutine(AnimatePanel(_panelOpen));
    }

    public void TogglePurse()
    {
        // Pour l'instant : affiche juste le total en notification
        if (Inventory.Instance == null) return;
        EventBus.Publish(new OnNotification
        {
            Message = "Bourse : " + Inventory.Instance.Gold + " pieces d'or",
            Type = NotificationType.Info
        });
    }

    private IEnumerator AnimatePanel(bool open)
    {
        if (_inventoryPanel == null) yield break;

        _inventoryPanel.SetActive(true);
        float elapsed = 0f;
        float startScale = open ? 0f : 1f;
        float endScale = open ? 1f : 0f;

        // Echelle depuis le coin droit
        if (_panelRect != null)
            _panelRect.localScale = new Vector3(startScale, 1f, 1f);

        while (elapsed < _animDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / _animDuration);
            float s = Mathf.Lerp(startScale, endScale, t);
            if (_panelRect != null)
                _panelRect.localScale = new Vector3(s, 1f, 1f);
            yield return null;
        }

        if (_panelRect != null)
            _panelRect.localScale = new Vector3(endScale, 1f, 1f);

        if (!open) _inventoryPanel.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Animation ramassage : objet vole vers icone
    // -------------------------------------------------------------------------

    private void OnItemPickedUp(OnItemPickedUp evt)
    {
        // Position monde -> position ecran
        var cam = Camera.main;
        if (cam == null) return;

        Vector3 screenPos = cam.WorldToScreenPoint(evt.WorldPos);

        // Destination : bourse si or, sac si autre
        RectTransform target = evt.ItemID == ItemID.PieceOr
            ? _btnPurse?.GetComponent<RectTransform>()
            : _btnBackpack?.GetComponent<RectTransform>();

        if (target == null) return;

        StartCoroutine(FlyToTarget(screenPos, target, evt.ItemID));

        // Bounce sur le sac apres le vol
        if (evt.ItemID != ItemID.PieceOr && _btnBackpack != null)
            StartCoroutine(BounceIcon(_btnBackpack.GetComponent<RectTransform>()));
        else if (evt.ItemID == ItemID.PieceOr && _btnPurse != null)
            StartCoroutine(BounceIcon(_btnPurse.GetComponent<RectTransform>()));
    }

    private IEnumerator FlyToTarget(Vector3 startScreen, RectTransform target, ItemID itemID)
    {
        // Creer une image volante temporaire
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) yield break;

        var flyGo = new GameObject("FlyItem");
        flyGo.transform.SetParent(canvas.transform, false);
        var img = flyGo.AddComponent<Image>();

        // Choisir le sprite selon l'item
        img.sprite = itemID == ItemID.PieceOr ? _coinFlySprite : null;
        img.SetNativeSize();

        var flyRect = flyGo.GetComponent<RectTransform>();
        flyRect.sizeDelta = new Vector2(16f, 16f);

        // Convertir startScreen en position canvas
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.GetComponent<RectTransform>(),
            startScreen, canvas.worldCamera,
            out Vector2 startLocal);

        Vector2 endLocal;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.GetComponent<RectTransform>(),
            RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, target.position),
            canvas.worldCamera, out endLocal);

        flyRect.anchoredPosition = startLocal;

        float elapsed = 0f;
        while (elapsed < _flyDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / _flyDuration);
            flyRect.anchoredPosition = Vector2.Lerp(startLocal, endLocal, t);

            // Legende : grossit puis retrecit
            float scale = 1f + Mathf.Sin(t * Mathf.PI) * 0.3f;
            flyRect.localScale = Vector3.one * scale;
            yield return null;
        }

        Destroy(flyGo);

        // Flash sur l'icone cible
        StartCoroutine(FlashTarget(target));
    }

    // -------------------------------------------------------------------------
    // Animation bounce sac
    // -------------------------------------------------------------------------

    private IEnumerator BounceIcon(RectTransform rt)
    {
        if (rt == null) yield break;

        // Petit delai pour que le bounce arrive apres le ramassage
        yield return new WaitForSeconds(0.05f);

        Vector3 originalScale = rt.localScale;
        float stepDuration = _bounceDuration / (_bounceCount * 2f);

        for (int i = 0; i < _bounceCount; i++)
        {
            float intensity = 1f - (float)i / _bounceCount; // diminue a chaque rebond

            // Phase 1 : etirement vers le haut
            float elapsed = 0f;
            while (elapsed < stepDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / stepDuration);
                float sx = Mathf.Lerp(1f, _squishScaleX * intensity + (1f - intensity), t);
                float sy = Mathf.Lerp(1f, _bounceScaleY * intensity + (1f - intensity), t);
                rt.localScale = new Vector3(
                    originalScale.x * sx,
                    originalScale.y * sy,
                    originalScale.z);
                yield return null;
            }

            // Phase 2 : retour a la normale avec leger ecrasement
            elapsed = 0f;
            while (elapsed < stepDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / stepDuration);
                float sx = Mathf.Lerp(_squishScaleX * intensity + (1f - intensity), 1f, t);
                float sy = Mathf.Lerp(_bounceScaleY * intensity + (1f - intensity), 1f, t);
                rt.localScale = new Vector3(
                    originalScale.x * sx,
                    originalScale.y * sy,
                    originalScale.z);
                yield return null;
            }
        }

        // Securite : reset scale exacte
        rt.localScale = originalScale;
    }

    private IEnumerator FlashTarget(RectTransform target)
    {
        var img = target.GetComponent<Image>();
        if (img == null) yield break;
        Color orig = img.color;
        img.color = Color.yellow;
        yield return new WaitForSeconds(0.15f);
        img.color = orig;
    }
}
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// TooltipUI - Texte qui suit la souris et affiche le nom de l'objet survole.
/// Ajouter un seul exemplaire sur le Canvas, desactive au depart.
/// Appeler TooltipUI.Show("texte") et TooltipUI.Hide() depuis SlotUI.
/// </summary>
public class TooltipUI : MonoBehaviour
{
    public static TooltipUI Instance { get; private set; }

    [Header("=== References ===")]
    [SerializeField] private RectTransform _panel;
    [SerializeField] private TextMeshProUGUI _text;

    [Header("=== Decalage souris ===")]
    [SerializeField] private Vector2 _offset = new Vector2(12f, -12f);

    private Canvas _canvas;

    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _canvas = GetComponentInParent<Canvas>();

        // Bloque tous les raycasts sur le tooltip pour eviter le clignotement
        var cg = _panel.gameObject.GetComponent<CanvasGroup>();
        if (cg == null) cg = _panel.gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;

        Hide();
    }

    private void Update()
    {
        if (!_panel.gameObject.activeSelf) return;

        // Position souris via nouveau Input System
        Vector2 mousePos = UnityEngine.InputSystem.Mouse.current != null
            ? UnityEngine.InputSystem.Mouse.current.position.ReadValue()
            : (Vector2)Input.mousePosition;

        Vector2 localPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.GetComponent<RectTransform>(),
            mousePos,
            _canvas.worldCamera,
            out localPos);

        _panel.anchoredPosition = localPos + _offset;
    }

    // -------------------------------------------------------------------------
    // API publique
    // -------------------------------------------------------------------------

    public static void Show(string itemName)
    {
        if (Instance == null) return;
        Instance._text.text = itemName;
        Instance._panel.gameObject.SetActive(true);
    }

    public static void Hide()
    {
        if (Instance == null) return;
        Instance._panel.gameObject.SetActive(false);
    }
}
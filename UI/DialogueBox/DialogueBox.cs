using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.EventSystems;

/// <summary>
/// DialogueBox - Boite de dialogue bas gauche.
/// Approche simple : une seule TextMeshPro avec tout l historique.
/// Scroll natif de ScrollRect avec molette souris.
/// </summary>
public class DialogueBox : MonoBehaviour
{
    public static DialogueBox Instance { get; private set; }

    [Header("=== Icone ===")]
    [SerializeField] private Button _iconButton;
    [SerializeField] private GameObject _badge;

    [Header("=== Panel ===")]
    [SerializeField] private GameObject _panel;
    [SerializeField] private ScrollRect _scrollRect;

    [Header("=== Texte unique ===")]
    [Tooltip("Un seul TextMeshPro qui contient tout l historique")]
    [SerializeField] private TextMeshProUGUI _historyText;
    [Tooltip("RectTransform du Content dans le ScrollRect")]
    [SerializeField] private RectTransform _scrollContent;

    [Header("=== Couleurs ===")]
    [SerializeField] private Color _colorHero = new Color(0.95f, 0.95f, 0.5f);
    [SerializeField] private Color _colorBerger = new Color(0.5f, 0.9f, 0.5f);
    [SerializeField] private Color _colorMouton = new Color(0.6f, 0.7f, 1f);
    [SerializeField] private Color _colorAldric = new Color(1f, 0.75f, 0.2f);
    [SerializeField] private Color _colorDefault = new Color(0.8f, 0.8f, 0.8f);

    private bool _isOpen = false;
    private bool _isTyping = false;
    private string _fullHistory = "";

    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _iconButton?.onClick.AddListener(TogglePanel);
        if (_panel != null) _panel.SetActive(false);
        if (_badge != null) _badge.SetActive(false);
        if (_historyText != null) _historyText.text = "";
    }

    // -------------------------------------------------------------------------
    // API
    // -------------------------------------------------------------------------

    [Header("=== Vitesse frappe ===")]
    [Tooltip("Caracteres par seconde - vitesse normale")]
    [SerializeField, Range(10f, 200f)] private float _typeSpeedNormal = 40f;
    [Tooltip("Caracteres par seconde - clic gauche enfonce")]
    [SerializeField, Range(100f, 2000f)] private float _typeSpeedFast = 500f;

    private Queue<(string speaker, string text)> _msgQueue = new();

    public void ShowMessage(string speaker, string text)
    {
        _msgQueue.Enqueue((speaker, text));

        // Ouvrir le panel si ferme
        if (!_isOpen)
            OpenPanel();

        // Toujours montrer le badge
        if (_badge != null) _badge.SetActive(true);

        // Lancer la queue si pas deja en cours
        if (!_isTyping)
            StartCoroutine(ProcessQueue());
    }

    // -------------------------------------------------------------------------
    // Queue et Typewriter
    // -------------------------------------------------------------------------

    private IEnumerator ProcessQueue()
    {
        while (_msgQueue.Count > 0)
        {
            var (speaker, text) = _msgQueue.Dequeue();

            string colorHex = ColorUtility.ToHtmlStringRGB(GetColor(speaker));
            string prefix = "<color=#" + colorHex + "><b>" + speaker + "</b></color> : ";
            string newLine = _fullHistory.Length > 0 ? "\n" : "";
            string fullLine = newLine + prefix + text;
            int startIdx = _fullHistory.Length;
            _fullHistory += fullLine;

            yield return StartCoroutine(TypeLine(startIdx, _fullHistory));
        }
    }

    private bool _wasAccelerating = false; // Clic tenu pendant le typewriter

    private IEnumerator TypeLine(int startIdx, string snapshot)
    {
        _isTyping = true;
        _wasAccelerating = false;

        if (_historyText == null) { _isTyping = false; yield break; }

        // Afficher tout le texte, TMP gere les tags
        _historyText.text = snapshot;
        _historyText.ForceMeshUpdate();

        // Forcer le scroll en bas APRES le rebuild du layout
        yield return StartCoroutine(ScrollBottomNextFrame());

        int totalVisible = _historyText.textInfo.characterCount;
        int visibleBefore = CountVisibleChars(snapshot, startIdx);

        for (int i = visibleBefore; i <= totalVisible; i++)
        {
            _historyText.maxVisibleCharacters = i;

            // Forcer recalcul layout puis scroll bas a chaque caractere
            Canvas.ForceUpdateCanvases();
            if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 0f;

            if (i < totalVisible)
            {
                bool fast = UnityEngine.InputSystem.Mouse.current != null
                         && UnityEngine.InputSystem.Mouse.current.leftButton.isPressed;
                if (fast) _wasAccelerating = true;
                float speed = fast ? _typeSpeedFast : _typeSpeedNormal;
                yield return new WaitForSeconds(1f / speed);
            }
        }

        _historyText.text = _fullHistory;
        _historyText.ForceMeshUpdate();
        _historyText.maxVisibleCharacters = int.MaxValue;
        Canvas.ForceUpdateCanvases();
        if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 0f;

        _isTyping = false;
    }

    /// Compte les caracteres visibles (hors balises rich text) avant position pos
    private int CountVisibleChars(string text, int pos)
    {
        int visible = 0;
        bool inTag = false;
        for (int i = 0; i < Mathf.Min(pos, text.Length); i++)
        {
            if (text[i] == '<') { inTag = true; continue; }
            if (text[i] == '>') { inTag = false; continue; }
            if (!inTag) visible++;
        }
        return visible;
    }

    // -------------------------------------------------------------------------
    // Panel
    // -------------------------------------------------------------------------

    public void TogglePanel()
    {
        // Ignorer si on vient de relacher le clic d acceleration
        if (_wasAccelerating)
        {
            _wasAccelerating = false;
            return;
        }
        if (_isOpen) ClosePanel();
        else OpenPanel();
    }

    private void OpenPanel()
    {
        _isOpen = true;
        if (_panel != null) _panel.SetActive(true);
        if (_badge != null) _badge.SetActive(false);
        StartCoroutine(ScrollBottomNextFrame());
    }

    private void ClosePanel()
    {
        _isOpen = false;
        if (_panel != null) _panel.SetActive(false);
    }

    private void ScrollBottom()
    {
        if (_scrollRect == null) return;

        // Forcer la hauteur du content a correspondre au texte
        if (_scrollContent != null && _historyText != null)
        {
            float textHeight = _historyText.preferredHeight;
            _scrollContent.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical, textHeight);
        }

        Canvas.ForceUpdateCanvases();
        _scrollRect.verticalNormalizedPosition = 0f;
        Canvas.ForceUpdateCanvases();
    }

    private IEnumerator ScrollBottomNextFrame()
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        ScrollBottom();
    }

    // -------------------------------------------------------------------------
    // Couleurs
    // -------------------------------------------------------------------------

    private Color GetColor(string speaker)
    {
        string s = speaker.ToLower();
        if (s.Contains("berger")) return _colorBerger;
        if (s.Contains("mouton")) return _colorMouton;
        if (s.Contains("aldric")) return _colorAldric;
        if (s.Contains("heros") || s.Contains("hero")) return _colorHero;
        return _colorDefault;
    }
}
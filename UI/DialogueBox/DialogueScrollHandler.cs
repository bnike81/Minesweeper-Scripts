using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[RequireComponent(typeof(ScrollRect))]
public class DialogueScrollHandler : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField, Range(0.001f, 1f)] private float _scrollSpeed = 1f;

    private ScrollRect _scrollRect;
    private bool _mouseOver = false;

    // Bloque la grille des que la souris est sur le panel
    public static bool MouseOverDialogue { get; private set; } = false;

    private void Awake()
    {
        _scrollRect = GetComponent<ScrollRect>();
    }

    private void Update()
    {
        if (!_mouseOver || _scrollRect == null) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) < 0.01f) return;

        _scrollRect.verticalNormalizedPosition =
            Mathf.Clamp01(_scrollRect.verticalNormalizedPosition
                + scroll * _scrollSpeed);
    }

    public void OnPointerEnter(PointerEventData e)
    {
        _mouseOver = true;
        MouseOverDialogue = true;
    }

    public void OnPointerExit(PointerEventData e)
    {
        _mouseOver = false;
        MouseOverDialogue = false;
    }
}
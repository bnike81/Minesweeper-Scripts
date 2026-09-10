using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;

/// <summary>
/// CampfireNPC - Feu de camp cliquable.
/// Eteint par defaut. S'allume pendant la cuisson via CampfireUI.
/// </summary>
public class CampfireNPC : MonoBehaviour,
    IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("=== Sprites ===")]
    [SerializeField] private Sprite _spriteOff;
    [SerializeField] private Sprite _spriteOn1;
    [SerializeField] private Sprite _spriteOn2;

    [Header("=== Animation Flamme ===")]
    [SerializeField, Range(0.05f, 0.5f)] private float _flameSpeed = 0.15f;
    [SerializeField, Range(0f, 0.15f)] private float _bobAmplitude = 0.04f;
    [SerializeField, Range(1f, 8f)] private float _bobSpeed = 4f;

    // Etat
    private bool _isLit = false;
    private bool _frame1 = true;
    private SpriteRenderer _sr;
    private Color _origColor;

    // -------------------------------------------------------------------------

    private bool _discovered = false;

    private void Start()
    {
        // Chercher SpriteRenderer sur ce GO ou ses enfants
        _sr = GetComponent<SpriteRenderer>();
        if (_sr == null) _sr = GetComponentInChildren<SpriteRenderer>();

        if (_sr == null)
            Debug.LogError("[CampfireNPC] SpriteRenderer introuvable sur " + gameObject.name + " !");
        else
            Debug.Log("<color=#FF8800>[CampfireNPC]</color> SpriteRenderer trouve. Sprites: Off="
                + (_spriteOff != null ? _spriteOff.name : "NULL")
                + " On1=" + (_spriteOn1 != null ? _spriteOn1.name : "NULL"));

        _origColor = _sr != null ? _sr.color : Color.white;

        // Toujours eteint au depart
        if (_sr != null && _spriteOff != null)
            _sr.sprite = _spriteOff;
    }

    // -------------------------------------------------------------------------
    // Animations
    // -------------------------------------------------------------------------

    private IEnumerator FlameAnimation()
    {
        while (_isLit)
        {
            if (_sr != null)
            {
                // Si On2 absent, oscille entre On1 et Off
                Sprite next = _frame1
                    ? (_spriteOn1 != null ? _spriteOn1 : _spriteOff)
                    : (_spriteOn2 != null ? _spriteOn2 : _spriteOn1);
                _sr.sprite = next;
            }
            _frame1 = !_frame1;
            yield return new WaitForSeconds(_flameSpeed);
        }
        if (_sr != null && _spriteOff != null) _sr.sprite = _spriteOff;
    }

    private IEnumerator BobAnimation()
    {
        float t = 0f;
        while (_isLit)
        {
            t += Time.deltaTime * _bobSpeed;
            // Pulse de couleur sur le sprite - GO reste fixe
            if (_sr != null)
            {
                float pulse = 0.9f + Mathf.Sin(t) * 0.1f;
                _sr.color = new Color(
                    _origColor.r,
                    _origColor.g * pulse,
                    _origColor.b * pulse,
                    _origColor.a);
            }
            yield return null;
        }
        if (_sr != null) _sr.color = _origColor;
    }

    // -------------------------------------------------------------------------
    // Interactions
    // -------------------------------------------------------------------------

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (GameManager.Instance == null || !GameManager.Instance.IsPlaying) return;

        // Dialogue de decouverte au premier clic
        if (!_discovered)
        {
            _discovered = true;
            CampSpawner.Instance?.OnCampDiscovered();
        }

        CampfireUI.Instance?.OpenUI();
    }


    private bool IsHeroAdjacent()
    {
        var hero = HeroController.Instance;
        if (hero == null) return true; // Pas de heros = mode libre
        float cs = GridManager.Instance?.CellStep ?? 1.05f;
        int hx = Mathf.RoundToInt(hero.transform.position.x / cs);
        int hy = Mathf.RoundToInt(hero.transform.position.y / cs);
        int nx = Mathf.RoundToInt(transform.position.x / cs);
        int ny = Mathf.RoundToInt(transform.position.y / cs);
        return Mathf.Max(Mathf.Abs(hx - nx), Mathf.Abs(hy - ny)) <= 1;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        TooltipUI.Show(_isLit ? "Feu de camp - En train de cuire" : "Feu de camp - Cliquer pour cuire");
    }

    public void OnPointerExit(PointerEventData eventData) => TooltipUI.Hide();

    // -------------------------------------------------------------------------
    // API publique
    // -------------------------------------------------------------------------

    public void Light()
    {
        if (_isLit) return;
        _isLit = true;
        if (_sr != null && _spriteOn1 != null) _sr.sprite = _spriteOn1;
        StopAllCoroutines();
        StartCoroutine(FlameAnimation());
        StartCoroutine(BobAnimation());
        Debug.Log("<color=#FF8800>[CampfireNPC]</color> Feu allume !");
    }

    public void Extinguish()
    {
        _isLit = false;
        StopAllCoroutines();
        if (_sr != null)
        {
            _sr.color = _origColor;
            if (_spriteOff != null) _sr.sprite = _spriteOff;
        }
        Debug.Log("<color=#FF8800>[CampfireNPC]</color> Feu eteint.");
    }
}
using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;

/// <summary>
/// GroundItem - Objet pose au sol, cliquable pour ramasser.
/// </summary>
public class GroundItem : MonoBehaviour
{
    private ItemData _itemData;
    private int _quantity;

    // Proprietes publiques pour HeroController
    public ItemData ItemData => _itemData;
    public int Quantity => _quantity;
    private bool _collected = false;

    [SerializeField] private float _bobAmplitude = 0.05f;
    [SerializeField] private float _bobSpeed = 2f;

    private Vector3 _startPos;
    private SpriteRenderer _sr;

    // -------------------------------------------------------------------------

    private int _gridX, _gridY;

    public void Initialize(ItemData data, int quantity)
    {
        _itemData = data;
        _quantity = quantity;
        _startPos = transform.position;
        _sr = GetComponent<SpriteRenderer>();

        // Calculer la case grille et s'enregistrer
        var gm = GridManager.Instance;
        if (gm != null)
        {
            _gridX = Mathf.RoundToInt(transform.position.x / gm.CellStep);
            _gridY = Mathf.RoundToInt(transform.position.y / gm.CellStep);
            LootSystem.RegisterGroundItem(_gridX, _gridY);
        }

        StartCoroutine(BobRoutine());
    }

    /// <summary>Relance le bob quand l'objet est réactivé (retour de indoor).</summary>
    private void OnEnable()
    {
        if (_startPos != Vector3.zero) // déjà initialisé
        {
            _startPos = new Vector3(transform.position.x, _startPos.y, transform.position.z);
            StartCoroutine(BobRoutine());
        }
    }

    private void OnDisable()
    {
        StopAllCoroutines();
    }

    // -------------------------------------------------------------------------
    // Clic -> ramassage
    // -------------------------------------------------------------------------

    // Desactive - la collecte est geree par HeroController.CheckInteraction
    // public void OnPointerClick(PointerEventData eventData)
    private void OnPointerClick_DISABLED(PointerEventData eventData)
    {
        // Protection stricte : ignore tout si deja collecte
        if (_collected) return;
        if (eventData.button != PointerEventData.InputButton.Left) return;

        Collect();
    }

    /// <summary>Cache l objet immediatement, supprime apres animation</summary>
    public void HideImmediate()
    {
        StopAllCoroutines();
        if (_sr != null) _sr.enabled = false;
        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;
    }

    public void Collect()
    {
        if (_collected) return;
        _collected = true;
        // Se d�senregistrer du registre LootSystem
        LootSystem.UnregisterGroundItem(_gridX, _gridY);
        HideImmediate();

        // Desactive immediatement le collider ET le composant IPointerClick
        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        // Ajoute a l'inventaire MAINTENANT (une seule fois, ici)
        bool added = true; // AddItem gere par HeroController.CheckInteraction
        if (!added) return;

        // Publie l'event pour l'animation UI uniquement (pas d'ajout dans InventoryUI)
        EventBus.Publish(new OnItemPickedUp
        {
            ItemID = _itemData.itemID,
            Quantity = _quantity,
            WorldPos = _startPos
        });

        StopAllCoroutines();
        StartCoroutine(FlyAnimation());
    }

    // -------------------------------------------------------------------------
    // Animation bob
    // -------------------------------------------------------------------------

    private IEnumerator BobRoutine()
    {
        float t = Random.Range(0f, Mathf.PI * 2f);
        while (!_collected)
        {
            t += Time.deltaTime * _bobSpeed;
            transform.position = _startPos
                + new Vector3(0f, Mathf.Sin(t) * _bobAmplitude, 0f);
            yield return null;
        }
    }

    // -------------------------------------------------------------------------
    // Animation vol (visuel uniquement, l'ajout est deja fait)
    // -------------------------------------------------------------------------

    private IEnumerator FlyAnimation()
    {
        Vector3 startPos = transform.position;
        float duration = 0.3f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            transform.position = Vector3.Lerp(startPos, startPos + Vector3.up * 1.5f, t);
            transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 0f, t);
            yield return null;
        }

        Destroy(gameObject);
    }
}
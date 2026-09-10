using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// NPC - Composant generique pour les personnages cliquables.
/// Gere : clic gauche, dialogue, type de PNJ.
/// Attach sur le GameObject NPC spawne par ShepherdFarmSpawner.
/// </summary>
public class NPC : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public enum NPCType
    {
        Shepherd,   // Vieux berger - demarre la quete
        Sheep,      // Mouton egare - compte pour la quete
    }

    [Header("=== Type de PNJ ===")]
    [SerializeField] private NPCType _npcType = NPCType.Shepherd;

    [Header("=== Identification ===")]
    [Tooltip("Index du mouton (0, 1, 2, 3...) pour le suivi de quete")]
    [SerializeField] private int _sheepIndex = 0;
    public int SheepIndex => _sheepIndex;

    private string _overrideDialogue = null;
    private bool _sheepValidated = false; // Dialogue special (bloque ou arrive)
    private bool _hasArrived = false;

    [Header("=== Sprites ===")]
    [SerializeField] private SpriteRenderer _spriteRenderer;
    [SerializeField] private Sprite _shepherdSprite;
    [SerializeField] private Sprite _sheepSprite;

    [Header("=== Label au dessus ===")]
    [Tooltip("TextMeshPro world space au dessus du sprite (enfant du prefab)")]
    [SerializeField] private TextMeshPro _nameLabel;
    [Tooltip("Decalage vertical au dessus du sprite")]
    [SerializeField] private float _labelOffsetY = 0.7f;

    [Header("=== Dialogue UI ===")]
    [Tooltip("Duree d'affichage du dialogue en secondes")]
    [SerializeField] private float _dialogueDuration = 4f;

    private bool _hasBeenFound = false;  // Pour les moutons : deja trouve ?
    private bool _firstTalk = true;   // Pour le berger : premier dialogue ?

    // -------------------------------------------------------------------------
    // Initialisation
    // -------------------------------------------------------------------------

    public void Initialize(NPCType type, int sheepIndex = 0)
    {
        _npcType = type;
        _sheepIndex = sheepIndex;

        if (_spriteRenderer == null)
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        RefreshSprite();
        SetupLabel();
    }

    private void SetupLabel()
    {
        if (_nameLabel == null) return;

        // Positionner au dessus du sprite
        _nameLabel.transform.localPosition = new Vector3(0f, _labelOffsetY, -0.1f);

        // Texte selon le type
        switch (_npcType)
        {
            case NPCType.Shepherd:
                _nameLabel.text = "Vieux berger";
                break;
            case NPCType.Sheep:
                _nameLabel.text = "Mouton egare...";
                break;
        }

        // Cache au depart
        _nameLabel.gameObject.SetActive(false);
    }

    private void RefreshSprite()
    {
        if (_spriteRenderer == null) return;
        _spriteRenderer.sprite = _npcType == NPCType.Shepherd
            ? _shepherdSprite
            : _sheepSprite;
    }

    // -------------------------------------------------------------------------
    // Tooltip survol
    // -------------------------------------------------------------------------

    public void OnPointerEnter(PointerEventData eventData)
    {
        ShowLabel();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        HideLabel();
    }

    private void ShowLabel()
    {
        if (_nameLabel == null) return;
        _nameLabel.gameObject.SetActive(true);
    }

    private void HideLabel()
    {
        if (_nameLabel == null) return;
        _nameLabel.gameObject.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Clic gauche
    // -------------------------------------------------------------------------

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        var gm = GameManager.Instance;
        if (gm == null || !gm.IsPlaying) return;

        // Verifier que le heros est adjacent (dist Chebyshev <= 1)
        var hero = HeroController.Instance;
        if (hero != null)
        {
            float cs = GridManager.Instance?.CellStep ?? 1.05f;
            int hx = Mathf.RoundToInt(hero.transform.position.x / cs);
            int hy = Mathf.RoundToInt(hero.transform.position.y / cs);
            int nx = Mathf.RoundToInt(transform.position.x / cs);
            int ny = Mathf.RoundToInt(transform.position.y / cs);
            int dist = Mathf.Max(Mathf.Abs(hx - nx), Mathf.Abs(hy - ny));

            if (dist > 1)
            {
                // Heros trop loin - lui demander de s approcher
                // HeroController gere le deplacement vers le NPC
                return;
            }
        }

        switch (_npcType)
        {
            case NPCType.Shepherd:
                HandleShepherdClick();
                break;

            case NPCType.Sheep:
                HandleSheepClick();
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Berger
    // -------------------------------------------------------------------------

    /// <summary>Appele par HeroController quand il arrive adjacent au NPC.</summary>
    /// <summary>Valide le mouton dans la quete s il etait deja trouve.</summary>
    public void ValidateSheepIfFound()
    {
        if (_npcType != NPCType.Sheep) return;
        if (!_hasBeenFound) return; // Pas encore trouve - pas de validation

        // Eviter double validation
        var qm = QuestManager.Instance;
        if (qm == null) return;

        // FindSheep est idempotent si deja compte ? Non - on track localement
        if (!_sheepValidated)
        {
            _sheepValidated = true;
            qm.FindSheep(_sheepIndex);
        }
    }

    public void TriggerInteraction()
    {
        var gm = GameManager.Instance;
        if (gm == null || !gm.IsPlaying) return;

        switch (_npcType)
        {
            case NPCType.Shepherd: HandleShepherdClick(); break;
            case NPCType.Sheep: HandleSheepClick(); break;
        }
    }

    private void HandleShepherdClick()
    {
        var qm = QuestManager.Instance;
        if (qm == null) return;

        if (_firstTalk && !qm.ShepherdTalkedTo)
        {
            _firstTalk = false;
            qm.TalkToShepherd();
        }
        else
        {
            // Clics suivants : dialogue de suivi
            string dialogue = qm.GetShepherdDialogue();
            ShowDialogue(dialogue);
        }
    }

    // -------------------------------------------------------------------------
    // Mouton
    // -------------------------------------------------------------------------

    /// <summary>Assigne un dialogue de mouton bloque.</summary>
    public void SetStuckDialogue(string text)
    {
        _overrideDialogue = text;
    }

    /// <summary>Assigne un dialogue de mouton arrive a la ferme.</summary>
    public void SetArrivedDialogue(string text)
    {
        _overrideDialogue = text;
        _hasArrived = true;
    }

    private void HandleSheepClick()
    {
        var sheep = GetComponent<SheepBehaviour>();

        // Mouton arrive a la ferme - dialogue special + validation quete
        if (_hasArrived)
        {
            if (_overrideDialogue != null)
                ShowDialogue(_overrideDialogue);

            // Valider dans la quete si pas encore fait
            if (!_hasBeenFound)
            {
                _hasBeenFound = true;
                QuestManager.Instance?.FindSheep(_sheepIndex);
            }
            return;
        }

        // Mouton bloque - au reclic, tenter de relancer le retour
        if (sheep != null && sheep.IsStuck)
        {
            // Essayer de repartir maintenant que le joueur a peut-etre libere le passage
            bool restarted = sheep.TryRestartReturn();
            if (restarted)
            {
                ShowDialogue("Ah ! Je crois que je vois par ou passer maintenant !");
                _overrideDialogue = null; // Effacer le dialogue bloque
            }
            else if (_overrideDialogue != null)
            {
                ShowDialogue(_overrideDialogue);
            }
            return;
        }

        if (_hasBeenFound) return;

        var qm = QuestManager.Instance;
        if (qm == null || !qm.QuestStarted)
        {
            ShowDialogue("Beeeh... (va parler au vieux berger d'abord !)");
            return;
        }

        // Mouton trouve - lui demander de rentrer mais ne PAS valider encore
        // La validation se fait quand il arrive a la ferme (_hasArrived)
        _hasBeenFound = true;
        string dialogue = qm.GetSheepDialogue();
        ShowDialogue(dialogue);

        // Declencher le retour - la quete sera validee a l arrivee
        if (sheep != null)
            sheep.TriggerReturnToFarm();
        else
            StartCoroutine(FadeOutAndDestroy(1.5f));
    }

    // -------------------------------------------------------------------------
    // Affichage dialogue
    // -------------------------------------------------------------------------

    private void ShowDialogue(string text)
    {
        string speaker = _npcType switch
        {
            NPCType.Shepherd => "Vieux Berger",
            NPCType.Sheep => "Mouton",
            _ => "?"
        };

        // Boite de dialogue
        DialogueBox.Instance?.ShowMessage(speaker, text);

        // Notification HUD en complement
        EventBus.Publish(new OnNotification
        {
            Message = text,
            Type = NotificationType.Info
        });
    }

    // -------------------------------------------------------------------------
    // Disparition mouton trouve
    // -------------------------------------------------------------------------

    private System.Collections.IEnumerator FadeOutAndDestroy(float duration)
    {
        if (_spriteRenderer == null) yield break;

        float elapsed = 0f;
        Color startColor = _spriteRenderer.color;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - (elapsed / duration);
            _spriteRenderer.color = new Color(
                startColor.r, startColor.g, startColor.b, alpha);
            yield return null;
        }

        Destroy(gameObject);
    }
}
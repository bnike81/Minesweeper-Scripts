using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// QuestManager - Gere les quetes actives du jeu.
/// Phase 1.5 : quete du vieux berger (retrouver les moutons egares).
/// </summary>
public class QuestManager : MonoBehaviour
{
    public static QuestManager Instance { get; private set; }

    [Header("=== Quete du Berger ===")]
    [Tooltip("Nombre total de moutons a retrouver (repartis sur niveaux 1 et 2)")]
    [SerializeField] private int _totalSheep = 4;

    [Tooltip("Moutons presents au niveau 1 (le reste sera au niveau 2)")]
    [SerializeField] private int _sheepOnLevel1 = 2;

    [Tooltip("Recompense drapeaux bonus quand la quete est terminee")]
    [SerializeField] private int _flagReward = 3;

    [Header("=== Etat (lecture seule) ===")]
    [SerializeField, ReadOnly] private int _sheepFound = 0;
    [SerializeField, ReadOnly] private bool _questStarted = false;
    [SerializeField, ReadOnly] private bool _questComplete = false;
    [SerializeField, ReadOnly] private bool _shepherdTalkedTo = false;

    public int TotalSheep => _totalSheep;
    public int SheepOnLevel1 => _sheepOnLevel1;
    public int SheepOnLevel2 => _totalSheep - _sheepOnLevel1;
    public int SheepFound => _sheepFound;
    public bool QuestStarted => _questStarted;
    public bool QuestComplete => _questComplete;
    public bool ShepherdTalkedTo => _shepherdTalkedTo;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable() { EventBus.Subscribe<OnRunStarted>(OnRunStarted); }
    private void OnDisable() { EventBus.Unsubscribe<OnRunStarted>(OnRunStarted); }

    private void OnRunStarted(OnRunStarted evt) => ResetQuest();

    public void ResetQuest()
    {
        _sheepFound = 0;
        _questStarted = false;
        _questComplete = false;
        _shepherdTalkedTo = false;
    }

    // --- Interactions --------------------------------------------------------

    /// <summary>
    /// Appele quand le joueur clique sur le vieux berger pour la premiere fois.
    /// Demarre la quete et publie une notification.
    /// </summary>
    public void TalkToShepherd()
    {
        // Lire le dialogue AVANT de changer l etat
        string dialogue = GetShepherdDialogue();

        _shepherdTalkedTo = true;
        _questStarted = true;
        DialogueBox.Instance?.ShowMessage("Vieux Berger", dialogue);
        EventBus.Publish(new OnNotification
        {
            Message = dialogue,
            Type = NotificationType.Info
        });

        Debug.Log("<color=#FFCC44>[QuestManager]</color> Quete demarree : retrouver "
            + _totalSheep + " moutons !");
    }

    /// <summary>
    /// Appele quand le joueur decouvre une case mouton egare.
    /// </summary>
    public void FindSheep(int sheepIndex)
    {
        if (_questComplete) return;

        _sheepFound++;

        // Le berger fait les comptes - pas les moutons
        int remaining = _totalSheep - _sheepFound;
        string shepherdReaction;

        if (_sheepFound == 1)
            shepherdReaction = "En voila un de rentre ! Il en manque encore "
                + remaining + " !";
        else if (_sheepFound < _totalSheep)
            shepherdReaction = _sheepFound + " sur " + _totalSheep
                + " de rentres... encore " + remaining
                + " a trouver, continue !";
        else
            shepherdReaction = "Le dernier ! Mon troupeau est au complet !";

        DialogueBox.Instance?.ShowMessage("Vieux Berger", shepherdReaction);
        EventBus.Publish(new OnNotification
        {
            Message = shepherdReaction,
            Type = NotificationType.Info
        });

        EventBus.Publish(new OnXPGained { Amount = 20 });

        if (_sheepFound >= _totalSheep)
            CompleteQuest();
    }

    private void CompleteQuest()
    {
        _questComplete = true;

        Debug.Log("<color=#FFCC44>[QuestManager]</color> Quete terminee ! Recompense accordee.");

        // Recompense drapeaux
        var stats = PlayerStats.Instance;
        if (stats != null)
        {
            stats.AddBonusFlags(_flagReward);
        }

        string rewardMsg = "Quete terminee ! +" + _flagReward + " drapeaux et un bon fromage !";
        DialogueBox.Instance?.ShowMessage("Vieux Berger", rewardMsg);
        EventBus.Publish(new OnNotification
        {
            Message = rewardMsg,
            Type = NotificationType.LevelUp
        });
    }

    // --- Dialogues du berger -------------------------------------------------

    public string GetShepherdDialogue()
    {
        if (!_shepherdTalkedTo)
        {
            return "Hep ! Toi la ! Mes moutons se sont encore echappes !\n" +
                   "Ce vieux berger que je suis n'arrive plus a les suivre...\n" +
                   "Tu peux les retrouver ? Y en a " + _totalSheep + " d'egares !";
        }

        if (_questComplete)
        {
            return "Merci a toi ! Tiens, un bon fromage de ma fabrication !\n" +
                   "Et quelques drapeaux pour marquer les mauvaises herbes...";
        }

        int remaining = _totalSheep - _sheepFound;
        string[] enCours = new string[]
        {
            "Alors alors ? T'as vu mes moutons ?! Il en reste " + remaining + " !",
            "Ces betes betes ! Il en manque encore " + remaining + " !",
            "Depeche toi ! Ces moutons vont finir perdus dans les bois ! " + remaining + " manquent !",
        };
        return enCours[Random.Range(0, enCours.Length)];
    }

    public string GetSheepDialogue()
    {
        string[] dialogues = new string[]
        {
            "Beeeh... je m'etais juste un peu promene...",
            "Beeeh ! Tu n'es pas le loup toi ?",
            "Beeeh beeeh ! La prairie etait si belle par ici...",
            "Beeeh... Le vieux berger m'avait encore oublie sa gamelle !",
        };
        return dialogues[Random.Range(0, dialogues.Length)];
    }
}
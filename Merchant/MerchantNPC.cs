using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// MerchantNPC - Marchand ambulant Aldric.
/// Clic gauche -> dialogue puis ouvre la boutique.
/// </summary>
public class MerchantNPC : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    private bool _firstTalk = true;
    private bool _shopOpen = false;

    // -------------------------------------------------------------------------

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        var gm = GameManager.Instance;
        if (gm == null || !gm.IsPlaying) return;

        // Verifier que le heros est adjacent
        if (!IsHeroAdjacent()) return;

        if (_firstTalk)
        {
            _firstTalk = false;
            ShowDialogue(GetFirstDialogue());
            // Petit delai puis ouvre la boutique
            StartCoroutine(OpenShopDelayed());
        }
        else
        {
            OpenShop();
        }
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
        TooltipUI.Show("Aldric - Marchand des routes");
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        TooltipUI.Hide();
    }

    // -------------------------------------------------------------------------

    private System.Collections.IEnumerator OpenShopDelayed()
    {
        yield return new WaitForSeconds(2.5f);
        OpenShop();
    }

    private void OpenShop()
    {
        if (ShopUI.Instance != null)
            ShopUI.Instance.OpenShop();
    }

    private void ShowDialogue(string text)
    {
        DialogueBox.Instance?.ShowMessage("Aldric", text);
        EventBus.Publish(new OnNotification
        {
            Message = text,
            Type = NotificationType.Info
        });
    }

    private string GetFirstDialogue()
    {
        string[] dialogues = new string[]
        {
            "Hola voyageur ! Bien tombe ! Je suis Aldric, marchand des routes. Qu'est-ce qui me vaut l'honneur ?",
            "Ah, un aventurier ! Ca fait du bien de voir du monde par ici ! Je suis Aldric. Tu cherches quelque chose ?",
            "Par les routes du royaume ! Un voyageur ! Je suis Aldric. Mes prix sont les plus honnetes du coin... presque !",
        };
        return dialogues[Random.Range(0, dialogues.Length)];
    }

    public string GetIdleDialogue()
    {
        string[] dialogues = new string[]
        {
            "Aldric : Tu reviens ! Toujours content de servir !",
            "Aldric : Ah mon meilleur client ! Que puis-je faire pour toi ?",
            "Aldric : Ces routes sont dangereuses... heureusement que je suis la !",
        };
        return dialogues[Random.Range(0, dialogues.Length)];
    }
}
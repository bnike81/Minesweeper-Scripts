using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// CampfireUI - Interface de cuisson du feu de camp.
/// Drag and drop depuis inventaire. Cuisson temps reel.
/// </summary>
public class CampfireUI : MonoBehaviour
{
    public static CampfireUI Instance { get; private set; }

    [Header("=== Panel ===")]
    [SerializeField] private GameObject _panel;
    [SerializeField] private Button _btnClose;
    [SerializeField] private Button _btnCook;
    [SerializeField] private TextMeshProUGUI _statusText;

    [Header("=== Feu de Camp ===")]
    [SerializeField] private CampfireNPC _campfireNPC;

    [Header("=== Slots ===")]
    [SerializeField] private CookingSlot _slotInput;
    [SerializeField] private CookingSlot _slotFuel;
    [SerializeField] private CookingSlot _slotOutput;

    [Header("=== Barres ===")]
    [SerializeField] private Image _cookBar;
    [SerializeField] private Image _fuelBar;
    [SerializeField] private TextMeshProUGUI _cookBarText;
    [SerializeField] private TextMeshProUGUI _fuelBarText;

    [Header("=== Recette ===")]
    [SerializeField, Range(1, 6)] private int _branchesPerSteak = 3;
    [SerializeField, Range(1f, 15f)] private float _cookTimePerBranch = 5f;

    [Header("=== Items ===")]
    [SerializeField] private ItemData _itemSteackCru;
    [SerializeField] private ItemData _itemSteackCuit;
    [SerializeField] private ItemData _itemBranche;

    // Etat
    private bool _isCooking = false;
    private int _steaksQueued = 0;
    private int _branchesQueued = 0;
    private int _steaksCooked = 0;
    private int _branchesUsed = 0;

    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (_panel != null) _panel.SetActive(false);
    }

    private void Start()
    {
        _btnClose?.onClick.AddListener(CloseUI);
        _btnCook?.onClick.AddListener(StartCooking);
        if (_cookBar != null) _cookBar.fillAmount = 0f;
        if (_fuelBar != null) _fuelBar.fillAmount = 1f;


    }

    // -------------------------------------------------------------------------
    // Ouverture / Fermeture
    // -------------------------------------------------------------------------

    public void OpenUI()
    {
        if (_panel != null) _panel.SetActive(true);
        RefreshSlots();

        if (_isCooking)
            SetStatus("Cuisson en cours - " + _steaksCooked + "/" + _steaksQueued + " steack(s) cuit(s)");
        else
            SetStatus("Glisse un steack et des branches pour cuire !");
    }

    public void CloseUI()
    {
        // Ferme juste le panel - la cuisson continue en arriere-plan
        if (_panel != null) _panel.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Helpers NPC
    // -------------------------------------------------------------------------

    private CampfireNPC GetNPC()
    {
        if (_campfireNPC != null) return _campfireNPC;

        // Cherche parmi tous les CampfireNPC celui avec un SpriteRenderer
        var all = FindObjectsOfType<CampfireNPC>();
        foreach (var npc in all)
        {
            var sr = npc.GetComponent<SpriteRenderer>();
            if (sr == null) sr = npc.GetComponentInChildren<SpriteRenderer>();
            if (sr != null) { _campfireNPC = npc; break; }
        }
        if (_campfireNPC == null && all.Length > 0)
            _campfireNPC = all[0];

        Debug.Log("<color=#FF8800>[CampfireUI]</color> NPC trouve: "
            + (_campfireNPC != null ? _campfireNPC.gameObject.name : "NULL"));
        return _campfireNPC;
    }

    // -------------------------------------------------------------------------
    // Cuisson
    // -------------------------------------------------------------------------

    public void StartCooking()
    {
        if (_isCooking) return;

        int steaks = _slotInput?.Quantity ?? 0;
        int branches = _slotFuel?.Quantity ?? 0;

        if (steaks <= 0) { SetStatus("Pas de steack a cuire !"); return; }
        if (branches < _branchesPerSteak) { SetStatus("Il faut " + _branchesPerSteak + " branches par steack !"); return; }

        int steaksPossible = branches / _branchesPerSteak;
        int steaksToCook = Mathf.Min(steaks, steaksPossible);

        _steaksQueued = steaksToCook;
        _branchesQueued = steaksToCook * _branchesPerSteak;
        _steaksCooked = 0;
        _branchesUsed = 0;

        GetNPC()?.Light();
        SetStatus("Cuisson de " + steaksToCook + " steack(s)...");
        StartCoroutine(CookRoutine());
    }

    private IEnumerator CookRoutine()
    {
        _isCooking = true;
        if (_btnCook != null) _btnCook.interactable = false;

        while (_steaksCooked < _steaksQueued)
        {
            // Cuire 1 steack = branchesPerSteak branches
            for (int b = 0; b < _branchesPerSteak; b++)
            {
                _branchesUsed++;
                float elapsed = 0f;
                float cookStart = (float)b / _branchesPerSteak;

                while (elapsed < _cookTimePerBranch)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / _cookTimePerBranch;

                    if (_fuelBar != null) _fuelBar.fillAmount = 1f - t;
                    if (_cookBar != null) _cookBar.fillAmount = cookStart + t / _branchesPerSteak;

                    if (_fuelBarText != null)
                        _fuelBarText.text = (_branchesQueued - _branchesUsed + 1) + " branche(s)";
                    if (_cookBarText != null)
                        _cookBarText.text = _steaksCooked + "/" + _steaksQueued;

                    yield return null;
                }
                if (_fuelBar != null) _fuelBar.fillAmount = 1f;
            }

            // Steack cuit !
            _steaksCooked++;
            if (_slotOutput != null && _itemSteackCuit != null)
                _slotOutput.SetItem(_itemSteackCuit, _steaksCooked);

            if (_cookBar != null) _cookBar.fillAmount = 0f;
            SetStatus(_steaksCooked + "/" + _steaksQueued + " steack(s) cuit(s) !");

            // Flash vert
            if (_cookBar != null)
            {
                _cookBar.color = Color.green;
                yield return new WaitForSeconds(0.2f);
                _cookBar.color = new Color(1f, 0.5f, 0f);
            }
        }

        FinishCooking();
    }

    private void FinishCooking()
    {
        _isCooking = false;
        GetNPC()?.Extinguish();
        if (_btnCook != null) _btnCook.interactable = true;

        // Retirer les items utilises des slots
        _slotInput?.RemoveQuantity(_steaksCooked);
        _slotFuel?.RemoveQuantity(_steaksCooked * _branchesPerSteak);
        // Les steacks cuits restent dans slotOutput - joueur doit les glisser

        SetStatus("Cuisson terminee ! Glisse les steacks dans ton sac !");
        EventBus.Publish(new OnNotification
        {
            Message = _steaksCooked + " steack(s) cuit(s) - recupère-les !",
            Type = NotificationType.Item
        });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void ReturnItemsToInventory()
    {
        int steaks = _slotInput?.Quantity ?? 0;
        int branches = _slotFuel?.Quantity ?? 0;
        int cooked = _slotOutput?.Quantity ?? 0;

        if (steaks > 0 && _itemSteackCru != null) Inventory.Instance?.AddItem(_itemSteackCru, steaks);
        if (branches > 0 && _itemBranche != null) Inventory.Instance?.AddItem(_itemBranche, branches);
        if (cooked > 0 && _itemSteackCuit != null) Inventory.Instance?.AddItem(_itemSteackCuit, cooked);

        _slotInput?.Clear();
        _slotFuel?.Clear();
        _slotOutput?.Clear();
    }

    private void RefreshSlots()
    {
        _slotInput?.RefreshVisual();
        _slotFuel?.RefreshVisual();
        _slotOutput?.RefreshVisual();
    }

    private void SetStatus(string msg)
    {
        if (_statusText != null) _statusText.text = msg;
    }
}
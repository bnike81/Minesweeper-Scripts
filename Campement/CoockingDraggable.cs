using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// CookingDraggable - Ghost de drag depuis un CookingSlot vers l'inventaire.
/// Quand droppé sur un SlotUI, ajoute l'item à l'inventaire et vide le slot source.
/// </summary>
public class CookingDraggable : MonoBehaviour
{
    public ItemData ItemData { get; private set; }
    public int Quantity { get; private set; }
    public CookingSlot SourceSlot { get; private set; }

    public void Setup(ItemData item, int qty, CookingSlot source)
    {
        ItemData = item;
        Quantity = qty;
        SourceSlot = source;
    }

    /// <summary>Appelé par SlotUI.OnDrop quand le drop réussit</summary>
    public void OnDropped()
    {
        // Vider le slot source
        SourceSlot?.Clear();
    }
}
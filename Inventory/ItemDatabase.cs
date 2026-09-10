using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ItemDatabase - ScriptableObject listant tous les ItemData du jeu.
/// Creer via Assets -> Create -> MinesweeperRPG -> ItemDatabase
/// Assigner sur le GameObject Inventory dans l'Inspector.
/// </summary>
[CreateAssetMenu(fileName = "ItemDatabase", menuName = "MinesweeperRPG/ItemDatabase")]
public class ItemDatabase : ScriptableObject
{
    [SerializeField] private List<ItemData> _items = new();

    private Dictionary<ItemID, ItemData> _lookup;

    public void Initialize()
    {
        _lookup = new Dictionary<ItemID, ItemData>();
        foreach (var item in _items)
        {
            if (item == null) continue;
            if (_lookup.ContainsKey(item.itemID))
            {
                Debug.LogWarning("[ItemDatabase] Doublon : " + item.itemID);
                continue;
            }
            _lookup[item.itemID] = item;
        }
        Debug.Log("<color=#AAFFAA>[ItemDatabase]</color> " + _lookup.Count + " objets charges.");
    }

    public ItemData Get(ItemID id)
    {
        if (_lookup == null) Initialize();
        _lookup.TryGetValue(id, out var result);
        if (result == null)
            Debug.LogWarning("[ItemDatabase] ItemID introuvable : " + id);
        return result;
    }
}
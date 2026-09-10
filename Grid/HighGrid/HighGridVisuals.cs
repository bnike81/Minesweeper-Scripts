using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// HighGridVisuals — Visuels du plateau montagneux commun.
/// Tree layers (trunk/canopy/cime) + ground.
/// </summary>
public class HighGridVisuals : MonoBehaviour
{
    [Header("=== Sprites Arbres ===")]
    [SerializeField] private Sprite _treeTrunkSprite;
    [SerializeField] private Sprite _treeCanopySprite;
    [SerializeField] private Sprite _treeCimeSprite;

    [Header("=== Sprite Ground ===")]
    [SerializeField] private Sprite _groundSprite;

    [Header("=== Sorting ===")]
    [SerializeField] private string _groundSortingLayer = "Ground";
    [SerializeField] private int _groundSortingOrder = 0;
    [SerializeField] private string _treeSortingLayer = "CellContent";
    [SerializeField] private int _treeSortingOrder = 3;

    private HighGrid _highGrid;
    private float _cellStep;
    private Dictionary<(int, int), SpriteRenderer> _groundRenderers = new();
    private Dictionary<(int, int), SpriteRenderer> _treeRenderers = new();

    public void Initialize(HighGrid highGrid)
    {
        _highGrid = highGrid;
        _cellStep = highGrid.CellStep;
        CreateVisuals();
    }

    private void CreateVisuals()
    {
        // Nettoyer les anciens
        foreach (Transform child in transform) Destroy(child.gameObject);
        _groundRenderers.Clear();
        _treeRenderers.Clear();

        var cells = _highGrid.GetAllCells();
        int gCount = 0, tCount = 0;

        foreach (var kvp in cells)
        {
            int wx = kvp.Key.Item1;
            int wy = kvp.Key.Item2;
            Vector3 pos = new Vector3(wx * _cellStep, wy * _cellStep, 0f);

            // Ground
            if (_groundSprite != null)
            {
                var gGO = new GameObject($"HG_{wx}_{wy}");
                gGO.transform.SetParent(transform);
                gGO.transform.position = pos;
                var gsr = gGO.AddComponent<SpriteRenderer>();
                gsr.sprite = _groundSprite;
                gsr.sortingLayerName = _groundSortingLayer;
                gsr.sortingOrder = _groundSortingOrder;
                _groundRenderers[(wx, wy)] = gsr;
                gCount++;
            }

            // Arbre avec tree layer
            Sprite treeSpr = GetTreeLayerSprite(wx, wy, cells);
            if (treeSpr != null)
            {
                var tGO = new GameObject($"HT_{wx}_{wy}");
                tGO.transform.SetParent(transform);
                tGO.transform.position = pos;
                var tsr = tGO.AddComponent<SpriteRenderer>();
                tsr.sprite = treeSpr;
                tsr.sortingLayerName = _treeSortingLayer;
                tsr.sortingOrder = _treeSortingOrder;
                _treeRenderers[(wx, wy)] = tsr;
                tCount++;
            }
        }

        Debug.Log($"[HighGridVisuals] {gCount} grounds, {tCount} arbres créés");
    }

    [Header("=== Sprite Buisson ===")]
    [SerializeField] private Sprite _treeBuissonSprite;

    private Sprite GetTreeLayerSprite(int wx, int wy,
        Dictionary<(int, int), Cell> cells)
    {
        bool belowExists = cells.ContainsKey((wx, wy - 1));
        bool aboveExists = cells.ContainsKey((wx, wy + 1));

        // Même logique que GridManager tree layers
        if (belowExists && aboveExists) return _treeCanopySprite;     // milieu
        if (!belowExists && !aboveExists) return _treeBuissonSprite;  // isolé
        if (!belowExists) return _treeTrunkSprite;                     // bas
        if (!aboveExists) return _treeCimeSprite;                      // haut
        return _treeCanopySprite;
    }

    // Les arbres et le ground sont TOUJOURS visibles.
    // Le fog est une couche séparée par-dessus qui les masque visuellement.
    // Pas de UpdateTreeVisibility — le sorting order fait le travail.
}
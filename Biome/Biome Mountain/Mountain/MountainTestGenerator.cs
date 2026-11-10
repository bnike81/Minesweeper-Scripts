using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// MountainTestGenerator — Test en mode Edit avec Gizmos.
///
/// [ContextMenu "Test Generate Mountain"] pour générer sans jouer.
/// Dessine les pièces dans la Scene View avec couleurs par type.
///
/// Placer sur un GO dans la scène MainGrid pour tester.
/// </summary>
public class MountainTestGenerator : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private MountainRecipe _recipe;
    [SerializeField] private int _baseX = 8;
    [SerializeField] private int _baseY = 10;
    [SerializeField] private int _gridWidth = 16;

    [Header("Mode Spawner (simule le calcul de MountainSpawner)")]
    [Tooltip("Activer pour calculer baseY comme MountainSpawner le fait en jeu")]
    [SerializeField] private bool _simulateSpawner = false;
    [Tooltip("Hauteur de la grille simulée (gm.Height)")]
    [SerializeField] private int _simulatedGridHeight = 40;

    private MountainWFCGenerator _wfc;

    [ContextMenu("Test Generate Mountain")]
    public void TestGenerate()
    {
        int bx = _baseX;
        int by = _baseY;
        int gw = _gridWidth;

        if (_simulateSpawner)
        {
            // Reproduire exactement le calcul de MountainSpawner
            int ext = _recipe.spawnAtLevel >= 4 ? (_recipe.spawnAtLevel - 3) * 16 : 16;
            int totalMtnHeight = _recipe.midTotalHeight + 12; // base(5) + mid + top(~7)
            by = _simulatedGridHeight - ext + ext / 3;
            by = Mathf.Clamp(by, 4, _simulatedGridHeight - totalMtnHeight - 2);
            bx = gw / 2;
            Debug.Log($"[MtnTest] Mode Spawner : baseX={bx} baseY={by} " +
                      $"gridH={_simulatedGridHeight} ext={ext} mtnH={totalMtnHeight}");
        }

        _wfc = new MountainWFCGenerator();
        _wfc.Generate(_recipe, bx, by, gw);
        Debug.Log($"[MtnTest] {_wfc.Placements.Count} pièces générées, sommet Y={_wfc.SommetY}");
    }

    [ContextMenu("Clear")]
    public void Clear() => _wfc = null;

    private void OnDrawGizmosSelected()
    {
        if (_wfc == null || _wfc.Placements.Count == 0) return;

        float cs = 1.063f; // CellStep
        if (GridManager.Instance != null) cs = GridManager.Instance.CellStep;

        foreach (var p in _wfc.Placements)
        {
            int h = MountainPieceData.GetHeight(p.type);
            float wx = p.gridX * cs;
            float wy = p.gridY * cs;

            // Couleur par type
            Gizmos.color = GetColor(p.type);

            // Rectangle de la pièce (1 case large × h cases haut)
            float centerX = wx + cs * 0.5f;
            float centerY = wy + cs * h * 0.5f;
            Gizmos.DrawCube(
                new Vector3(centerX, centerY, 0f),
                new Vector3(cs * 0.85f, cs * h * 0.9f, 0.1f));

            // Label (en mode Scene)
#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                new Vector3(wx, wy + cs * h, 0f),
                p.type.ToString().Replace("PieceType.", ""),
                new GUIStyle { fontSize = 8, normal = { textColor = Color.white } });
#endif
        }
    }

    private Color GetColor(PieceType type) => type switch
    {
        PieceType.AngleL or PieceType.AngleR
            => new Color(0.9f, 0.5f, 0.2f, 0.7f), // orange
        PieceType.AngleTopG or PieceType.AngleTopD
            => new Color(0.9f, 0.8f, 0.2f, 0.7f), // jaune
        PieceType.FaceLeft or PieceType.FaceRight
            => new Color(0.5f, 0.3f, 0.15f, 0.7f), // marron
        PieceType.FaceLeftCave or PieceType.FaceRightCave
            => new Color(0.2f, 0.7f, 0.3f, 0.7f), // vert (cave)
        PieceType.BorderSimpleL or PieceType.BorderSimpleR
            => new Color(0.4f, 0.4f, 0.5f, 0.7f), // gris
        PieceType.BorderDoubleL or PieceType.BorderDoubleR
            => new Color(0.5f, 0.5f, 0.6f, 0.7f), // gris clair
        PieceType.TurnShortL or PieceType.TurnShortR
            => new Color(0.3f, 0.5f, 0.8f, 0.7f), // bleu
        PieceType.TurnLargeL or PieceType.TurnLargeR
            => new Color(0.2f, 0.4f, 0.9f, 0.7f), // bleu foncé
        PieceType.TopEdge
            => new Color(0.8f, 0.8f, 0.2f, 0.7f), // jaune
        _ => new Color(0.5f, 0.5f, 0.5f, 0.5f)
    };
}
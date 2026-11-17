using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ArenaConfig — ScriptableObject qui definit une configuration d arene.
///
/// Chaque asset .asset represente une config testable :
///   - Taille et visuel de la grille
///   - Position de depart du heros
///   - Liste des ennemis a placer
///
/// Les configs se creent via Project → clic droit → Create → Arena → Config.
///
/// CombatArena peut referencer une config dans Inspector. Si assignee, elle
/// override les parametres locaux de CombatArena au BuildArena().
///
/// REUTILISATION :
///   - Dev test : empiler des configs pour couvrir les cas interessants
///   - Tests PlayMode automatises : chaque test charge une config par asset path
///   - Mode Colisée futur : les vagues du Colisée sont des listes de configs
/// </summary>
[CreateAssetMenu(menuName = "Arena/Config", fileName = "ArenaConfig")]
public class ArenaConfig : ScriptableObject
{
    [Header("Metadata")]
    [Tooltip("Nom lisible de la config (debug, UI, etc.)")]
    public string displayName = "Unnamed Config";

    [TextArea(2, 4)]
    [Tooltip("Description libre : objectif, strategie testee, notes.")]
    public string notes = "";

    [Header("Grille")]
    [Min(3)] public int gridWidth = 10;
    [Min(3)] public int gridHeight = 10;
    public float cellStep = 1.05f;
    public float cellVisualSize = 1.0f;
    public Color floorColor = new Color(0.82f, 0.74f, 0.58f);
    public bool centerGridOnZero = true;

    [Header("Héros")]
    public Vector2Int heroStartPos = new Vector2Int(5, 1);

    [Header("Ennemis")]
    public List<EnemySpawnEntry> enemies = new List<EnemySpawnEntry>();

    [System.Serializable]
    public class EnemySpawnEntry
    {
        public CellContent enemyType = CellContent.Enemy_Wolf;
        public Vector2Int position = new Vector2Int(5, 5);
    }

    /// <summary>Validation debug : verifie que les positions sont dans la grille.</summary>
    private void OnValidate()
    {
        // Clamp hero position
        heroStartPos.x = Mathf.Clamp(heroStartPos.x, 0, Mathf.Max(0, gridWidth - 1));
        heroStartPos.y = Mathf.Clamp(heroStartPos.y, 0, Mathf.Max(0, gridHeight - 1));

        // Clamp enemy positions
        if (enemies != null)
        {
            foreach (var e in enemies)
            {
                if (e == null) continue;
                e.position.x = Mathf.Clamp(e.position.x, 0, Mathf.Max(0, gridWidth - 1));
                e.position.y = Mathf.Clamp(e.position.y, 0, Mathf.Max(0, gridHeight - 1));
            }
        }
    }
}
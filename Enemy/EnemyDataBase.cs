using UnityEngine;

/// <summary>
/// EnemyDatabase - ScriptableObject central pour tous les ennemis.
/// 
/// SETUP :
///   1. Project -> Create -> MinesweeperRPG -> Enemy Database
///   2. Configurer les entrees (une par type d ennemi)
///   3. Assigner l asset sur le GO GameManager dans le champ Enemy Database
/// 
/// AJOUTER UN ENNEMI :
///   - Creer son prefab (SpriteRenderer + EnemyInstance configure)
///   - Ajouter une entree ici avec le prefab
///   - Zero code supplementaire
/// </summary>
[CreateAssetMenu(fileName = "EnemyDatabase",
    menuName = "MinesweeperRPG/Enemy Database")]
public class EnemyDatabase : ScriptableObject
{
    public static EnemyDatabase Instance { get; private set; }

    [SerializeField] private EnemyData[] _enemies;

    // Appele automatiquement quand l asset est charge
    private void OnEnable() => Instance = this;

    public EnemyData Get(CellContent type)
    {
        if (_enemies == null) return null;
        foreach (var e in _enemies)
            if (e != null && e.enemyType == type) return e;
        return null;
    }

    // Exposer tous les ennemis pour préchargement dans AssetPreloader
    public EnemyData[] All => _enemies;
}

// ─── Type de comportement general ────────────────────────────────────────────

public enum EnemyBehaviorType
{
    Melee,      // loup, ours, bandit epee — corps a corps
    Ranged,     // archer, goblin lance — attaque a distance
    Magic,      // futur : mage, sorcier
    Passive     // futur : animal neutre qui fuit
}

// ─── Donnees par ennemi ───────────────────────────────────────────────────────

[System.Serializable]
public class EnemyData
{
    [Header("Identite")]
    public CellContent enemyType;
    public string displayName = "Ennemi";

    [Header("Prefab")]
    [Tooltip("GO avec SpriteRenderer + EnemyInstance + Collider")]
    public GameObject prefab;

    [Header("Stats de base")]
    public int maxHP = 3;
    public int attackDmg = 1;
    public int xpReward = 10;

    [Header("Taille")]
    [Tooltip("Nombre de cases occupees : 1 (normal), 2 (large), 4 (boss)")]
    [Range(1, 4)] public int cellSize = 1;

    [Header("Comportement")]
    public EnemyBehaviorType behavior = EnemyBehaviorType.Melee;

    [Tooltip("Portee d attaque : 1 = corps a corps, 2+ = distance")]
    [Range(1, 8)] public int attackRange = 1;

    [Tooltip("Seuil de fuite : 0 = jamais, 0.3 = fuit a 30 pourcent HP")]
    [Range(0f, 1f)] public float fleeThreshold = 0f;

    [Header("Perception et Territoire")]
    [Tooltip("Rayon de vue — au dela l ennemi perd le heros de vue")]
    [Range(1, 10)] public int perceptionRadius = 3;

    [Tooltip("Rayon de ronde quand cache (etat Errant)")]
    [Range(1, 8)] public int territoryRadius = 3;

    [Tooltip("Couleur du fog affiche au hover sur les cases du rayon de perception")]
    public Color perceptionFogColor = new Color(1f, 0f, 0f, 0.15f);

    [Header("Icone grille (case non revelee)")]
    [Tooltip("Sprite affiche sur la case avant que l ennemi apparaisse")]
    public Sprite gridIcon;
}
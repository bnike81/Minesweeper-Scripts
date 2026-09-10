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

    [Header("Stats")]
    public int maxHP = 3;
    public int attackDmg = 1;
    public int xpReward = 10;

    [Header("Icone grille (case non revelee)")]
    [Tooltip("Sprite affiche sur la case avant que l ennemi apparaisse")]
    public Sprite gridIcon;
}
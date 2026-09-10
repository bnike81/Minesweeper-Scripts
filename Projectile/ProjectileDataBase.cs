using UnityEngine;

/// <summary>
/// ProjectileDatabase - ScriptableObject central pour tous les projectiles.
/// Chaque entree definit le comportement visuel et physique d un type de projectile.
/// Utilisable par ennemis, heros, pieges, sorts, etc.
/// 
/// SETUP : Create -> MinesweeperRPG -> Projectile Database
/// </summary>
[CreateAssetMenu(fileName = "ProjectileDatabase",
    menuName = "MinesweeperRPG/Projectile Database")]
public class ProjectileDatabase : ScriptableObject
{
    public static ProjectileDatabase Instance { get; private set; }

    [SerializeField] private ProjectileData[] _projectiles;

    private void OnEnable() => Instance = this;

    public ProjectileData Get(ProjectileType type)
    {
        if (_projectiles == null) return null;
        foreach (var p in _projectiles)
            if (p.type == type) return p;
        return null;
    }
}

// ─── Types de projectiles ─────────────────────────────────────────────────────

public enum ProjectileType
{
    Arrow,       // Fleche standard
    HeroArrow,   // Fleche du heros (futur)
    Grenade,     // Grenade (futur)
    MagicBolt,   // Sort (futur)
}

// ─── Donnees d un projectile ──────────────────────────────────────────────────

[System.Serializable]
public class ProjectileData
{
    [Header("=== Identification ===")]
    public ProjectileType type;
    public string displayName = "Projectile";

    [Header("=== Sprites ===")]
    [Tooltip("Sprite du projectile (oriente vers la droite par defaut)")]
    public Sprite sprite;

    [Header("=== Trajectoire ===")]
    [Tooltip("Hauteur max de l arc en cloche (cases)")]
    public float arcHeightBase = 0.8f;

    [Tooltip("Hauteur min de l arc quand la cible est tres proche")]
    public float arcHeightMin = 0.1f;

    [Tooltip("Distance (cases) a partir de laquelle l arc est maximum")]
    public float arcMaxDistance = 4f;

    [Tooltip("Vitesse de deplacement du projectile (cases/seconde)")]
    public float speed = 8f;

    [Header("=== Rotation ===")]
    [Tooltip("Le sprite pivote pour suivre la direction de deplacement")]
    public bool rotateWithTrajectory = true;

    [Tooltip("Offset de rotation (degres) pour aligner le sprite avec la trajectoire")]
    public float rotationOffset = 0f;

    [Header("=== Impact ===")]
    [Tooltip("Degats infliges a l impact")]
    public int damage = 1;

    [Tooltip("Prefab d effet a l impact (optionnel)")]
    public GameObject impactPrefab;
}
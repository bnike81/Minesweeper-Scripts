using UnityEngine;

/// <summary>
/// CaveSpawner — Conteneur physique d'une cave dans la hiérarchie.
///
/// Chaque CaveSpawner regroupe :
///   - Cells    : les cell views (sol, murs, portails)
///   - Enemies  : les instances d'ennemis spawned
///   - Items    : les items (futur)
///
/// Activer/désactiver le CaveSpawner = activer/désactiver toute la cave.
/// CaveManager.OnCaveReady active le bon CaveSpawner et désactive les autres.
///
/// Inspector :
///   CaveSpawner 1 → CaveConfig montagne 1, CaveIndex=0
///   CaveSpawner 2 → CaveConfig montagne 2, CaveIndex=1
/// </summary>
public class CaveSpawner : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private CaveConfig _config;
    [SerializeField] private MountainRecipe _mountainRecipe;
    [SerializeField] private int _caveIndex = 0;

    // Conteneurs enfants (créés automatiquement)
    public Transform CellsContainer { get; private set; }
    public Transform EnemiesContainer { get; private set; }
    public Transform ItemsContainer { get; private set; }

    // Propriétés publiques
    public CaveConfig Config => _config;
    public MountainRecipe Mountain => _mountainRecipe;
    public int CaveIndex => _caveIndex;

    private void Awake()
    {
        EnsureContainers();
    }

    private void OnEnable()
    {
        CaveManager.Instance?.RegisterSpawner(this);
    }

    private void OnDisable()
    {
        CaveManager.Instance?.UnregisterSpawner(this);
    }

    /// <summary>Crée les GO conteneurs enfants s'ils n'existent pas.</summary>
    public void EnsureContainers()
    {
        if (CellsContainer == null)
        {
            CellsContainer = FindOrCreate($"Cave{_caveIndex}_Cells");
        }
        if (EnemiesContainer == null)
        {
            EnemiesContainer = FindOrCreate($"Cave{_caveIndex}_Enemies");
        }
        if (ItemsContainer == null)
        {
            ItemsContainer = FindOrCreate($"Cave{_caveIndex}_Items");
        }
    }

    private Transform FindOrCreate(string childName)
    {
        var existing = transform.Find(childName);
        if (existing != null) return existing;

        var go = new GameObject(childName);
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;
        return go.transform;
    }

    /// <summary>Détruit toutes les cells views de cette cave.</summary>
    public void ClearCells()
    {
        if (CellsContainer == null) return;
        for (int i = CellsContainer.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying) Destroy(CellsContainer.GetChild(i).gameObject);
            else DestroyImmediate(CellsContainer.GetChild(i).gameObject);
        }
    }

    /// <summary>Détruit tous les ennemis de cette cave.</summary>
    public void ClearEnemies()
    {
        if (EnemiesContainer == null) return;
        for (int i = EnemiesContainer.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying) Destroy(EnemiesContainer.GetChild(i).gameObject);
            else DestroyImmediate(EnemiesContainer.GetChild(i).gameObject);
        }
    }

    /// <summary>Détruit tous les items de cette cave.</summary>
    public void ClearItems()
    {
        if (ItemsContainer == null) return;
        for (int i = ItemsContainer.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying) Destroy(ItemsContainer.GetChild(i).gameObject);
            else DestroyImmediate(ItemsContainer.GetChild(i).gameObject);
        }
    }

    /// <summary>Nettoie tout le contenu de cette cave.</summary>
    public void ClearAll()
    {
        ClearCells();
        ClearEnemies();
        ClearItems();
    }

    // Debug
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0.2f, 0.8f, 0.8f);
        Gizmos.DrawWireCube(transform.position, new Vector3(1.2f, 1.2f, 0.1f));
#if UNITY_EDITOR
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 0.8f,
            $"Cave {_caveIndex}\n{(_mountainRecipe ? _mountainRecipe.name : "no mountain")}");
#endif
    }
}
using UnityEngine;
using UnityEngine.Pool;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// CellViewPool — Pool natif Unity (UnityEngine.Pool.ObjectPool).
/// Remplace Instantiate/Destroy par Get/Release — zéro allocation GC.
/// 
/// SETUP : Créer un GO "CellViewPool" dans la scène avec ce script.
///         Doit s'exécuter AVANT GridManager (Script Execution Order).
/// </summary>
public class CellViewPool : MonoBehaviour
{
    public static CellViewPool Instance { get; private set; }

    private Dictionary<GameObject, ObjectPool<GameObject>> _pools
        = new Dictionary<GameObject, ObjectPool<GameObject>>();

    // ─── Lookup inversé : objet → prefab source ───────────────────────────────
    // Permet Release(obj) sans connaître le prefab (utilisé par ClearVisuals)
    private Dictionary<GameObject, GameObject> _objectToPrefab
        = new Dictionary<GameObject, GameObject>(512);

    private Transform _poolRoot;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        var root = new GameObject("[PoolRoot]");
        root.transform.SetParent(transform);
        _poolRoot = root.transform;
    }

    // ─── API publique ─────────────────────────────────────────────────────────

    /// <summary>
    /// Pré-chauffe le pool étalé sur plusieurs frames.
    /// CORRECTION : utilise Instantiate direct (inactif) au lieu de pool.Get()
    /// pool.Get() activait les objets → visibles brièvement pendant le yield → flash indésirable.
    /// </summary>
    public IEnumerator PrewarmCoroutinePublic(GameObject prefab, int count)
    {
        var pool = GetOrCreatePool(prefab);
        const int perFrame = 16;

        for (int i = 0; i < count; i++)
        {
            // Instancie directement dans le root du pool — reste INACTIF, jamais rendu
            var obj = UnityEngine.Object.Instantiate(prefab, _poolRoot);
            obj.SetActive(false);
            // Release ajoute l'objet à la collection interne du pool (collectionCheck: false)
            pool.Release(obj);

            if ((i + 1) % perFrame == 0)
                yield return null;
        }
    }

    /// <summary>Récupère un objet du pool, activé et repositionné.</summary>
    public GameObject Get(GameObject prefab, Vector3 position, Transform parent)
    {
        var obj = GetOrCreatePool(prefab).Get();
        obj.transform.SetParent(parent, false);
        obj.transform.position = position;
        // Enregistre la relation objet → prefab pour Release(obj) sans prefab
        _objectToPrefab[obj] = prefab;
        return obj;
    }

    /// <summary>
    /// Retourne un objet au pool en connaissant son prefab.
    /// (API historique — toujours valide)
    /// </summary>
    public void Release(GameObject prefab, GameObject obj)
    {
        if (obj == null) return;
        _objectToPrefab.Remove(obj);
        GetOrCreatePool(prefab).Release(obj);
    }

    /// <summary>
    /// Retourne un objet au pool SANS connaître son prefab.
    /// Utilisé par GridManager.ClearVisuals() — aucune dépendance au prefab source.
    /// </summary>
    public void Release(GameObject obj)
    {
        if (obj == null) return;

        if (_objectToPrefab.TryGetValue(obj, out var prefab))
        {
            _objectToPrefab.Remove(obj);
            GetOrCreatePool(prefab).Release(obj);
        }
        else
        {
            // L'objet n'a pas été créé via ce pool (ex: spawné avant l'init du pool)
            Debug.LogWarning($"[CellViewPool] Release(obj) : objet non suivi → Destroy fallback ({obj.name})");
            Destroy(obj);
        }
    }

    /// <summary>Vide tous les pools (ex: changement de scène).</summary>
    public void ClearAll()
    {
        foreach (var pool in _pools.Values)
            pool.Clear();
        _objectToPrefab.Clear();
        _pools.Clear();
    }

    // ─── Privé ────────────────────────────────────────────────────────────────

    private ObjectPool<GameObject> GetOrCreatePool(GameObject prefab)
    {
        if (_pools.TryGetValue(prefab, out var existing))
            return existing;

        var capturedPrefab = prefab;
        var capturedRoot = _poolRoot;

        var pool = new ObjectPool<GameObject>(
            createFunc: () => Instantiate(capturedPrefab, capturedRoot),
            actionOnGet: obj => obj.SetActive(true),
            actionOnRelease: obj =>
            {
                obj.SetActive(false);
                obj.transform.SetParent(capturedRoot, false);
            },
            // Quand le pool est plein et détruit un objet → nettoie le lookup
            actionOnDestroy: obj =>
            {
                _objectToPrefab.Remove(obj);
                Destroy(obj);
            },
            collectionCheck: false,
            defaultCapacity: 300,
            maxSize: 1000
        );

        _pools[prefab] = pool;
        return pool;
    }
}
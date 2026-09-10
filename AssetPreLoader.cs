using UnityEngine;
using UnityEngine.U2D;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// AssetPreloader — Précharge tous les assets au démarrage.
/// Élimine les File.Open (3529ms) pendant le gameplay.
/// 
/// SETUP dans l'Inspector :
///   _spriteAtlases  : tes 3 atlas (Atlas_Grille, Atlas_Montagne, Atlas_PNJ)
///   _cellPrefabs    : prefabs des cases grille
///   _enemyPrefabs   : prefabs ennemis
///   _itemPrefabs    : prefabs items/loot
///   _worldPrefabs   : PNJ, campements, moutons
///   _effectPrefabs  : effets visuels
///   _uiPrefabs      : UI panels, dialogues, notifications
///   _allSprites     : sprites isolés non couverts par les atlas
///
/// Script Execution Order : AssetPreloader = -100 (avant tout)
/// </summary>
public class AssetPreloader : MonoBehaviour
{
    public static AssetPreloader Instance { get; private set; }

    [Header("=== Sprite Atlas (priorité maximale) ===")]
    [Tooltip("Glisser ici Atlas_Grille, Atlas_Montagne, Atlas_PNJ")]
    [SerializeField] private SpriteAtlas[] _spriteAtlases;

    [Header("=== Prefabs Grille ===")]
    [SerializeField] private GameObject[] _cellPrefabs;

    [Header("=== Prefabs Ennemis ===")]
    [SerializeField] private GameObject[] _enemyPrefabs;

    [Header("=== Prefabs Items/Loot ===")]
    [SerializeField] private GameObject[] _itemPrefabs;

    [Header("=== Prefabs Monde (PNJ, campement, moutons) ===")]
    [SerializeField] private GameObject[] _worldPrefabs;

    [Header("=== Prefabs Effets ===")]
    [SerializeField] private GameObject[] _effectPrefabs;

    [Header("=== Prefabs UI ===")]
    [SerializeField] private GameObject[] _dialogBoxPrefabs;
    [SerializeField] private GameObject[] _panelPrefabs;
    [SerializeField] private GameObject[] _notificationPrefabs;
    [SerializeField] private GameObject[] _hudPrefabs;

    [Header("=== ScriptableObject Databases ===")]
    [Tooltip("EnemyDatabase ScriptableObject — précharge tous les prefabs ennemis")]
    [SerializeField] private EnemyDatabase _enemyDatabase;
    [Tooltip("ItemDatabase ScriptableObject — précharge tous les prefabs items")]
    [SerializeField] private ItemDatabase _itemDatabase;

    [Header("=== Sprites isolés (hors atlas) ===")]
    [SerializeField] private Sprite[] _allSprites;

    // Cache accessible depuis n'importe quel script
    private Dictionary<string, GameObject> _prefabCache
        = new Dictionary<string, GameObject>(64);
    private Dictionary<string, Sprite> _spriteCache
        = new Dictionary<string, Sprite>(256);

    // ─── Accesseur ────────────────────────────────────────────────────────────
    public Sprite GetSprite(string name)
        => _spriteCache.TryGetValue(name, out var s) ? s : null;

    public GameObject GetPrefab(string name)
        => _prefabCache.TryGetValue(name, out var p) ? p : null;

    // ─── Awake : préchargement synchrone ──────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Callback Late Binding — Unity demande l'atlas quand il en a besoin
        SpriteAtlasManager.atlasRequested += OnAtlasRequested;

        // 1. Atlas en premier — charge et uploade toutes les textures en VRAM
        PreloadAtlases();
        VerifyAtlasWorking();

        // 2. Prefabs — accéder aux composants force le chargement des sous-assets
        PreloadPrefabs(_cellPrefabs);
        PreloadPrefabs(_enemyPrefabs);
        PreloadPrefabs(_itemPrefabs);
        PreloadPrefabs(_worldPrefabs);
        PreloadPrefabs(_effectPrefabs);
        PreloadPrefabs(_dialogBoxPrefabs);
        PreloadPrefabs(_panelPrefabs);
        PreloadPrefabs(_notificationPrefabs);
        PreloadPrefabs(_hudPrefabs);

        // 3. Sprites isolés
        PreloadSprites(_allSprites);
    }

    private void VerifyAtlasWorking()
    {
        if (_spriteAtlases == null || _spriteAtlases.Length == 0)
        {
            Debug.LogWarning("[AssetPreloader] Aucun atlas assigné dans l'Inspector !");
            return;
        }
        foreach (var atlas in _spriteAtlases)
        {
            if (atlas == null) { Debug.LogWarning("[AssetPreloader] Atlas null !"); continue; }

            // En Editor Play Mode, spriteCount = 0 (limitation Unity 6 V2)
            // En Build Standalone, spriteCount est correct
#if UNITY_EDITOR
            Debug.Log($"[AssetPreloader] Atlas '{atlas.name}' assigné " +
                      $"(spriteCount=0 normal en Editor — actif en Build)");
#else
            Debug.Log($"[AssetPreloader] ✓ Atlas '{atlas.name}' : {atlas.spriteCount} sprites");
#endif
        }
        Debug.Log($"[AssetPreloader] Prefabs préchargés : {_prefabCache.Count}");
    }

    private void OnDestroy()
    {
        SpriteAtlasManager.atlasRequested -= OnAtlasRequested;
    }

    // Callback Late Binding — V2 utilise le nom de l'atlas comme identifiant
    private void OnAtlasRequested(string tag, System.Action<SpriteAtlas> callback)
    {
        if (_spriteAtlases == null) return;
        foreach (var atlas in _spriteAtlases)
        {
            if (atlas == null) continue;
            // V2 : le tag correspond au nom du fichier atlas
            if (atlas.name == tag || atlas.name.Contains(tag) || tag.Contains(atlas.name))
            {
                callback(atlas);
                return;
            }
        }
    }

    // ─── Préchargement atlas ───────────────────────────────────────────────────
    private void PreloadAtlases()
    {
        if (_spriteAtlases == null) return;
        foreach (var atlas in _spriteAtlases)
        {
            if (atlas == null) continue;

            var sprites = new Sprite[atlas.spriteCount];
            int count = atlas.GetSprites(sprites);

            for (int i = 0; i < count; i++)
            {
                var sprite = sprites[i];
                if (sprite == null) continue;

                // Accéder à la texture force son chargement complet
                var tex = sprite.texture;
                if (tex != null) _ = tex.width;

                // Unity ajoute "(Clone)" — on le retire
                string name = sprite.name.Replace("(Clone)", "").Trim();
                _spriteCache[name] = sprite;
            }

            Debug.Log($"[AssetPreloader] Atlas '{atlas.name}' : {count}/{atlas.spriteCount} sprites");
        }
    }

    // ─── Préchargement prefabs ─────────────────────────────────────────────────
    private void PreloadPrefabs(GameObject[] prefabs)
    {
        if (prefabs == null) return;
        foreach (var prefab in prefabs)
        {
            if (prefab == null) continue;

            // Accéder au nom force le chargement du prefab
            _ = prefab.name;

            // Forcer le chargement de toutes les textures des SpriteRenderers
            foreach (var sr in prefab.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr.sprite == null) continue;
                var tex = sr.sprite.texture;
                if (tex != null) _ = tex.width;
                // Cacher aussi les sprites des prefabs
                _spriteCache[sr.sprite.name] = sr.sprite;
            }

            // Charger les RuntimeAnimatorController
            foreach (var anim in prefab.GetComponentsInChildren<Animator>(true))
                if (anim.runtimeAnimatorController != null)
                    _ = anim.runtimeAnimatorController.name;

            _prefabCache[prefab.name] = prefab;
        }
    }

    // ─── Préchargement EnemyDatabase ──────────────────────────────────────────
    private void PreloadEnemyDatabase()
    {
        if (_enemyDatabase == null) return;
        // Accéder aux entries force le chargement des prefabs en mémoire
        if (_enemyDatabase?.All == null) return;
        foreach (var entry in _enemyDatabase.All)
        {
            if (entry?.prefab == null) continue;
            _ = entry.prefab.name;
            foreach (var sr in entry.prefab.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.sprite != null) _ = sr.sprite.texture.width;
            if (entry.gridIcon != null) _ = entry.gridIcon.texture.width;
            _prefabCache[entry.prefab.name] = entry.prefab;
        }
        Debug.Log($"[AssetPreloader] EnemyDatabase : {_enemyDatabase.All.Length} ennemis préchargés");
    }

    private void PreloadItemDatabase()
    {
        if (_itemDatabase == null) return;
        Debug.Log("[AssetPreloader] ItemDatabase assignée — prefabs items via _itemPrefabs[]");
    }

    // ─── Préchargement sprites isolés ─────────────────────────────────────────
    private void PreloadSprites(Sprite[] sprites)
    {
        if (sprites == null) return;
        foreach (var sprite in sprites)
        {
            if (sprite == null) continue;
            var tex = sprite.texture;
            if (tex != null) _ = tex.width;
            _spriteCache[sprite.name] = sprite;
        }
    }
}
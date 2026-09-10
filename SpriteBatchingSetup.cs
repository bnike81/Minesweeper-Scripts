using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// SpriteBatchingSetup — Force le batching en partageant un seul Material.
/// 
/// Unity ne peut batcher que les SpriteRenderers qui partagent EXACTEMENT
/// le même Material instance. Ce script crée un Material par texture
/// et l'assigne à tous les sprites de la grille.
/// 
/// SETUP : Attacher sur un GO dans la scène.
///         Assigner _defaultSpriteMaterial dans l'Inspector
///         (créer un Material avec shader "Sprites/Default").
/// </summary>
public class SpriteBatchingSetup : MonoBehaviour
{
    public static SpriteBatchingSetup Instance { get; private set; }

    [Header("Material partagé (Sprites/Default)")]
    [Tooltip("Créer un Material avec shader Sprites/Default et l'assigner ici")]
    [SerializeField] private Material _defaultSpriteMaterial;

    // Cache material par texture — 1 material par atlas/spritesheet
    private Dictionary<Texture2D, Material> _materialCache
        = new Dictionary<Texture2D, Material>(8);

    private void OnDestroy()
    {
        // Libérer tous les materials créés — évite fuite mémoire
        foreach (var mat in _materialCache.Values)
            if (mat != null) Destroy(mat);
        _materialCache.Clear();
        if (_defaultSpriteMaterial != null) Destroy(_defaultSpriteMaterial);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Créer le material par défaut si pas assigné
        if (_defaultSpriteMaterial == null)
        {
            _defaultSpriteMaterial = new Material(Shader.Find("Sprites/Default"));
            _defaultSpriteMaterial.enableInstancing = true;
        }

        // Précompiler le shader maintenant — évite spike au premier rendu
        // Crée un quad invisible qui force la compilation du shader
        var warmupGO = new GameObject("ShaderWarmup");
        var sr = warmupGO.AddComponent<SpriteRenderer>();
        sr.sharedMaterial = _defaultSpriteMaterial;
        warmupGO.SetActive(false);
        Destroy(warmupGO, 0.1f);
    }

    /// <summary>
    /// Retourne un Material partagé pour une texture donnée.
    /// Tous les sprites de la même texture partagent le même Material
    /// → Unity peut les batcher automatiquement.
    /// </summary>
    public Material GetOrCreateMaterial(Texture2D tex)
    {
        if (tex == null) return _defaultSpriteMaterial;
        if (_materialCache.TryGetValue(tex, out var mat)) return mat;

        mat = new Material(_defaultSpriteMaterial);
        mat.mainTexture = tex;
        mat.enableInstancing = true;
        _materialCache[tex] = mat;
        return mat;
    }

    /// <summary>
    /// Applique le material partagé à un SpriteRenderer.
    /// Appeler après chaque spawn de CellView.
    /// </summary>
    public void ApplySharedMaterial(SpriteRenderer sr)
    {
        if (sr == null || sr.sprite == null) return;
        sr.sharedMaterial = GetOrCreateMaterial(sr.sprite.texture);
    }
}
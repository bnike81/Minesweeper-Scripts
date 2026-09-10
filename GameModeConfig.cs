using UnityEngine;

/// <summary>
/// GameModeConfig — Configuration du mode de jeu actif.
/// 
/// NORMAL  : paramètres de jeu standard
/// DEBUG   : paramètres développeur pour tester sans contraintes
///           (XP max, niveau ciblé, montagne visible immédiatement)
/// 
/// SETUP : Attacher sur un GO dans la scène.
///         Changer _mode dans l'Inspector avant de lancer.
/// </summary>
public class GameModeConfig : MonoBehaviour
{
    public static GameModeConfig Instance { get; private set; }

    public enum GameMode { Normal, Debug }

    [Header("=== Mode actif ===")]
    [SerializeField] private GameMode _mode = GameMode.Normal;

    [Header("=== Paramètres Debug ===")]
    [Tooltip("Niveau de départ en mode Debug")]
    [SerializeField] private int _debugStartLevel = 1;
    [Tooltip("XP exponent en mode Debug (bas = monter vite)")]
    [SerializeField] private float _debugXpExponent = 0.01f;
    [Tooltip("Ratio de danger en mode Debug")]
    [SerializeField] private float _debugDangerRatio = 0.01f;
    [Tooltip("Spawner montagne immédiatement au niveau cible")]
    [SerializeField] private bool _debugSpawnMountainNow = true;
    [Tooltip("Révéler toute la grille au démarrage")]
    [SerializeField] private bool _debugRevealAll = false;

    // ─── Accesseurs ──────────────────────────────────────────────────────────

    public bool IsDebug => _mode == GameMode.Debug;
    public bool IsNormal => _mode == GameMode.Normal;
    public int DebugStartLevel => _debugStartLevel;
    public float DebugXpExponent => _debugXpExponent;
    public float DebugDangerRatio => _debugDangerRatio;
    public bool DebugSpawnMountain => _debugSpawnMountainNow;
    public bool DebugRevealAll => _debugRevealAll;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (IsDebug)
            Debug.Log($"[GameMode] MODE DEBUG actif — niveau {_debugStartLevel}, " +
                      $"xpExp={_debugXpExponent}, danger={_debugDangerRatio}");
        else
            Debug.Log("[GameMode] MODE NORMAL");
    }

    // ─── Raccourci clavier en jeu ─────────────────────────────────────────────
    private void Update()
    {
        // F1 = basculer mode Debug/Normal (en développement seulement)
#if UNITY_EDITOR
        if (UnityEngine.InputSystem.Keyboard.current?.f1Key.wasPressedThisFrame == true)
        {
            _mode = _mode == GameMode.Normal ? GameMode.Debug : GameMode.Normal;
            Debug.Log($"[GameMode] Basculé → {_mode}");
        }
#endif
    }
}
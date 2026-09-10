using UnityEngine;
using System.Collections;

/// <summary>
/// GameManager — Singleton maître.
/// Gère l'état global du jeu, les transitions, et coordonne les systèmes.
/// Persiste entre les scènes (DontDestroyOnLoad).
/// </summary>
public class GameManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────
    public static GameManager Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("═══ État Initial ═══")]
    [SerializeField] private GameState _initialState = GameState.MainMenu;

    [Header("═══ Configuration Partie ═══")]
    [Tooltip("PV de base du joueur au début d'une run")]
    [SerializeField] private int _basePlayerHP = 5;

    [Tooltip("Niveau de départ")]
    [SerializeField] private int _startLevel = 1;

    [Header("═══ Grille — Niveau 1 ═══")]
    [Tooltip("Largeur de la grille initiale")]
    [SerializeField] private int _startGridWidth = 16;

    [Tooltip("Hauteur de la grille initiale")]
    [SerializeField] private int _startGridHeight = 16;

    [Tooltip("Extension en hauteur à chaque nouveau niveau")]
    [SerializeField] private int _gridHeightExtensionPerLevel = 10;

    [Header("═══ Debug ═══")]
    [SerializeField] private bool _enableDebugLogs = true;

    // ─── État courant ─────────────────────────────────────────────────────────
    private GameState _currentState;

    // ─── Propriétés publiques ─────────────────────────────────────────────────
    public GameState CurrentState => _currentState;
    public int BasePlayerHP => _basePlayerHP;
    public int StartGridWidth => _startGridWidth;
    public int StartGridHeight => _startGridHeight;
    public int GridHeightExtensionPerLevel => _gridHeightExtensionPerLevel;
    public bool IsPlaying => _currentState == GameState.Playing;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _currentState = _initialState;
        Log($"GameManager initialisé — État: {_currentState}");
    }

    private void Start()
    {
        // En build : toujours démarrer sur Playing
        // En Editor : respecter _initialState pour tester les menus
#if UNITY_EDITOR
        if (_initialState == GameState.Playing)
        {
            Log("Lancement automatique (mode test)");
            StartNewRun();
        }
#else
        // Build standalone → démarrer directement le jeu
        Log("Lancement build standalone");
        StartNewRun();
#endif
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            EventBus.Clear();
            Instance = null;
        }
    }

    // ─── Gestion des états ────────────────────────────────────────────────────
    public void ChangeState(GameState newState)
    {
        if (_currentState == newState) return;

        var oldState = _currentState;
        _currentState = newState;

        Log($"État changé : {oldState} → {newState}");
        EventBus.Publish(new OnGameStateChanged { OldState = oldState, NewState = newState });

        HandleStateTransition(oldState, newState);
    }

    private void HandleStateTransition(GameState from, GameState to)
    {
        switch (to)
        {
            case GameState.Playing:
                Time.timeScale = 1f;
                break;

            case GameState.Paused:
                Time.timeScale = 0f;
                break;

            case GameState.GameOver:
                Time.timeScale = 0f;
                EventBus.Publish(new OnRunEnded { Victory = false });
                break;

            case GameState.Victory:
                Time.timeScale = 0f;
                EventBus.Publish(new OnRunEnded { Victory = true });
                break;

            case GameState.LevelTransition:
                Time.timeScale = 1f;
                break;
        }
    }

    // ─── Actions rapides ──────────────────────────────────────────────────────
    public void StartNewRun()
    {
        Log("Démarrage nouvelle run");
        EventBus.Publish(new OnRunStarted());
        ChangeState(GameState.Playing);
    }

    public void PauseGame()
    {
        if (_currentState == GameState.Playing)
            ChangeState(GameState.Paused);
    }

    public void ResumeGame()
    {
        if (_currentState == GameState.Paused)
            ChangeState(GameState.Playing);
    }

    public void TriggerGameOver()
    {
        Log("GAME OVER");
        ChangeState(GameState.GameOver);
    }

    public void TriggerVictory()
    {
        Log("VICTOIRE !");
        ChangeState(GameState.Victory);
    }

    // ─── Utilitaire ───────────────────────────────────────────────────────────
    public void Log(string message)
    {
        if (_enableDebugLogs)
            Debug.Log($"<color=#00FF88>[GameManager]</color> {message}");
    }

    public static void LogWarning(string message)
        => Debug.LogWarning($"<color=#FFAA00>[GameManager]</color> {message}");

    public static void LogError(string message)
        => Debug.LogError($"<color=#FF4444>[GameManager]</color> {message}");
}
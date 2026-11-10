using UnityEngine;
using UnityEngine.SceneManagement;
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
        // En Editor : toutes les scènes sont déjà ouvertes (multi-scene editing)
        // → on peut lancer StartNewRun() directement
#if UNITY_EDITOR
        if (_initialState == GameState.Playing)
        {
            Log("Lancement automatique (mode test)");
            StartNewRun();
        }
#else
        // Build standalone → GameManager gère lui-même la séquence de boot.
        // On charge toutes les scènes additivement, puis on lance le jeu.
        Log("Build standalone — lancement séquence de boot intégrée");
        StartCoroutine(BuildBootSequence());
#endif
    }

    /// <summary>
    /// Séquence de boot pour le build standalone.
    /// Remplace le Bootstrapper (qui peut être absent de la scène Core).
    /// Charge les scènes permanentes + zones, puis lance le jeu via SceneLoader.
    /// </summary>
    private IEnumerator BuildBootSequence()
    {
        Log("BuildBootSequence — début chargement des scènes...");

        // 1. Charger les scènes permanentes
        yield return LoadSceneIfNeeded("UI");
        yield return LoadSceneIfNeeded("Hero");

        // 2. Charger les zones (managers s'initialisent, contenu caché)
        yield return LoadSceneIfNeeded("Underground");
        yield return LoadSceneIfNeeded("Indoor");

        Log("BuildBootSequence — toutes les scènes secondaires chargées");

        // 3. Lancer le jeu : SceneLoader charge MainGrid puis appelle StartNewRun()
        if (SceneLoader.Instance != null)
        {
            Log("BuildBootSequence — appel SceneLoader.StartGame()");
            SceneLoader.Instance.StartGame();
        }
        else
        {
            // Fallback : SceneLoader absent → charger MainGrid directement
            Debug.LogWarning("[GameManager] SceneLoader.Instance est NULL — chargement MainGrid en fallback");
            yield return LoadSceneIfNeeded("MainGrid");

            // Définir MainGrid comme scène active
            var mainScene = SceneManager.GetSceneByName("MainGrid");
            if (mainScene.isLoaded)
                SceneManager.SetActiveScene(mainScene);

            Log("BuildBootSequence — MainGrid chargée en fallback, appel StartNewRun()");
            StartNewRun();
        }

        Log("BuildBootSequence — séquence terminée");
    }

    private IEnumerator LoadSceneIfNeeded(string sceneName)
    {
        if (SceneManager.GetSceneByName(sceneName).isLoaded)
        {
            Log($"Scène déjà chargée : {sceneName}");
            yield break;
        }

        var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        while (op != null && !op.isDone)
            yield return null;

        Log($"Scène chargée : {sceneName}");
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
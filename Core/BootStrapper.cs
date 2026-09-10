using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// Bootstrapper — Point d'entrée unique du jeu.
/// Placé dans la scène Core (la première chargée par Unity).
///
/// Séquence de démarrage :
///   1. Core  est déjà chargée (scène de démarrage dans Build Settings)
///   2. UI    est chargée additivement
///   3. Hero  est chargée additivement
///   4. SceneLoader.StartGame() charge MainGrid additivement
///
/// SETUP BUILD SETTINGS (ordre) :
///   Index 0 : Core        ← scène de démarrage
///   Index 1 : UI
///   Index 2 : Hero
///   Index 3 : MainGrid
///   Index 4 : Underground
///   Index 5 : Indoor
/// </summary>
public class Bootstrapper : MonoBehaviour
{
    [Header("Chargement initial")]
    [Tooltip("Délai d'attente entre chaque chargement de scène (0 = immédiat)")]
    [SerializeField] private float _loadDelay = 0f;

    [Header("Debug")]
    [SerializeField] private bool _skipSplash = true;

    private void Start()
    {
        StartCoroutine(BootSequence());
    }

    private IEnumerator BootSequence()
    {
        Debug.Log("[Bootstrapper] Démarrage séquence de chargement...");

        // Charger les scènes permanentes
        yield return LoadAdditive(SceneLoader.SCENE_UI);
        yield return LoadAdditive(SceneLoader.SCENE_HERO);

        // Charger Underground + Indoor au démarrage
        // Les managers (CaveManager, IndoorManager) sont dans ces scènes.
        // Le contenu visuel est caché par chaque manager dans Awake().
        yield return LoadAdditive(SceneLoader.SCENE_UNDERGROUND);
        yield return LoadAdditive(SceneLoader.SCENE_INDOOR);

        if (_loadDelay > 0f) yield return new WaitForSeconds(_loadDelay);

        // Lancer le jeu (charge MainGrid)
        SceneLoader.Instance?.StartGame();

        Debug.Log("[Bootstrapper] Séquence de démarrage terminée.");
    }

    private IEnumerator LoadAdditive(string sceneName)
    {
        if (SceneManager.GetSceneByName(sceneName).isLoaded)
            yield break;

        var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        while (!op.isDone)
            yield return null;

        Debug.Log($"[Bootstrapper] Scène chargée : {sceneName}");
    }
}
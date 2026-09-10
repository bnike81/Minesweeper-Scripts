using UnityEngine;
using System.Collections;

/// <summary>
/// CaveEnemyBehaviour — Comportement simplifié pour les ennemis de grotte.
///
/// Fournit le mouvement de lunge vers le héros quand l'ennemi attaque.
/// Remplace EnemyBehaviour pour les ennemis cave (pas d'animations complexes).
///
/// Ajouté dynamiquement par UndergroundCellView.SpawnEnemyInstance.
/// </summary>
public class CaveEnemyBehaviour : MonoBehaviour
{
    private EnemyInstance _ei;
    private Vector3 _homePos;

    private void Awake()
    {
        _ei = GetComponent<EnemyInstance>();
    }

    private void Start()
    {
        _homePos = transform.position;
    }

    /// <summary>
    /// Contre-attaque avec mouvement de lunge vers le héros.
    /// Appelé par EnemyInstance après que le héros ait attaqué.
    /// </summary>
    public IEnumerator CounterAttack()
    {
        var hero = HeroController.Instance;
        if (hero == null) yield break;

        _homePos = transform.position;
        Vector3 heroPos = hero.transform.position;

        // Direction vers le héros
        Vector3 dir = (heroPos - _homePos).normalized;
        float lungeDistance = 0.4f;
        Vector3 lungeTarget = _homePos + dir * lungeDistance;

        // Lunge vers le héros (0.15s)
        float t = 0f;
        while (t < 0.15f)
        {
            t += Time.deltaTime;
            transform.position = Vector3.Lerp(_homePos, lungeTarget, t / 0.15f);
            yield return null;
        }

        // Infliger les dégâts au point le plus proche
        if (_ei != null)
            _ei.EnemyAttackDamage();

        // Pause au contact (0.05s)
        yield return new WaitForSeconds(0.05f);

        // Retour à la position d'origine (0.15s)
        t = 0f;
        Vector3 currentPos = transform.position;
        while (t < 0.15f)
        {
            t += Time.deltaTime;
            transform.position = Vector3.Lerp(currentPos, _homePos, t / 0.15f);
            yield return null;
        }

        transform.position = _homePos;
    }
}
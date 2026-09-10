using UnityEngine;
using System.Collections;

/// <summary>
/// ProjectileLauncher - Genere et anime un projectile de from vers to.
/// Utilise ProjectileDatabase pour les parametres visuels.
/// Appele par BanditArcher, Hero (futur), pieges, sorts, etc.
///
/// USAGE :
///   ProjectileLauncher.Fire(ProjectileType.Arrow, from, to, damage, onHit);
/// </summary>
public class ProjectileLauncher : MonoBehaviour
{
    public static ProjectileLauncher Instance { get; private set; }

    [Header("=== Inspector override (optionnel) ===")]
    [Tooltip("Laisser vide pour utiliser ProjectileDatabase")]
    [SerializeField] private ProjectileDatabase _database;

    [Header("=== Debug ===")]
    [SerializeField] private bool _debugTrajectory = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    // =========================================================================
    // API publique
    // =========================================================================

    /// <summary>Lance un projectile depuis worldFrom vers worldTo.</summary>
    public static void Fire(ProjectileType type,
        Vector3 worldFrom, Vector3 worldTo,
        int damage, System.Action onHit = null)
    {
        // Auto-creer si absent de la scene
        if (Instance == null)
        {
            var go = new GameObject("ProjectileLauncher");
            go.AddComponent<ProjectileLauncher>();
            Debug.Log("[Projectile] ProjectileLauncher cree automatiquement.");
        }
        Instance.StartCoroutine(
            Instance.FlyRoutine(type, worldFrom, worldTo, damage, onHit));
    }

    // =========================================================================
    // Coroutine de vol
    // =========================================================================

    private IEnumerator FlyRoutine(ProjectileType type,
        Vector3 from, Vector3 to,
        int damage, System.Action onHit)
    {
        var db = _database ?? ProjectileDatabase.Instance;
        var data = db?.Get(type);

        if (data == null)
        {
            Debug.LogError("[Projectile] Aucune donnee pour " + type
                + " - verifier ProjectileDatabase asset assigné sur ProjectileLauncher");
            onHit?.Invoke();
            yield break;
        }
        if (data.sprite == null)
        {
            Debug.LogError("[Projectile] Sprite null pour " + type
                + " - assigner le sprite dans ProjectileDatabase");
            onHit?.Invoke();
            yield break;
        }

        Debug.Log("[Projectile] Lancement " + type
            + " from=" + from + " to=" + to
            + " sprite=" + (data.sprite != null ? data.sprite.name : "NULL"));

        // Creer le GO projectile
        var go = new GameObject("Projectile_" + type);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = data.sprite;
        // Meme sorting layer que les ennemis pour etre visible
        sr.sortingLayerName = "CellContent"; // Meme layer que les ennemis
        sr.sortingOrder = 15;            // Au dessus des ennemis (order 9)
        var startPos = from;
        startPos.z = -0.5f;         // Devant la grille
        go.transform.position = startPos;
        Debug.Log("[Projectile] GO cree: " + go.name
            + " layer=CellContent order=15");

        float cellStep = GridManager.Instance?.CellStep ?? 1.05f;
        float dist = Vector3.Distance(from, to);
        float distCells = dist / cellStep;

        // Hauteur de l arc en fonction de la distance
        float arcHeight = Mathf.Lerp(
            data.arcHeightMin,
            data.arcHeightBase,
            Mathf.Clamp01(distCells / data.arcMaxDistance)) * cellStep;

        float duration = dist / (data.speed * cellStep);
        float elapsed = 0f;
        bool hitDone = false;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Position interpolee avec arc parabolique
            Vector3 linear = Vector3.Lerp(from, to, t);
            float arc = Mathf.Sin(t * Mathf.PI) * arcHeight;
            Vector3 pos = linear + Vector3.up * arc;
            pos.z = -0.5f; // Maintenir Z devant la grille
            go.transform.position = pos;

            // Rotation du sprite selon la tangente de la trajectoire
            if (data.rotateWithTrajectory && elapsed > 0.01f)
            {
                float nextT = Mathf.Clamp01((elapsed + 0.02f) / duration);
                Vector3 nextLinear = Vector3.Lerp(from, to, nextT);
                float nextArc = Mathf.Sin(nextT * Mathf.PI) * arcHeight;
                Vector3 nextPos = nextLinear + Vector3.up * nextArc;

                Vector3 dir = (nextPos - pos).normalized;
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                // Miroir si va vers la gauche
                if (to.x < from.x)
                {
                    go.transform.rotation = Quaternion.Euler(180f, 0f,
                        -(angle + data.rotationOffset));
                }
                else
                {
                    go.transform.rotation = Quaternion.Euler(0f, 0f,
                        angle + data.rotationOffset);
                }
            }

            yield return null;
        }

        // Arriv e a destination
        go.transform.position = to;

        // Infliger degats
        if (!hitDone)
        {
            hitDone = true;
            ApplyDamage(damage);
            onHit?.Invoke();
        }

        // Effet d impact
        if (data.impactPrefab != null)
            Instantiate(data.impactPrefab, to, Quaternion.identity);

        Destroy(go);
    }

    private void ApplyDamage(int damage)
    {
        if (damage <= 0) return;

        int defense = Equipment.Instance?.Defense ?? 0;
        int finalDmg = Mathf.Max(0, damage - defense);

        if (finalDmg > 0)
            EventBus.Publish(new OnPlayerDamaged
            {
                Damage = finalDmg,
                CurrentHP = 0,
                MaxHP = 0
            });

        EventBus.Publish(new OnNotification
        {
            Message = "Touche par une fleche ! -" + finalDmg + " PV",
            Type = NotificationType.Damage
        });
    }
}
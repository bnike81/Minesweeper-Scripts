using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// EnemyBehaviour - IA et actions des ennemis sur le monde.
/// Futur : loup qui suit, bandit qui fuit, ours qui d�truit arbres, etc.
/// Separe de EnemyInstance (stats/HP) et EnemyAnimator (animations).
/// </summary>
[RequireComponent(typeof(EnemyInstance))]
public class EnemyBehaviour : MonoBehaviour
{
    // =========================================================================
    // Inspector
    // =========================================================================

    [Header("=== Loup ===")]
    [Tooltip("Distance max a laquelle le loup peut suivre le heros (futur)")]
    [SerializeField, Range(1, 8)] private int _wolfFollowRange = 4;

    [Header("=== Ours ===")]
    [Tooltip("L ours peut detruire des arbres adjacents (futur)")]
    [SerializeField] private bool _bearCanBreakTrees = true;

    [Header("=== Bandit Epee ===")]
    [Tooltip("Le bandit fuit si HP < seuil (futur)")]
    [SerializeField, Range(0, 100)] private int _swordFleeHPPercent = 0;

    [Header("=== Archer ===")]
    [Tooltip("Portee de tir de l archer")]
    [SerializeField, Range(2, 6)] private int _archerRange = 4;
    public int ArcherRange => _archerRange;

    // =========================================================================
    // References
    // =========================================================================

    private EnemyInstance _ei;
    private EnemyAnimator _anim;

    private void Awake()
    {
        _ei = GetComponent<EnemyInstance>();
        _anim = GetComponent<EnemyAnimator>();
    }

    // =========================================================================
    // API publique
    // =========================================================================

    /// <summary>Lancer le comportement initial selon le type d ennemi.</summary>
    public void StartBehaviour()
    {
        switch (_ei.EnemyType)
        {
            case CellContent.Enemy_Wolf:
                _anim?.ApplyWolfIdle();
                StartCoroutine(WolfBehaviour());
                break;

            case CellContent.Enemy_BanditSword:
                _anim?.ApplySwordIdle();
                StartCoroutine(BanditSwordBehaviour());
                break;

            case CellContent.Enemy_BanditArcher:
                _anim?.ApplyArcherIdle();
                CheckArcherTrigger();
                break;

            default:
                StartCoroutine(GenericAttackDelay());
                break;
        }
    }

    /// <summary>Riposte apres avoir recu des degats.
    /// Les degats sont inclus dans les animations (frame specifique).
    /// </summary>
    public IEnumerator CounterAttack()
    {
        yield return new WaitForSeconds(0.3f);
        if (_ei.IsDead) yield break;

        switch (_ei.EnemyType)
        {
            case CellContent.Enemy_Wolf:
                // WolfAlertThenAttack inclut les degats a la frame 6
                if (_anim != null)
                {
                    yield return StartCoroutine(_anim.WolfAlertThenAttack());
                }
                else
                {
                    yield return new WaitForSeconds(0.3f);
                }
                break;
            case CellContent.Enemy_BanditSword:
                // BanditSwordAttackAnim inclut les degats a la frame 8
                if (_anim != null)
                {
                    yield return StartCoroutine(_anim.BanditSwordAttackAnim());
                }
                else
                {
                    yield return new WaitForSeconds(0.3f);
                }
                break;
            case CellContent.Enemy_BanditArcher:
                // ArcherAttackAnim tire la fleche a la frame 11
                if (_anim != null)
                {
                    yield return StartCoroutine(_anim.ArcherAttackAnim(FireArrow));
                }
                else
                {
                    yield return new WaitForSeconds(0.3f);
                }
                break;
            default:
                // Vérifier si c'est un ennemi cave avec CaveEnemyBehaviour
                var caveBeh = GetComponent<CaveEnemyBehaviour>();
                if (caveBeh != null)
                {
                    yield return StartCoroutine(caveBeh.CounterAttack());
                }
                else if (_anim != null)
                {
                    yield return StartCoroutine(_anim.AttackLungeAnim());
                    _ei.EnemyAttackDamage();
                }
                else
                {
                    yield return new WaitForSeconds(0.3f);
                    _ei.EnemyAttackDamage();
                }
                break;
        }
    }

    // =========================================================================
    // Archer - detection portee
    // =========================================================================

    private bool _archerSubscribed = false;
    private bool _hasAttacked = false; // Eviter tirs multiples

    public void SubscribeArcherEvents()
    {
        if (_archerSubscribed) return;
        _archerSubscribed = true;
        EventBus.Subscribe<OnCellsRevealed>(OnCellsRevealedArcher);
        Debug.Log("[Archer(" + _ei.GridX + "," + _ei.GridY + ")] Subscribe");

        // Verifier immediatement si une case voisine est deja revelee
        // (cas ou l archer spawn dans une zone adjacente a une zone ouverte)
        var gm = GridManager.Instance;
        if (gm == null) return;
        var dirs = new[] {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right,
            new Vector2Int(1,1), new Vector2Int(-1,1),
            new Vector2Int(1,-1), new Vector2Int(-1,-1)
        };
        foreach (var d in dirs)
        {
            int nx = _ei.GridX + d.x;
            int ny = _ei.GridY + d.y;
            if (nx < 0 || ny < 0 || nx >= gm.Width || ny >= gm.Height) continue;
            var cell = gm.GetCell(nx, ny);
            if (cell != null && cell.IsRevealed)
            {
                // Une case adjacente est deja revelee - verifier portee
                CheckArcherTrigger();
                return;
            }
        }
    }

    private void OnDestroy()
    {
        EventBus.Unsubscribe<OnCellsRevealed>(OnCellsRevealedArcher);
    }

    private void OnCellsRevealedArcher(OnCellsRevealed evt)
    {
        if (_ei.IsDead || _ei.IsAnimating) return;
        var gm = GridManager.Instance;
        var ownCell = gm?.GetCell(_ei.GridX, _ei.GridY);
        if (ownCell == null || !ownCell.IsRevealed) return;

        // Ne pas d�clencher pendant un batch � �vite boucle de r�v�lations
        if (GridManager.Instance != null && GridManager.Instance.IsPublishingBatch) return;

        foreach (var c in evt.Cells)
        {
            int dist = Mathf.Max(
                Mathf.Abs(c.X - _ei.GridX),
                Mathf.Abs(c.Y - _ei.GridY));
            if (dist <= 1)
            {
                CheckArcherTrigger();
                return;
            }
        }
    }

    public void CheckArcherTrigger()
    {
        if (_ei.IsDead || _ei.IsAnimating) return;
        if (_hasAttacked) return; // Deja attaque - attendre contre-attaque du joueur

        var hero = HeroController.Instance;
        if (hero == null) return;

        int dist = Mathf.Max(
            Mathf.Abs(hero.GridPosition.x - _ei.GridX),
            Mathf.Abs(hero.GridPosition.y - _ei.GridY));

        if (dist >= 1 && dist <= _archerRange)
        {
            _hasAttacked = true;
            _ei.UpdateFacing();
            StartCoroutine(ArcherBehaviour());
        }
    }

    private IEnumerator ArcherBehaviour()
    {
        if (_anim != null)
        {
            yield return StartCoroutine(_anim.ArcherAttackAnim(FireArrow));
        }
        else
        {
            yield return new WaitForSeconds(0.3f);
        }
        // Pret pour la prochaine attaque
        _hasAttacked = false;
    }

    private void FireArrow()
    {
        var hero = HeroController.Instance;
        if (hero == null) return;

        int dist = Mathf.Max(
            Mathf.Abs(hero.GridPosition.x - _ei.GridX),
            Mathf.Abs(hero.GridPosition.y - _ei.GridY));
        if (dist > _archerRange) return;

        float cs = GridManager.Instance?.CellStep ?? 1.05f;
        var from = new Vector3(_ei.GridX * cs, _ei.GridY * cs + cs * 0.3f, 0f);
        var to = new Vector3(
            hero.GridPosition.x * cs,
            hero.GridPosition.y * cs + cs * 0.1f, 0f);

        Debug.Log("[Archer] FireArrow dist=" + dist);
        ProjectileLauncher.Fire(ProjectileType.Arrow, from, to, _ei.GetDamage());
    }

    // =========================================================================
    // Comportements existants (loup, bandit)
    // =========================================================================

    private IEnumerator WolfBehaviour()
    {
        if (_anim != null)
        {
            yield return StartCoroutine(_anim.WolfAlertThenAttack());
        }
        else
        {
            yield return new WaitForSeconds(0.3f);
        }
    }

    private IEnumerator BanditSwordBehaviour()
    {
        if (_anim != null)
        {
            yield return StartCoroutine(_anim.BanditSwordAlertThenAttack());
        }
        else
        {
            yield return new WaitForSeconds(0.3f);
        }
    }

    private IEnumerator GenericAttackDelay()
    {
        yield return new WaitForSeconds(0.5f);
        if (!_ei.IsDead) _ei.EnemyAttackDamage();
    }

    // =========================================================================
    // Futurs comportements (squelette)
    // =========================================================================

    // FUTUR - Loup suit le heros
    // private IEnumerator WolfFollow() { ... }

    // FUTUR - Ours d�truit arbres
    // private IEnumerator BearBreakTree() { ... }

    // FUTUR - Bandit fuit
    // private IEnumerator BanditFlee() { ... }
}
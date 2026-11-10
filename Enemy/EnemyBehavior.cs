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
    // Acces donnees centralisees (EnemyDatabase)
    // =========================================================================

    /// <summary>Lit les donnees depuis EnemyDatabase pour ce type d ennemi.</summary>
    private EnemyData GetData()
        => EnemyDatabase.Instance?.Get(_ei.EnemyType);

    /// <summary>Portee d attaque (1 = melee, 2+ = distance). Fallback = 1.</summary>
    public int AttackRange => GetData()?.attackRange ?? 1;

    /// <summary>Seuil de fuite (0 = jamais). Fallback = 0.</summary>
    public float FleeThreshold => GetData()?.fleeThreshold ?? 0f;

    /// <summary>Rayon de perception. Fallback = 3.</summary>
    public int PerceptionRadius => GetData()?.perceptionRadius ?? 3;

    /// <summary>Rayon de territoire (ronde Errant). Fallback = 3.</summary>
    public int TerritoryRadius => GetData()?.territoryRadius ?? 3;

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
        // Si combat tour par tour actif, ne pas lancer les attaques legacy
        // Le TurnManager gerera les attaques via TakeTurn()
        bool combatMode = (GameModeConfig.Instance != null && GameModeConfig.Instance.CombatEnabled)
                       || TurnManager.Instance != null;

        switch (_ei.EnemyType)
        {
            case CellContent.Enemy_Wolf:
                _anim?.ApplyWolfIdle();
                if (!combatMode) StartCoroutine(WolfBehaviour());
                break;

            case CellContent.Enemy_BanditSword:
                _anim?.ApplySwordIdle();
                if (!combatMode) StartCoroutine(BanditSwordBehaviour());
                break;

            case CellContent.Enemy_BanditArcher:
                _anim?.ApplyArcherIdle();
                if (!combatMode) CheckArcherTrigger();
                break;

            default:
                if (!combatMode) StartCoroutine(GenericAttackDelay());
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
                    yield return StartCoroutine(
                        _anim.AttackLungeAnim(() => _ei.EnemyAttackDamage()));
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

        if (dist >= 1 && dist <= AttackRange)
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
        if (dist > AttackRange) return;

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
    // Indicateurs visuels de perception (! et ?)
    // =========================================================================

    // Provisoire : TMPro texte — sera remplace par sprites pixel art
    private GameObject _indicatorGO;

    /// <summary>
    /// Joue l animation alert propre a ce type d ennemi (pose avant combat).
    /// Appelee par TurnManager a l activation, en parallele du "!".
    /// </summary>
    public IEnumerator PlayAlertPose()
    {
        switch (_ei.EnemyType)
        {
            case CellContent.Enemy_Wolf:
                _ei.UpdateFacing();
                if (_anim != null)
                    yield return StartCoroutine(_anim.WolfAlertAnim());
                break;

            case CellContent.Enemy_BanditSword:
                _ei.UpdateFacing();
                if (_anim != null)
                    yield return StartCoroutine(_anim.BanditSwordAlertAnim());
                break;

            // Futurs ennemis : ajouter ici si animation alert distincte

            default:
                yield break;
        }
    }

    /// <summary>Affiche un "!" bondissant au-dessus de l ennemi (detection). Fire-and-forget.</summary>
    public void ShowAlertIndicator()
    {
        StartCoroutine(ShowIndicatorCoroutine("!", new Color(1f, 0.2f, 0.2f)));
    }

    /// <summary>Coroutine "!" — peut etre yieldee pour attendre la fin.</summary>
    public IEnumerator ShowAlertIndicatorAndWait()
    {
        yield return StartCoroutine(
            ShowIndicatorCoroutine("!", new Color(1f, 0.2f, 0.2f)));
    }

    /// <summary>Affiche un "?" au-dessus de l ennemi (perte de vue). Fire-and-forget.</summary>
    public void ShowLostIndicator()
    {
        StartCoroutine(ShowIndicatorCoroutine("?", new Color(0.4f, 0.7f, 1f)));
    }

    /// <summary>Coroutine "?" — peut etre yieldee pour attendre la fin.</summary>
    public IEnumerator ShowLostIndicatorAndWait()
    {
        yield return StartCoroutine(
            ShowIndicatorCoroutine("?", new Color(0.4f, 0.7f, 1f)));
    }

    private IEnumerator ShowIndicatorCoroutine(string symbol, Color color)
    {
        // Detruire l ancien si encore visible
        if (_indicatorGO != null) Destroy(_indicatorGO);

        float cs = GridManager.Instance?.CellStep ?? 1.05f;

        _indicatorGO = new GameObject("PerceptionIndicator");
        _indicatorGO.transform.SetParent(transform, false);

        var tmp = _indicatorGO.AddComponent<TMPro.TextMeshPro>();
        tmp.text = symbol;
        tmp.fontSize = 8f;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.fontStyle = TMPro.FontStyles.Bold;
        tmp.enableWordWrapping = false;

        // Sorting au dessus de tout
        var mr = _indicatorGO.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.sortingLayerName = "CellContent";
            mr.sortingOrder = 20;
        }

        // Animation : bondit vers le haut puis fade
        float startY = cs * 0.8f;
        float peakY = cs * 1.6f;
        float duration = 0.7f;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float progress = t / duration;

            // Courbe de rebond : monte vite, petit rebond, stabilise
            float bounce;
            if (progress < 0.3f)
            {
                // Montee rapide
                bounce = Mathf.Lerp(startY, peakY, progress / 0.3f);
            }
            else if (progress < 0.5f)
            {
                // Petit retour
                float sub = (progress - 0.3f) / 0.2f;
                bounce = Mathf.Lerp(peakY, peakY - cs * 0.15f, sub);
            }
            else
            {
                // Stabilise + fade
                float sub = (progress - 0.5f) / 0.5f;
                bounce = Mathf.Lerp(peakY - cs * 0.15f, peakY - cs * 0.1f, sub);
                tmp.color = new Color(color.r, color.g, color.b, 1f - sub);
            }

            _indicatorGO.transform.localPosition = new Vector3(0f, bounce, -0.1f);

            // Scale pop
            float scale = progress < 0.15f
                ? Mathf.Lerp(0f, 1.3f, progress / 0.15f)
                : progress < 0.3f
                    ? Mathf.Lerp(1.3f, 1f, (progress - 0.15f) / 0.15f)
                    : 1f;
            _indicatorGO.transform.localScale = Vector3.one * scale;

            yield return null;
        }

        if (_indicatorGO != null) Destroy(_indicatorGO);
        _indicatorGO = null;
    }

    // =========================================================================
    // Tour par tour — action de l ennemi
    // =========================================================================

    /// <summary>
    /// Appele par TurnManager pendant EnemyTurn.
    /// L ennemi avance d une case vers le heros ou attaque si adjacent.
    /// </summary>
    public IEnumerator TakeTurn()
    {
        if (_ei.IsDead) yield break;

        var hero = HeroController.Instance;
        if (hero == null) yield break;
        var heroPos = hero.GridPosition;

        // Distance Manhattan au heros
        int dist = Mathf.Abs(heroPos.x - _ei.GridX)
                 + Mathf.Abs(heroPos.y - _ei.GridY);
        int perception = PerceptionRadius;

        // Hors perception → ne fait rien, indicateur "?"
        if (dist > perception)
        {
            // Yield pour laisser l animation se terminer avant le tour suivant
            yield return StartCoroutine(
                ShowIndicatorCoroutine("?", new Color(0.4f, 0.7f, 1f)));
            yield break;
        }

        // Adjacent (Chebyshev 1) → attaque pure (sans animation alert)
        int chebyDist = Mathf.Max(
            Mathf.Abs(heroPos.x - _ei.GridX),
            Mathf.Abs(heroPos.y - _ei.GridY));
        if (chebyDist <= 1)
        {
            _ei.UpdateFacing();
            yield return StartCoroutine(TurnAttack());
            yield break;
        }

        // Dans perception mais pas adjacent → avancer d une case
        yield return StartCoroutine(MoveTowardHero(heroPos));
    }

    /// <summary>
    /// Attaque pure pour le tour par tour — sans animation alert.
    /// L animation alert joue a la decouverte (avec le "!"), pas a chaque tour.
    /// </summary>
    public IEnumerator TurnAttack()
    {
        if (_ei.IsDead) yield break;

        switch (_ei.EnemyType)
        {
            case CellContent.Enemy_Wolf:
                if (_anim != null)
                    yield return StartCoroutine(_anim.WolfAttackAnim());
                else
                { yield return new WaitForSeconds(0.3f); _ei.EnemyAttackDamage(); }
                break;

            case CellContent.Enemy_BanditSword:
                if (_anim != null)
                    yield return StartCoroutine(_anim.BanditSwordAttackAnim());
                else
                { yield return new WaitForSeconds(0.3f); _ei.EnemyAttackDamage(); }
                break;

            case CellContent.Enemy_BanditArcher:
                if (_anim != null)
                    yield return StartCoroutine(_anim.ArcherAttackAnim(FireArrow));
                else
                { yield return new WaitForSeconds(0.3f); _ei.EnemyAttackDamage(); }
                break;

            default:
                var caveBeh = GetComponent<CaveEnemyBehaviour>();
                if (caveBeh != null)
                    yield return StartCoroutine(caveBeh.CounterAttack());
                else if (_anim != null)
                {
                    yield return StartCoroutine(
                        _anim.AttackLungeAnim(() => _ei.EnemyAttackDamage()));
                }
                else
                {
                    yield return new WaitForSeconds(0.3f);
                    _ei.EnemyAttackDamage();
                }
                break;
        }
    }

    /// <summary>
    /// Deplace l ennemi d une case vers le heros.
    /// Uniquement sur cases revelees. Pathfinding simple :
    /// parmi les 8 voisins reveles, choisir celui qui rapproche le plus.
    /// </summary>
    private IEnumerator MoveTowardHero(Vector2Int heroPos)
    {
        // Detecter grille active
        bool inCave = CaveManager.Instance != null && CaveManager.Instance.IsInCave;
        GridManager gm = inCave ? null : GridManager.Instance;
        UndergroundGrid ug = inCave ? UndergroundGrid.Instance : null;

        int gridW, gridH;
        if (inCave && ug != null) { gridW = ug.Width; gridH = ug.Height; }
        else if (gm != null) { gridW = gm.Width; gridH = gm.Height; }
        else yield break;

        // Trouver la meilleure case adjacente revelee
        Vector2Int bestCell = new Vector2Int(-1, -1);
        int bestDist = int.MaxValue;

        var dirs = new Vector2Int[]
        {
            new Vector2Int( 0, 1), new Vector2Int( 0,-1),
            new Vector2Int( 1, 0), new Vector2Int(-1, 0),
            new Vector2Int( 1, 1), new Vector2Int(-1, 1),
            new Vector2Int( 1,-1), new Vector2Int(-1,-1)
        };

        foreach (var d in dirs)
        {
            int nx = _ei.GridX + d.x;
            int ny = _ei.GridY + d.y;
            if (nx < 0 || ny < 0 || nx >= gridW || ny >= gridH) continue;

            // Verifier que la case est revelee
            Cell cell = inCave ? ug?.GetCell(nx, ny) : gm?.GetCell(nx, ny);
            if (cell == null || !cell.IsRevealed) continue;

            // Verifier qu il n y a pas un autre ennemi dessus
            bool occupied = false;
            foreach (var other in EnemyInstance.All)
            {
                if (other == _ei || other.IsDead) continue;
                if (other.GridX == nx && other.GridY == ny)
                { occupied = true; break; }
            }
            if (occupied) continue;

            // Distance Manhattan au heros
            int d2 = Mathf.Abs(heroPos.x - nx) + Mathf.Abs(heroPos.y - ny);
            if (d2 < bestDist)
            {
                bestDist = d2;
                bestCell = new Vector2Int(nx, ny);
            }
        }

        // Aucune case valide → reste sur place
        if (bestCell.x < 0) yield break;

        // Animer le deplacement
        float cs = GridManager.Instance?.CellStep ?? 1.05f;
        Vector3 from = transform.position;
        Vector3 to = new Vector3(bestCell.x * cs, bestCell.y * cs, from.z);

        // Orienter vers la direction de deplacement
        _ei.UpdateFacing();

        float duration = 0.2f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            transform.position = Vector3.Lerp(from, to, t / duration);
            yield return null;
        }
        transform.position = to;

        // Mettre a jour la position grille
        _ei.SetGridPosition(bestCell.x, bestCell.y);
    }

    // =========================================================================
    // Futurs comportements (squelette)
    // =========================================================================

    // FUTUR - EnemyRoamingSystem lira territoryRadius et perceptionRadius
    // FUTUR - EnemyTrait flags pour comportements bonus (CanFlee, BreaksTrees...)

}
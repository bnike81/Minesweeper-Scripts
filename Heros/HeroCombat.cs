using UnityEngine;
using System.Collections;

/// <summary>
/// HeroCombat v5 — Architecture combat unifiee avec TurnManager.
///
/// CHANGEMENTS vs v4 :
///   1. Publie OnHeroActionComplete apres chaque attaque (DrawThenAttack, JustAttack)
///      → TurnManager recoit le signal et lance le tour ennemi.
///   2. Publie OnHeroActionComplete apres SheathThenFlee (rengaine + fuite)
///      → le deplacement du heros est considere comme son action de tour.
///
/// FIX v6 :
///   - SheathThenFlee : onDone AVANT NotifyActionComplete
///
/// FIX v7 :
///   - Ajout IsFlinching pour que TurnManager puisse attendre la fin du flinch
///     avant de deverrouiller le hero.
///
/// PRINCIPE :
///   TurnManager est l UNIQUE autorite du cycle de combat.
///   HeroCombat signale la fin de chaque action hero via OnHeroActionComplete.
///   TurnManager gere ensuite le tour ennemi (EnemyTurnSequence).
///   Aucune contre-attaque legacy ne passe par ici.
///
/// CONSERVE de v4 :
///   - PlayHitAnimation ne verrouille PAS le mouvement (flinch visuel seulement)
///   - ForceReset() pour HeroState.ResetAll()
///   - _activeHitCoroutine tracking
/// </summary>
public class HeroCombat : MonoBehaviour
{
    public static HeroCombat Instance { get; private set; }

    [Header("=== Degaine (5 sprites) ===")]
    [SerializeField] private Sprite[] _drawRight = new Sprite[5];
    [SerializeField] private Sprite[] _drawLeft = new Sprite[5];

    [Header("=== Attaque 1 (6 sprites) ===")]
    [SerializeField] private Sprite[] _attack1Right = new Sprite[6];
    [SerializeField] private Sprite[] _attack1Left = new Sprite[6];

    [Header("=== Attaque 2 (9 sprites) ===")]
    [SerializeField] private Sprite[] _attack2Right = new Sprite[9];
    [SerializeField] private Sprite[] _attack2Left = new Sprite[9];

    [Header("=== Rengaine (5 sprites) ===")]
    [SerializeField] private Sprite[] _sheathRight = new Sprite[5];
    [SerializeField] private Sprite[] _sheathLeft = new Sprite[5];

    [Header("=== Degat sans arme ===")]
    [Tooltip("Animation coup recu avant d avoir degaine")]
    [SerializeField] private Sprite[] _hitUnarmedRight = new Sprite[6];
    [SerializeField] private Sprite[] _hitUnarmedLeft = new Sprite[6];

    [Header("=== Degat avec arme ===")]
    [Tooltip("Animation coup recu pendant le combat (arme sortie)")]
    [SerializeField] private Sprite[] _hitArmedRight = new Sprite[6];
    [SerializeField] private Sprite[] _hitArmedLeft = new Sprite[6];

    [SerializeField, Range(0.03f, 0.12f)] private float _hitFrameTime = 0.06f;

    [Header("=== Timing ===")]
    [SerializeField, Range(0.04f, 0.15f)] private float _drawFrameTime = 0.08f;
    [SerializeField, Range(0.04f, 0.12f)] private float _attack1FrameTime = 0.07f;
    [SerializeField, Range(0.04f, 0.12f)] private float _attack2FrameTime = 0.06f;
    [SerializeField, Range(0.04f, 0.15f)] private float _sheathFrameTime = 0.08f;

    // -------------------------------------------------------------------------
    // Etat
    // -------------------------------------------------------------------------

    public bool IsInCombat { get; private set; } = false;
    public bool IsAnimating { get; private set; } = false;

    /// <summary>
    /// FIX v7 : expose l etat du flinch (hurt animation) pour TurnManager.
    /// TurnManager attend IsFlinching == false avant de deverrouiller le hero.
    /// </summary>
    public bool IsFlinching => _activeHitCoroutine != null;

    private bool _facingRight = true;
    private bool _useAttack2Next = false;
    private Sprite _combatIdleSprite = null;
    private SpriteRenderer _sr;
    private EnemyInstance _currentEnemy;

    // Tracking du flinch — v4 ajout
    private Coroutine _activeHitCoroutine;

    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        _sr = GetComponent<SpriteRenderer>();
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnPlayerDamaged>(OnPlayerDamaged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnPlayerDamaged>(OnPlayerDamaged);
    }

    // =========================================================================
    // DEGATS RECUS — flinch visuel SANS verrouillage
    // =========================================================================

    private void OnPlayerDamaged(OnPlayerDamaged evt)
    {
        // ─── v13 FIX : retire le check "if (evt.Damage <= 0) return;"
        // pour jouer le flinch visuel meme quand la defense bloque les degats.

        // Si une animation HERO est en cours (Draw/Attack/Sheath),
        // ne pas lancer de flinch → evite les conflits de sprites.
        if (IsAnimating)
        {
            Debug.Log("<color=#FF00FF>[HeroCombat]</color> OnPlayerDamaged — flinch BLOQUE (IsAnimating=true)");
            return;
        }

        bool fromRight = FindAttackerDirection();
        Debug.Log("<color=#FF00FF>[HeroCombat]</color> OnPlayerDamaged — flinch (dmg=" + evt.Damage + ")");

        // Stopper un flinch precedent si encore en cours
        if (_activeHitCoroutine != null)
        {
            StopCoroutine(_activeHitCoroutine);
            _activeHitCoroutine = null;
        }

        _activeHitCoroutine = StartCoroutine(PlayHitAnimation(fromRight));
    }

    /// Direction de l attaque ennemie pour le hurt hero.
    /// v11/v12 : utilise TurnManager.CurrentAttacker en priorite.
    private bool FindAttackerDirection()
    {
        var hero = HeroController.Instance;
        if (hero == null) return true;
        Vector3 heroPos = hero.transform.position;

        // v11 : priorite a CurrentAttacker du TurnManager
        var tm = TurnManager.Instance;
        if (tm != null && tm.CurrentAttacker != null && !tm.CurrentAttacker.IsDead)
        {
            return tm.CurrentAttacker.transform.position.x > heroPos.x;
        }

        // Fallback : chercher un ennemi qui anime (cas legacy)
        foreach (var ei in EnemyInstance.All)
        {
            if (ei == null || ei.IsDead) continue;
            if (!ei.IsAnimating) continue;
            return ei.transform.position.x > heroPos.x;
        }

        return !_facingRight;
    }

    /// Flinch visuel — ne verrouille PAS le mouvement.
    /// v13 Jour 4 : transitionne la SM vers Flinching pendant l anim.
    /// </summary>
    private IEnumerator PlayHitAnimation(bool attackFromRight)
    {
        // ─── v13 Jour 4 : transitionner vers Flinching ───
        var hs = HeroState.Instance;
        bool smOwned = false;
        if (hs != null && hs.SM != null)
        {
            // Only transition if not in a phase that disallows Flinching
            smOwned = hs.SM.TryTransitionTo(HeroPhase.Flinching, "PlayHitAnimation start");
        }
        // ─── fin transition ───

        bool lookRight = attackFromRight;
        Sprite[] sprites;

        if (IsInCombat)
            sprites = lookRight ? _hitArmedRight : _hitArmedLeft;
        else
            sprites = lookRight ? _hitUnarmedRight : _hitUnarmedLeft;

        if (sprites != null && sprites.Length > 0)
        {
            foreach (var sp in sprites)
            {
                if (sp != null && _sr != null) _sr.sprite = sp;
                yield return new WaitForSeconds(_hitFrameTime);
            }
        }

        // Restaurer le sprite idle
        if (IsInCombat)
        {
            _facingRight = attackFromRight;
            ShowIdle();
        }
        else
        {
            var hero = HeroController.Instance;
            if (hero != null)
                hero.SetIdleFacing(attackFromRight);
        }

        _activeHitCoroutine = null;

        // ─── v13 Jour 4 : retour a Idle si on a bien transitionne ───
        if (smOwned && hs != null && hs.SM != null
            && hs.SM.CurrentPhase == HeroPhase.Flinching)
        {
            hs.SM.TryTransitionTo(HeroPhase.Idle, "PlayHitAnimation end");
        }
    }


    // =========================================================================
    // API publique
    // =========================================================================

    /// <summary>
    /// Appelee par EnemyInstance.OnPointerClick.
    /// Ignore si animation en cours.
    /// </summary>
    public void RequestAttack(EnemyInstance enemy)
    {
        if (IsAnimating) return;
        if (enemy == null || enemy.IsDead) return;

        _currentEnemy = enemy;
        UpdateFacing(enemy);

        // Stopper le flinch si en cours
        if (_activeHitCoroutine != null)
        {
            StopCoroutine(_activeHitCoroutine);
            _activeHitCoroutine = null;
        }

        if (!IsInCombat)
            StartCoroutine(DrawThenAttack(enemy));
        else
            StartCoroutine(JustAttack(enemy));
    }

    /// <summary>
    /// Appelee par HeroController quand clic deplacement pendant combat.
    /// Rengaine puis execute le callback.
    /// </summary>
    public void RequestFlee(System.Action onDone)
    {
        if (!IsInCombat)
        {
            onDone?.Invoke();
            return;
        }
        if (IsAnimating)
        {
            StartCoroutine(WaitThenSheath(onDone));
            return;
        }
        StartCoroutine(SheathThenFlee(onDone));
    }

    // =========================================================================
    // Coroutines internes
    // =========================================================================

    private IEnumerator DrawThenAttack(EnemyInstance enemy)
    {
        SetLocked(true);
        IsInCombat = true;

        yield return StartCoroutine(PlayAnim(
            _facingRight ? _drawRight : _drawLeft, _drawFrameTime));

        _combatIdleSprite = GetLast(_facingRight ? _drawRight : _drawLeft);
        ShowIdle();

        yield return StartCoroutine(RunAttack(enemy));

        SetLocked(false);

        // ── v5 : signaler au TurnManager que l action hero est terminee ──
        NotifyActionComplete();
    }

    private IEnumerator JustAttack(EnemyInstance enemy)
    {
        SetLocked(true);
        yield return StartCoroutine(RunAttack(enemy));
        SetLocked(false);

        // ── v5 : signaler au TurnManager que l action hero est terminee ──
        NotifyActionComplete();
    }

    /// Animation d attaque pure + infliger degats.
    /// v12 FIX : attend que la REACTION de l ennemi (hit shake + flash)
    /// soit terminee avant de retourner, pour que NotifyActionComplete
    /// ne parte qu apres la fin COMPLETE de l action hero + consequences.
    private IEnumerator RunAttack(EnemyInstance enemy)
    {
        UpdateFacing(enemy);

        Sprite[] sprites;
        float ft;

        if (!_useAttack2Next)
        {
            sprites = _facingRight ? _attack1Right : _attack1Left;
            ft = _attack1FrameTime;
            _useAttack2Next = true;
        }
        else
        {
            sprites = _facingRight ? _attack2Right : _attack2Left;
            ft = _attack2FrameTime;
            _useAttack2Next = false;
        }

        yield return StartCoroutine(PlayAnim(sprites, ft));

        ShowIdle();

        if (enemy != null && !enemy.IsDead)
        {
            int dmg = Mathf.Max(1, Equipment.Instance?.AttackDamage ?? 1);
            enemy.ReceiveAttack(dmg);

            // ── v12 FIX : attendre que la reaction de l ennemi soit TERMINEE ──
            // ReceiveAttack lance PlayerAttackSequence en StartCoroutine (fire-and-forget).
            // On attend que IsAnimating redevienne false (fin du shake + flash).
            // Timeout 3s de securite.
            float timeout = 3f;
            float elapsed = 0f;
            while (enemy != null && !enemy.IsDead && enemy.IsAnimating && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (elapsed >= timeout)
                Debug.LogWarning("[HeroCombat] RunAttack — timeout attente reaction ennemi");

            // Petit delai apres le shake pour la lisibilite
            yield return WaitCache.Get(0.15f);
        }
    }

    private IEnumerator WaitThenSheath(System.Action onDone)
    {
        while (IsAnimating) yield return null;
        yield return StartCoroutine(SheathThenFlee(onDone));
    }

    /// Rengaine l arme puis execute le callback de fuite.
    /// v12 FIX : NotifyActionComplete RETIRE d ici.
    ///   Le signal partira de MoveAlongSegments quand le deplacement sera
    ///   COMPLETEMENT termine. Cela garantit que le heros finit son action
    ///   (rengaine + deplacement + arrivee) AVANT que le tour ennemi ne commence.
    private IEnumerator SheathThenFlee(System.Action onDone)
    {
        SetLocked(true);

        yield return StartCoroutine(PlayAnim(
            _facingRight ? _sheathRight : _sheathLeft, _sheathFrameTime));

        IsInCombat = false;
        _currentEnemy = null;

        SetLocked(false);
        HeroController.Instance?.SetIdleSprite();

        // Lancer le deplacement via le callback
        onDone?.Invoke();

        // ── v12 FIX : PAS de NotifyActionComplete ici ──
        // Le signal sera publie par HeroController.MoveAlongSegments
        // quand le deplacement sera completement termine (position finale atteinte).
        // Cela garantit : rengaine → deplacement → arrivee → PUIS tour ennemi.
    }

    // =========================================================================
    // v5 — Signal au TurnManager
    // =========================================================================

    /// Publie OnHeroActionComplete pour signaler au TurnManager
    /// que le heros a termine son action (attaque ou fuite).
    /// Ne fait rien si TurnManager n existe pas ou n est pas actif
    /// (mode libre — pas de tour par tour).
    private void NotifyActionComplete()
    {
        var tm = TurnManager.Instance;
        if (tm == null) return;

        // Publier meme si TurnManager est Inactive — il pourrait
        // reactiver le combat si un ennemi est en perception.
        // OnHeroActionDone() dans TurnManager gere les deux cas.
        Debug.Log("<color=#FF00FF>[HeroCombat]</color> → OnHeroActionComplete");
        EventBus.Publish(new OnHeroActionComplete());
    }

    // =========================================================================
    // Reset d urgence (appele par HeroState.ResetAll)
    // =========================================================================

    public void ForceReset()
    {
        StopAllCoroutines();
        _activeHitCoroutine = null;
        IsAnimating = false;
        IsInCombat = false;
        _currentEnemy = null;
        _useAttack2Next = false;
        HeroController.Instance?.RestoreIdleSprite();
        Debug.Log("<color=#FFAA00>[HeroCombat]</color> ForceReset");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// Verrou : IsAnimating + transition SM vers Attacking.
    /// v13 Jour 3 : utilise SetLockedPhase pour transitionner explicitement
    /// vers HeroPhase.Attacking au lieu du UILocked generique.
    private void SetLocked(bool locked)
    {
        IsAnimating = locked;

        if (HeroState.Instance != null)
        {
            HeroState.Instance.SetLockedPhase(
                locked,
                HeroPhase.Attacking,
                "HeroCombat.SetLocked=" + locked);
        }

        Debug.Log("<color=#FF00FF>[HeroCombat]</color> SetLocked(" + locked + ")");
    }

    private void UpdateFacing(EnemyInstance enemy)
    {
        if (enemy == null || HeroController.Instance == null) return;
        _facingRight = enemy.transform.position.x
                    >= HeroController.Instance.transform.position.x;
    }

    private void ShowIdle()
    {
        if (_combatIdleSprite != null && _sr != null)
            _sr.sprite = _combatIdleSprite;
    }

    private IEnumerator PlayAnim(Sprite[] sprites, float frameTime)
    {
        if (sprites == null) yield break;
        foreach (var sp in sprites)
        {
            if (sp != null && _sr != null) _sr.sprite = sp;
            yield return new WaitForSeconds(frameTime);
        }
    }

    private Sprite GetLast(Sprite[] arr)
    {
        if (arr == null) return null;
        for (int i = arr.Length - 1; i >= 0; i--)
            if (arr[i] != null) return arr[i];
        return null;
    }
}
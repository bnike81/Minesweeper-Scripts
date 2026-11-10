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
        if (evt.Damage <= 0) return;

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

    private bool FindAttackerDirection()
    {
        var hero = HeroController.Instance;
        if (hero == null) return true;

        foreach (var ei in EnemyInstance.All)
        {
            if (!ei.IsAnimating || ei.IsDead) continue;
            return ei.transform.position.x > hero.transform.position.x;
        }
        return !_facingRight;
    }

    /// <summary>
    /// Flinch visuel — ne verrouille PAS le mouvement.
    /// Seules les actions HERO (Draw/Attack/Sheath) verrouillent.
    /// </summary>
    private IEnumerator PlayHitAnimation(bool attackFromRight)
    {
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

    /// <summary>Animation d attaque pure + infliger degats</summary>
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
        }
    }

    private IEnumerator WaitThenSheath(System.Action onDone)
    {
        while (IsAnimating) yield return null;
        yield return StartCoroutine(SheathThenFlee(onDone));
    }

    private IEnumerator SheathThenFlee(System.Action onDone)
    {
        SetLocked(true);

        yield return StartCoroutine(PlayAnim(
            _facingRight ? _sheathRight : _sheathLeft, _sheathFrameTime));

        IsInCombat = false;
        _currentEnemy = null;

        SetLocked(false);
        HeroController.Instance?.SetIdleSprite();

        // ── FIX v6 : onDone AVANT NotifyActionComplete ──
        // L ancien ordre (Notify → onDone) causait un blocage :
        // TurnManager verrouillait IsUILocked=true AVANT que le mouvement ne demarre.
        // Nouveau flux :
        //   1. onDone.Invoke() → demarre le deplacement (coroutine)
        //   2. NotifyActionComplete → TurnManager lance le tour ennemi
        // Le heros se deplace PENDANT que les ennemis jouent. Apres le tour ennemi,
        // IsUILocked=false et le hero peut re-agir normalement.
        onDone?.Invoke();
        NotifyActionComplete();
    }

    // =========================================================================
    // v5 — Signal au TurnManager
    // =========================================================================

    /// <summary>
    /// Publie OnHeroActionComplete pour signaler au TurnManager
    /// que le heros a termine son action (attaque ou fuite).
    /// Ne fait rien si TurnManager n existe pas ou n est pas actif
    /// (mode libre — pas de tour par tour).
    /// </summary>
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

    /// <summary>Verrou : IsAnimating + HeroState.IsUILocked</summary>
    private void SetLocked(bool locked)
    {
        IsAnimating = locked;
        if (HeroState.Instance != null)
            HeroState.Instance.IsUILocked = locked;
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
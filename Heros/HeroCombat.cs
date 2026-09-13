using UnityEngine;
using System.Collections;

/// <summary>
/// HeroCombat - Animations combat heros.
/// 
/// REGLES STRICTES :
///   IsAnimating = true pendant TOUTE animation (degaine+attaque groupes)
///   IsInCombat  = true des le premier clic, false apres rengaine
///   IsUILocked  = miroir de IsAnimating pour bloquer HeroController
/// 
/// FLUX :
///   1er clic ennemi -> DrawAndAttack (degaine + attaque1 sans interruption)
///   clics suivants  -> DoAttack seulement (pas de degaine)
///   clic deplacement -> SheathAndFlee
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
    // Etat - source unique de verite
    // -------------------------------------------------------------------------

    public bool IsInCombat { get; private set; } = false;
    public bool IsAnimating { get; private set; } = false;

    private bool _facingRight = true;
    private bool _useAttack2Next = false;
    private Sprite _combatIdleSprite = null;
    private SpriteRenderer _sr;
    private EnemyInstance _currentEnemy;

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

    private void OnPlayerDamaged(OnPlayerDamaged evt)
    {
        if (evt.Damage <= 0) return;
        // Trouver d ou vient l ennemi qui attaque
        bool fromRight = FindAttackerDirection();
        StartCoroutine(PlayHitAnimation(fromRight));
    }

    private bool FindAttackerDirection()
    {
        // Chercher l ennemi le plus proche qui est en animation
        float cs = GridManager.Instance?.CellStep ?? 1.05f;
        var hero = HeroController.Instance;
        if (hero == null) return true;

        foreach (var ei in EnemyInstance.All)
        {
            if (!ei.IsAnimating || ei.IsDead) continue;
            // L ennemi attaquant est a gauche ou droite du heros
            return ei.transform.position.x > hero.transform.position.x;
        }
        // Fallback : utiliser _facingRight inverse
        return !_facingRight;
    }

    private IEnumerator PlayHitAnimation(bool attackFromRight)
    {
        if (IsAnimating) yield break;
        SetLocked(true);

        // Direction du coup = cote d ou vient l attaque
        // Le heros regarde vers l attaquant
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

        // Apres le coup : orienter le heros face a l attaquant
        if (IsInCombat)
        {
            // Mettre a jour _facingRight selon la direction du coup
            _facingRight = attackFromRight;
            ShowIdle();
        }
        else
        {
            // Pas encore en combat - orienter l idle vers l attaquant
            var hero = HeroController.Instance;
            if (hero != null)
                hero.SetIdleFacing(attackFromRight);
        }

        SetLocked(false);
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
            // Attendre la fin de l animation courante puis rengainer
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

        // Degaine
        yield return StartCoroutine(PlayAnim(
            _facingRight ? _drawRight : _drawLeft, _drawFrameTime));

        // Sauvegarder posture combat
        _combatIdleSprite = GetLast(_facingRight ? _drawRight : _drawLeft);
        ShowIdle();

        // Attaque directement - pas de deverrouillage entre les deux
        yield return StartCoroutine(RunAttack(enemy));

        SetLocked(false);
    }

    private IEnumerator JustAttack(EnemyInstance enemy)
    {
        SetLocked(true);
        yield return StartCoroutine(RunAttack(enemy));
        SetLocked(false);
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

        // Degats apres animation
        if (enemy != null && !enemy.IsDead)
        {
            int dmg = Mathf.Max(1, Equipment.Instance?.AttackDamage ?? 1);
            enemy.ReceiveAttack(dmg);
        }
    }

    private IEnumerator WaitThenSheath(System.Action onDone)
    {
        // Attendre que IsAnimating redevienne false
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

        onDone?.Invoke();
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>Verrou unique pour IsAnimating + HeroState.IsUILocked</summary>
    private void SetLocked(bool locked)
    {
        IsAnimating = locked;
        if (HeroState.Instance != null)
            HeroState.Instance.IsUILocked = locked;
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
using UnityEngine;
using System.Collections;

/// <summary>
/// EnemyAnimator - Toutes les coroutines d animation des ennemis.
/// Separe de EnemyInstance pour la lisibilite.
/// Chaque ennemi a ses propres sprites et timings configures dans Inspector.
///
/// FIX v4 : les gardes IsPlaying() apres les alertes ont ete RETIRES.
///   Cause : apres l alerte, IsPlaying() pouvait retourner false
///   (GameManager null ou timing) et bloquer l attaque silencieusement.
///   Le check _ei.IsDead suffit — EnemyAttackDamage() verifie deja IsPlaying
///   avant d infliger les degats.
/// </summary>
[RequireComponent(typeof(EnemyInstance))]
public class EnemyAnimator : MonoBehaviour
{
    public static EnemyAnimator GetFor(EnemyInstance ei)
        => ei.GetComponent<EnemyAnimator>();

    // =========================================================================
    // Inspector - Loup
    // =========================================================================

    [Header("=== Loup - Alerte (10 frames) ===")]
    [SerializeField] private Sprite[] _wolfAlertRight = new Sprite[10];
    [SerializeField] private Sprite[] _wolfAlertLeft = new Sprite[10];
    [SerializeField, Range(0.04f, 0.15f)] private float _wolfAlertFrameTime = 0.08f;

    [Header("=== Loup - Attaque (12 frames) ===")]
    [SerializeField] private Sprite[] _wolfAttackRight = new Sprite[12];
    [SerializeField] private Sprite[] _wolfAttackLeft = new Sprite[12];
    [SerializeField, Range(0.03f, 0.12f)] private float _wolfAttackFrameTime = 0.06f;
    [SerializeField, Range(0.1f, 1f)] private float _wolfAttackLunge = 0.4f;

    // =========================================================================
    // Inspector - Bandit Epee
    // =========================================================================

    [Header("=== Bandit Epee - Decouverte (7 frames) ===")]
    [SerializeField] private Sprite[] _swordAlertRight = new Sprite[7];
    [SerializeField] private Sprite[] _swordAlertLeft = new Sprite[7];
    [SerializeField, Range(0.04f, 0.15f)] private float _swordAlertFrameTime = 0.08f;

    [Header("=== Bandit Epee - Attaque (13 frames) ===")]
    [SerializeField] private Sprite[] _swordAttackRight = new Sprite[13];
    [SerializeField] private Sprite[] _swordAttackLeft = new Sprite[13];
    [SerializeField, Range(0.03f, 0.12f)] private float _swordAttackFrameTime = 0.06f;
    [SerializeField, Range(0.1f, 1f)] private float _swordAttackLunge = 0.35f;

    // =========================================================================
    // Inspector - Bandit Archer
    // =========================================================================

    [Header("=== Bandit Archer - Attaque (15 frames) ===")]
    [SerializeField] private Sprite[] _archerAttackRight = new Sprite[15];
    [SerializeField] private Sprite[] _archerAttackLeft = new Sprite[15];
    [SerializeField, Range(0.03f, 0.12f)] private float _archerAttackFrameTime = 0.07f;

    // =========================================================================
    // Inspector - Generique
    // =========================================================================

    [Header("=== Generique ===")]
    [SerializeField, Range(0.1f, 1f)] private float _attackLungeDefault = 0.3f;
    [SerializeField, Range(0.1f, 2f)] private float _attackDelay = 0.5f;

    // =========================================================================
    // References
    // =========================================================================

    private EnemyInstance _ei;
    private SpriteRenderer _sr;

    private void Awake()
    {
        _ei = GetComponent<EnemyInstance>();
        _sr = GetComponent<SpriteRenderer>()
           ?? GetComponentInChildren<SpriteRenderer>();
    }

    // =========================================================================
    // API publique - appelee par EnemyInstance
    // =========================================================================

    public IEnumerator WolfAlertThenAttack()
    {
        Debug.Log("<color=#FF6600>[EnemyAnimator]</color> Wolf alerte START");
        yield return StartCoroutine(WolfAlertAnim());

        if (_ei.IsDead)
        {
            Debug.Log("<color=#FF6600>[EnemyAnimator]</color> Wolf mort apres alerte — skip attaque");
            yield break;
        }

        // ── FIX v4 : IsPlaying() RETIRE ──
        // L ancien garde bloquait l attaque si GameManager.IsPlaying etait false.
        // EnemyAttackDamage() verifie deja IsPlaying avant d infliger les degats.
        Debug.Log("<color=#FF6600>[EnemyAnimator]</color> Wolf attaque START");
        yield return StartCoroutine(WolfAttackAnim());
        Debug.Log("<color=#FF6600>[EnemyAnimator]</color> Wolf attaque FIN");
    }

    public IEnumerator BanditSwordAlertThenAttack()
    {
        Debug.Log("<color=#FF6600>[EnemyAnimator]</color> BanditSword alerte START");
        var alert = _ei.FacingRight ? _swordAlertRight : _swordAlertLeft;
        yield return StartCoroutine(PlaySprites(alert, _swordAlertFrameTime));
        ApplySwordCombatIdle();

        if (_ei.IsDead)
        {
            Debug.Log("<color=#FF6600>[EnemyAnimator]</color> BanditSword mort apres alerte — skip attaque");
            yield break;
        }

        // ── FIX v4 : IsPlaying() RETIRE ──
        Debug.Log("<color=#FF6600>[EnemyAnimator]</color> BanditSword attaque START");
        yield return new WaitForSeconds(0.2f);
        yield return StartCoroutine(BanditSwordAttackAnim());
        Debug.Log("<color=#FF6600>[EnemyAnimator]</color> BanditSword attaque FIN");
    }

    public IEnumerator BanditSwordAttackAnim()
    {
        var sprites = _ei.FacingRight ? _swordAttackRight : _swordAttackLeft;
        var dir = GetHeroDir();
        bool dmgDone = false;

        for (int i = 0; i < sprites.Length; i++)
        {
            if (_ei.IsDead) yield break;
            if (sprites[i] != null && _sr != null) _sr.sprite = sprites[i];
            float t = (float)i / (sprites.Length - 1);
            float d = Mathf.Sin(t * Mathf.PI) * _swordAttackLunge;
            transform.position = _ei.OriginalPos + dir * d;
            if (i == 8 && !dmgDone) { dmgDone = true; _ei.EnemyAttackDamage(); }
            yield return new WaitForSeconds(_swordAttackFrameTime);
        }
        transform.position = _ei.OriginalPos;
        ApplySwordCombatIdle();
    }

    public IEnumerator ArcherAttackAnim(System.Action onFireFrame)
    {
        var sprites = _ei.FacingRight ? _archerAttackRight : _archerAttackLeft;
        if (sprites == null || sprites.Length == 0) yield break;

        bool fired = false;
        for (int i = 0; i < sprites.Length; i++)
        {
            if (_ei.IsDead) yield break;
            if (sprites[i] != null && _sr != null) _sr.sprite = sprites[i];
            if (i == 11 && !fired) { fired = true; onFireFrame?.Invoke(); }
            yield return new WaitForSeconds(_archerAttackFrameTime);
        }
        ApplyArcherIdle();
    }

    public IEnumerator AttackLungeAnim()
    {
        var dir = GetHeroDir();
        float t = 0f;
        float half = 0.25f;
        while (t < 1f)
        {
            t += Time.deltaTime / half;
            float d = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * _attackLungeDefault;
            transform.position = _ei.OriginalPos + dir * d;
            yield return null;
        }
        transform.position = _ei.OriginalPos;
        yield return new WaitForSeconds(_attackDelay);
    }

    /// <summary>Surcharge avec callback degats a la fin du lunge</summary>
    public IEnumerator AttackLungeAnim(System.Action onDamage)
    {
        yield return StartCoroutine(AttackLungeAnim());
        onDamage?.Invoke();
    }

    public IEnumerator HitFlashAnim()
    {
        if (_sr == null) yield break;
        _sr.color = Color.red;
        yield return new WaitForSeconds(0.08f);
        _sr.color = _ei.OriginalColor;
    }

    public IEnumerator HitShakeAnim()
    {
        var orig = _ei.OriginalPos;
        for (int i = 0; i < 3; i++)
        {
            transform.position = orig + new Vector3(0.06f, 0f, 0f);
            yield return new WaitForSeconds(0.04f);
            transform.position = orig + new Vector3(-0.06f, 0f, 0f);
            yield return new WaitForSeconds(0.04f);
        }
        transform.position = orig;
    }

    public IEnumerator DeathAnim()
    {
        if (_sr == null) yield break;
        float t = 0f;
        while (t < 0.4f)
        {
            t += Time.deltaTime;
            _sr.color = Color.Lerp(_ei.OriginalColor, Color.clear, t / 0.4f);
            transform.localScale = Vector3.Lerp(Vector3.one, Vector3.zero, t / 0.4f);
            yield return null;
        }
    }

    // =========================================================================
    // Idle helpers
    // =========================================================================

    public void ApplyWolfIdle()
    {
        if (_sr == null) return;
        var arr = _ei.FacingRight ? _wolfAlertRight : _wolfAlertLeft;
        if (arr != null && arr.Length > 0 && arr[0] != null) _sr.sprite = arr[0];
    }

    public void ApplySwordIdle()
    {
        if (_sr == null) return;
        var arr = _ei.FacingRight ? _swordAlertRight : _swordAlertLeft;
        if (arr != null && arr.Length > 0 && arr[0] != null) _sr.sprite = arr[0];
    }

    public void ApplySwordCombatIdle()
    {
        if (_sr == null) return;
        var arr = _ei.FacingRight ? _swordAlertRight : _swordAlertLeft;
        if (arr != null && arr.Length > 0)
        {
            var last = arr[arr.Length - 1];
            if (last != null) _sr.sprite = last;
        }
    }

    public void ApplyArcherIdle()
    {
        if (_sr == null) return;
        var arr = _ei.FacingRight ? _archerAttackRight : _archerAttackLeft;
        if (arr != null && arr.Length > 0 && arr[0] != null) _sr.sprite = arr[0];
    }

    // =========================================================================
    // Prive
    // =========================================================================

    public IEnumerator WolfAlertAnim()
    {
        var sprites = _ei.FacingRight ? _wolfAlertRight : _wolfAlertLeft;
        yield return StartCoroutine(PlaySprites(sprites, _wolfAlertFrameTime));
    }

    /// <summary>Alerte bandit epee seule (sans enchainer l attaque)</summary>
    public IEnumerator BanditSwordAlertAnim()
    {
        var alert = _ei.FacingRight ? _swordAlertRight : _swordAlertLeft;
        yield return StartCoroutine(PlaySprites(alert, _swordAlertFrameTime));
        ApplySwordCombatIdle();
    }

    public IEnumerator WolfAttackAnim()
    {
        var sprites = _ei.FacingRight ? _wolfAttackRight : _wolfAttackLeft;
        var dir = GetHeroDir();
        float lunge = _wolfAttackLunge;
        bool dmgDone = false;

        for (int i = 0; i < sprites.Length; i++)
        {
            if (_ei.IsDead) yield break;
            if (sprites[i] != null && _sr != null) _sr.sprite = sprites[i];

            if (i <= 2) transform.position = _ei.OriginalPos;
            else if (i <= 7) transform.position = Vector3.Lerp(_ei.OriginalPos, _ei.OriginalPos + dir * lunge, (i - 3) / 4f);
            else if (i == 8) transform.position = _ei.OriginalPos + dir * lunge;
            else transform.position = Vector3.Lerp(_ei.OriginalPos + dir * lunge, _ei.OriginalPos, (i - 8) / 3f);

            if (i == 6 && !dmgDone) { dmgDone = true; _ei.EnemyAttackDamage(); }
            yield return new WaitForSeconds(_wolfAttackFrameTime);
        }
        transform.position = _ei.OriginalPos;
    }

    private IEnumerator PlaySprites(Sprite[] sprites, float frameTime)
    {
        if (sprites == null) yield break;
        foreach (var sp in sprites)
        {
            if (sp != null && _sr != null) _sr.sprite = sp;
            yield return new WaitForSeconds(frameTime);
        }
    }

    private Vector3 GetHeroDir()
    {
        var hero = HeroController.Instance;
        if (hero == null) return Vector3.right;
        return (hero.transform.position - _ei.OriginalPos).normalized;
    }

    // IsPlaying() conserve pour reference mais PLUS UTILISE dans les gardes d attaque.
    // EnemyAttackDamage() fait deja ce check avant d infliger les degats.
    private bool IsPlaying()
    {
        var gm = GameManager.Instance;
        return gm != null && gm.IsPlaying && !_ei.IsDead;
    }
}
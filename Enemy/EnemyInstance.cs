using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;

/// <summary>
/// EnemyInstance - Stats, HP, clic joueur, mort, feedback visuel.
/// Les animations sont dans EnemyAnimator.
/// Les comportements IA sont dans EnemyBehaviour.
/// </summary>
public class EnemyInstance : MonoBehaviour,
    IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    // Registre statique — remplace FindObjectsOfType<EnemyInstance>()
    public static readonly System.Collections.Generic.List<EnemyInstance> All
        = new System.Collections.Generic.List<EnemyInstance>();

    private void Awake() { if (!All.Contains(this)) All.Add(this); }
    private void OnDestroy() { All.Remove(this); }

    // =========================================================================
    // Inspector
    // =========================================================================

    [Header("=== Feedback visuel ===")]
    [SerializeField, Range(0.1f, 2f)] private float _attackDelay = 0.5f;

    [Header("=== Label nom ===")]
    [SerializeField] private float _labelOffsetY = 0.6f;
    [SerializeField] private float _labelFontSize = 2f;
    private TMPro.TextMeshPro _nameLabel;

    // =========================================================================
    // Etat
    // =========================================================================

    private CellContent _enemyType;
    private int _gridX, _gridY;
    private int _maxHP, _currentHP;
    private Vector3 _originalPos;
    private Color _originalColor = Color.white;
    private SpriteRenderer _sr;
    private bool _isDead = false;
    private bool _isAnimating = false;
    private bool _isAttacking = false;
    private bool _wolfFacingRight = true;

    // Proprietes publiques
    public bool IsDead => _isDead;
    public bool IsAnimating { get => _isAnimating; set => _isAnimating = value; }
    public bool IsAttacking => _isAttacking;
    public CellContent EnemyType => _enemyType;
    public int GridX => _gridX;
    public int GridY => _gridY;
    public Vector3 OriginalPos => _originalPos;
    public Color OriginalColor => _originalColor;
    public bool FacingRight => _wolfFacingRight;

    public void UpdateFacing()
    {
        var hero = HeroController.Instance;
        if (hero != null)
            _wolfFacingRight = hero.GridPosition.x >= _gridX;
    }

    public int GetDamage() => GetAttackDamage();

    // Barre de vie
    private SpriteRenderer _healthBarBgRenderer;
    private SpriteRenderer _healthBarFillRenderer;

    // =========================================================================
    // Init
    // =========================================================================

    private void CreateNameLabel()
    {
        var go = new GameObject("NameLabel");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, _labelOffsetY, -0.1f);

        _nameLabel = go.AddComponent<TMPro.TextMeshPro>();
        _nameLabel.text = GetEnemyName();
        _nameLabel.fontSize = _labelFontSize;
        _nameLabel.alignment = TMPro.TextAlignmentOptions.Center;
        _nameLabel.color = Color.white;
        _nameLabel.fontStyle = TMPro.FontStyles.Bold;

        // Sorting au dessus des ennemis
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.sortingLayerName = "CellContent";
            mr.sortingOrder = 15;
        }
    }

    private void ShowLabel()
    {
        if (_nameLabel == null) return;
        _nameLabel.text = GetEnemyName();
        _nameLabel.transform.localPosition = new Vector3(0f, _labelOffsetY, -0.1f);
        _nameLabel.gameObject.SetActive(true);
    }

    private void HideLabel()
    {
        if (_nameLabel == null) return;
        _nameLabel.gameObject.SetActive(false);
    }

    public void Initialize(CellContent enemyType, int gridX = -1, int gridY = -1)
    {
        _enemyType = enemyType;
        _gridX = gridX;
        _gridY = gridY;
        _maxHP = GetMaxHP(enemyType);
        _currentHP = _maxHP;
        _originalPos = transform.position;

        _sr = GetComponent<SpriteRenderer>()
           ?? GetComponentInChildren<SpriteRenderer>();
        if (_sr != null) _originalColor = _sr.color;

        SetupHealthBar();
        CreateNameLabel();
        HideLabel();

        UpdateFacing();

        // Deleguer a EnemyBehaviour si present
        var beh = GetComponent<EnemyBehaviour>();
        if (beh != null)
        {
            if (enemyType == CellContent.Enemy_BanditArcher)
                beh.SubscribeArcherEvents();
            beh.StartBehaviour();
        }
        else
        {
            // Fallback sans EnemyBehaviour
            StartCoroutine(FallbackAttackDelay());
        }
    }

    private IEnumerator FallbackAttackDelay()
    {
        yield return WaitCache.Get(_attackDelay);
        if (!_isDead) EnemyAttack();
    }

    // =========================================================================
    // Clic joueur → attaque
    // =========================================================================

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_isDead) return;
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (_isAttacking) return;

        var heroState = HeroState.Instance;
        if (heroState != null) heroState.IsUILocked = true;

        var hero = HeroController.Instance;
        if (hero != null)
        {
            float cs = GridManager.Instance?.CellStep ?? 1.05f;
            int hx = Mathf.RoundToInt(hero.transform.position.x / cs);
            int hy = Mathf.RoundToInt(hero.transform.position.y / cs);
            int dist = Mathf.Max(Mathf.Abs(hx - _gridX), Mathf.Abs(hy - _gridY));
            if (dist > 1)
            {
                if (heroState != null) heroState.IsUILocked = false;
                return;
            }
        }

        var combat = HeroCombat.Instance;
        if (combat != null)
        {
            if (!combat.IsAnimating) combat.RequestAttack(this);
            else if (heroState != null) heroState.IsUILocked = false;
            return;
        }

        if (heroState != null) heroState.IsUILocked = false;
    }

    // =========================================================================
    // Reçoit une attaque du joueur
    // =========================================================================

    public void ReceiveAttack(int dmg)
    {
        if (_isDead) return;
        StartCoroutine(PlayerAttackSequence(dmg));
    }

    private IEnumerator PlayerAttackSequence(int dmg)
    {
        _isAnimating = true;
        _isAttacking = true;

        UpdateFacing();

        // Orienter le heros vers l ennemi qui contre-attaque
        var hero = HeroController.Instance;
        if (hero != null)
        {
            bool enemyOnRight = transform.position.x > hero.transform.position.x;
            hero.SetIdleFacing(enemyOnRight);
        }

        // Feedback
        StartCoroutine(HitFlash());
        StartCoroutine(HitShake());
        TakeDamage(dmg);

        if (!_isDead)
        {
            yield return WaitCache.Get(_attackDelay);
            var beh = GetComponent<EnemyBehaviour>();
            if (beh != null)
                yield return StartCoroutine(beh.CounterAttack());
            // Sans EnemyBehaviour : pas de contre-attaque automatique
            // FallbackAttackDelay gere l attaque initiale a la decouverte
        }

        _isAnimating = false;
        _isAttacking = false;
    }

    // =========================================================================
    // Attaque ennemie → héros
    // =========================================================================

    public void EnemyAttackDamage() => EnemyAttack();

    private void EnemyAttack()
    {
        if (_isDead) return;
        var gm = GameManager.Instance;
        if (gm == null || !gm.IsPlaying) return;
        var ps = PlayerStats.Instance;
        if (ps == null || !ps.IsAlive) return;

        int dmg = GetAttackDamage();
        int defense = Equipment.Instance?.Defense ?? 0;
        int finalDmg = Mathf.Max(0, dmg - defense);

        if (finalDmg > 0)
            EventBus.Publish(new OnPlayerDamaged
            { Damage = finalDmg, CurrentHP = 0, MaxHP = 0 });

        EventBus.Publish(new OnNotification
        {
            Message = GetEnemyName() + " attaque ! -" + finalDmg + " PV",
            Type = NotificationType.Damage
        });
    }

    // =========================================================================
    // Dégâts et mort
    // =========================================================================

    private void TakeDamage(int dmg)
    {
        _currentHP = Mathf.Max(0, _currentHP - dmg);
        UpdateHealthBar();

        EventBus.Publish(new OnNotification
        {
            Message = GetEnemyName() + " -" + dmg
                    + " PV (" + _currentHP + "/" + _maxHP + ")",
            Type = NotificationType.Info
        });

        if (_currentHP <= 0) Die();
    }

    private void Die()
    {
        if (_isDead) return;
        _isDead = true;

        int xp = GetXPValue();
        EventBus.Publish(new OnXPGained { Amount = xp });
        EventBus.Publish(new OnNotification
        {
            Message = GetEnemyName() + " vaincu ! +" + xp + " XP",
            Type = NotificationType.Item
        });

        // Actualiser les nombres dans la bonne grille (surface ou cave)
        if (CaveManager.Instance != null && CaveManager.Instance.IsInCave)
        {
            var ug = UndergroundGrid.Instance;
            if (ug != null) ug.OnEnemyDefeated(_gridX, _gridY);
        }
        else
        {
            var gm = GridManager.Instance;
            if (gm != null) gm.OnEnemyDefeated(_gridX, _gridY);
        }

        StartCoroutine(DeathAnimation());
    }

    // =========================================================================
    // Feedback visuel
    // =========================================================================

    private IEnumerator HitFlash()
    {
        if (_sr == null) yield break;
        _sr.color = Color.red;
        yield return WaitCache.Get(0.08f);
        _sr.color = _originalColor;
    }

    private IEnumerator HitShake()
    {
        for (int i = 0; i < 3; i++)
        {
            transform.position = _originalPos + new Vector3(0.06f, 0f, 0f);
            yield return WaitCache.Get(0.04f);
            transform.position = _originalPos + new Vector3(-0.06f, 0f, 0f);
            yield return WaitCache.Get(0.04f);
        }
        transform.position = _originalPos;
    }

    private IEnumerator DeathAnimation()
    {
        var beh = GetComponent<EnemyBehaviour>();
        // Desactiver comportement
        if (beh != null) beh.enabled = false;

        if (_sr != null)
        {
            float t = 0f;
            while (t < 0.4f)
            {
                t += Time.deltaTime;
                _sr.color = Color.Lerp(_originalColor, Color.clear, t / 0.4f);
                transform.localScale = Vector3.Lerp(Vector3.one, Vector3.zero, t / 0.4f);
                yield return null;
            }
        }
        Destroy(gameObject);
    }

    // =========================================================================
    // Hover
    // =========================================================================

    public void OnPointerEnter(PointerEventData e)
    {
        if (_isDead) return;
        if (_sr != null) _sr.color = Color.Lerp(_originalColor, Color.yellow, 0.3f);
        ShowLabel();
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (!_isDead && _sr != null)
            _sr.color = _originalColor;
        HideLabel();
    }

    // =========================================================================
    // Barre de vie
    // =========================================================================

    private void SetupHealthBar()
    {
        float cs = GridManager.Instance?.CellStep ?? 1.05f;
        float barW = cs * 0.85f;
        float barH = cs * 0.08f;

        // Fond
        var bgGO = new GameObject("HealthBarBG");
        bgGO.transform.SetParent(transform, false);
        bgGO.transform.localPosition = new Vector3(0f, cs * 0.52f, -0.02f);
        bgGO.transform.localScale = new Vector3(barW, barH, 1f);
        _healthBarBgRenderer = bgGO.AddComponent<SpriteRenderer>();
        _healthBarBgRenderer.sprite = GetWhiteSprite();
        _healthBarBgRenderer.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
        _healthBarBgRenderer.sortingLayerName = "CellContent";
        _healthBarBgRenderer.sortingOrder = 11;

        // Remplissage - ancre a gauche
        var fillGO = new GameObject("HealthBarFill");
        fillGO.transform.SetParent(transform, false);
        fillGO.transform.localPosition = new Vector3(0f, cs * 0.52f, -0.03f);
        fillGO.transform.localScale = new Vector3(barW, barH * 0.75f, 1f);
        _healthBarFillRenderer = fillGO.AddComponent<SpriteRenderer>();
        _healthBarFillRenderer.sprite = GetWhiteSprite();
        _healthBarFillRenderer.color = Color.green;
        _healthBarFillRenderer.sortingLayerName = "CellContent";
        _healthBarFillRenderer.sortingOrder = 12;

        _barFullWidth = barW;
        SetHealthBarVisible(false);
    }

    private float _barFullWidth = 1f;

    private void UpdateHealthBar()
    {
        if (_healthBarFillRenderer == null) return;
        float ratio = (float)_currentHP / _maxHP;

        // Reduire la largeur de la barre fill en gardant l ancrage gauche
        var s = _healthBarFillRenderer.transform.localScale;
        _healthBarFillRenderer.transform.localScale =
            new Vector3(_barFullWidth * ratio, s.y, s.z);

        // Decaler pour rester ancre a gauche
        float cs = GridManager.Instance?.CellStep ?? 1.05f;
        _healthBarFillRenderer.transform.localPosition = new Vector3(
            _barFullWidth * (ratio - 1f) * 0.5f,
            cs * 0.52f, -0.03f);

        _healthBarFillRenderer.color = Color.Lerp(Color.red, Color.green, ratio);
        SetHealthBarVisible(true);
    }

    private void SetHealthBarVisible(bool v)
    {
        if (_healthBarBgRenderer != null) _healthBarBgRenderer.enabled = v;
        if (_healthBarFillRenderer != null) _healthBarFillRenderer.enabled = v;
    }

    // Sprite blanc partagé — créé une seule fois pour TOUS les ennemis
    private static Sprite _sharedWhiteSprite;
    private static Sprite GetWhiteSprite()
    {
        if (_sharedWhiteSprite != null) return _sharedWhiteSprite;
        var tex = new Texture2D(2, 2);
        tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        _sharedWhiteSprite = Sprite.Create(tex,
            new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2f);
        return _sharedWhiteSprite;
    }

    // =========================================================================
    // Données par type
    // =========================================================================

    private int GetMaxHP(CellContent t)
    {
        // Lire depuis EnemyDatabase si disponible
        var db = EnemyDatabase.Instance;
        var data = db?.Get(t);
        if (data != null) return data.maxHP;

        // Fallback hardcode
        return t switch
        {
            CellContent.Enemy_Wolf => 2,
            CellContent.Enemy_Bear => 6,
            CellContent.Enemy_Mercenary => 3,
            CellContent.Enemy_BanditSword => 3,
            CellContent.Enemy_BanditArcher => 2,
            _ => 2
        };
    }

    private int GetAttackDamage()
    {
        var db = EnemyDatabase.Instance;
        var data = db?.Get(_enemyType);
        if (data != null) return data.attackDmg;
        return _enemyType switch
        {
            CellContent.Enemy_Bear => 2,
            CellContent.Enemy_Spider => 4,
            CellContent.Enemy_Bat => 3,
            CellContent.Enemy_GoblinLance => 6,
            CellContent.Enemy_GoblinMasse => 8,
            _ => 1
        };
    }

    private int GetXPValue()
    {
        var db = EnemyDatabase.Instance;
        var data = db?.Get(_enemyType);
        if (data != null) return data.xpReward;
        return _enemyType switch
        {
            CellContent.Enemy_Bear => 30,
            CellContent.Enemy_BanditSword => 20,
            CellContent.Enemy_BanditArcher => 25,
            CellContent.Enemy_Spider => 15,
            CellContent.Enemy_Bat => 12,
            CellContent.Enemy_GoblinLance => 25,
            CellContent.Enemy_GoblinMasse => 30,
            _ => 10
        };
    }

    private string GetEnemyName()
    {
        var db = EnemyDatabase.Instance;
        var data = db?.Get(_enemyType);
        if (data != null && !string.IsNullOrEmpty(data.displayName))
            return data.displayName;
        return _enemyType switch
        {
            CellContent.Enemy_Wolf => "Loup",
            CellContent.Enemy_Bear => "Ours",
            CellContent.Enemy_Mercenary => "Mercenaire",
            CellContent.Enemy_BanditSword => "Bandit",
            CellContent.Enemy_BanditArcher => "Archer",
            CellContent.Enemy_Spider => "Araignée",
            CellContent.Enemy_Bat => "Chauve-Souris",
            CellContent.Enemy_GoblinLance => "Goblin Lance",
            CellContent.Enemy_GoblinMasse => "Goblin Masse",
            _ => "Ennemi"
        };
    }
}
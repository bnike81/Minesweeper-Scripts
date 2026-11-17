using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// EnemyInstance - Stats, HP, clic joueur, mort, feedback visuel.
/// Les animations sont dans EnemyAnimator.
/// Les comportements IA sont dans EnemyBehaviour.
///
/// FIX v5 :
///   PlayerAttackSequence — quand TurnManager est actif, la contre-attaque
///   legacy (beh.CounterAttack) est SUPPRIMEE. Le TurnManager gere les
///   attaques ennemies via EnemyTurnSequence → TakeTurn.
///   Cela elimine les doubles attaques et les blocages de mouvement.
/// </summary>
public class EnemyInstance : MonoBehaviour,
    IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    // Registre statique — remplace FindObjectsOfType<EnemyInstance>()
    public static readonly System.Collections.Generic.List<EnemyInstance> All
        = new System.Collections.Generic.List<EnemyInstance>();

    private void Awake()
    {
        if (!All.Contains(this)) All.Add(this);

        // v13 Jour 11 : init FSM
        _sm = new EnemyStateMachine(gameObject.name);
        _sm.OnPhaseChanged += OnEnemyPhaseChanged;
    }

    /// v13 Jour 11 FIX : callback passif — ne synchronise PAS les flags legacy.
    /// Les flags (_isDead, _isAnimating, _isAttacking) restent controles par
    /// leur code d origine (Die, HitShake, attack handlers, etc.).
    /// Ce callback est reserve au logging/debug futur.
    private void OnEnemyPhaseChanged(EnemyPhase prev, EnemyPhase next)
    {
        // Pas d effet de bord. La FSM est passive — un observer.
        // Pour tracer un evenement specifique de phase, ajouter ici.
    }
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

    // v13 Jour 11 : machine d etat
    private EnemyStateMachine _sm;
    public EnemyStateMachine SM => _sm;
    public EnemyPhase CurrentPhase => _sm != null ? _sm.CurrentPhase : EnemyPhase.NotSpawned;

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

    /// v13 Jour 11 : transition courte vers une phase donnee.
    public bool TryTransition(EnemyPhase phase, string reason = "")
    {
        return _sm != null && _sm.TryTransitionTo(phase, reason);
    }
    public int GetDamage() => GetAttackDamage();

    /// <summary>
    /// Met a jour la position grille de l ennemi (appele par EnemyBehaviour.MoveTowardHero).
    /// </summary>
    public void SetGridPosition(int x, int y)
    {
        _gridX = x;
        _gridY = y;
        _originalPos = transform.position;
    }

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
        // v13 Jour 11 : passage NotSpawned → Idle apres init
        if (_sm != null) _sm.TryTransitionTo(EnemyPhase.Idle, "EnemyInstance.Setup");
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

        // ── FIX v6 : empecher le clic pendant le tour ennemi ──
        var tm = TurnManager.Instance;
        if (tm != null && tm.IsActive && !tm.IsHeroTurn)
        {
            Debug.Log("<color=#00AAFF>[EnemyInstance]</color> Clic BLOQUE — pas le tour du heros");
            return;
        }

        var heroState = HeroState.Instance;
        var hero = HeroController.Instance;
        if (hero == null) return;

        // Calcul distance Chebyshev via la grille active (offset-aware)
        var grid = ActiveGrid.Current;
        Vector2Int heroGrid;
        if (grid != null)
            heroGrid = grid.WorldToGrid(hero.transform.position);
        else
        {
            float cs = GridManager.Instance?.CellStep ?? 1.05f;
            heroGrid = new Vector2Int(
                Mathf.RoundToInt(hero.transform.position.x / cs),
                Mathf.RoundToInt(hero.transform.position.y / cs));
        }
        int dist = Mathf.Max(
            Mathf.Abs(heroGrid.x - _gridX),
            Mathf.Abs(heroGrid.y - _gridY));

        Debug.Log($"<color=#FF8800>[EI.OnPointerClick]</color> " +
                  $"hero=({heroGrid.x},{heroGrid.y}) enemy=({_gridX},{_gridY}) " +
                  $"dist={dist} state={tm?.State}");

        // ─── dist 1 : attaque directe ───
        if (dist == 1)
        {
            Debug.Log("<color=#FF8800>[EI.OnPointerClick]</color> → RequestAttack direct (dist=1)");
            var combat = HeroCombat.Instance;
            if (combat != null && !combat.IsAnimating)
            {
                if (heroState != null) heroState.IsUILocked = true;
                combat.RequestAttack(this);
            }
            return;
        }

        // ─── dist 2 : deplacement + attaque ───
        if (dist == 2)
        {
            Debug.Log("<color=#FF8800>[EI.OnPointerClick]</color> → MoveToAndAttack (dist=2)");
            // Le pathfinder va chercher la case adjacente a l ennemi la plus proche,
            // deplacer le hero, puis attaquer.
            hero.MoveToAndAttack(this);
            return;
        }

        // ─── dist > 2 : trop loin ───
        Debug.Log("<color=#FF8800>[EI.OnPointerClick]</color> → Trop loin (dist>2)");
        EventBus.Publish(new OnNotification
        {
            Message = "Trop loin !",
            Type = NotificationType.Warning
        });
    }

    // =========================================================================
    // Recoit une attaque du joueur
    // =========================================================================

    public void ReceiveAttack(int dmg)
    {
        if (_isDead) return;
        StartCoroutine(PlayerAttackSequence(dmg));
    }

    /// Sequence de degats quand le heros attaque cet ennemi.
    /// v5  : pas de contre-attaque legacy quand TurnManager actif.
    /// v11 : HitShake + HitFlash en parallele, MAIS yieldees (attendre la fin).
    ///       Feedback visuel garanti pour TOUS les ennemis (crabe, ours, etc.)
    
    private IEnumerator PlayerAttackSequence(int dmg)
    {
        _isAnimating = true;
        _isAttacking = true;

        UpdateFacing();

        // Orienter le heros vers l ennemi
        var hero = HeroController.Instance;
        if (hero != null)
        {
            bool enemyOnRight = transform.position.x > hero.transform.position.x;
            hero.SetIdleFacing(enemyOnRight);
        }

        // v11 : feedback visuel — flash + shake en parallele, mais attendus
        var flashCo = StartCoroutine(HitFlash());
        var shakeCo = StartCoroutine(HitShake());
        TakeDamage(dmg);

        // Attendre que le shake soit fini (le plus long des deux)
        yield return shakeCo;

        if (!_isDead)
        {
            var tm = TurnManager.Instance;
            if (tm != null && tm.IsActive)
            {
                Debug.Log("<color=#00AAFF>[EnemyInstance]</color> " + GetEnemyName()
                    + " — contre-attaque legacy IGNOREE (TurnManager actif)");
                yield return WaitCache.Get(0.15f);
            }
            else
            {
                yield return WaitCache.Get(_attackDelay);
                var beh = GetComponent<EnemyBehaviour>();
                if (beh != null)
                    yield return StartCoroutine(beh.CounterAttack());
            }
        }

        _isAnimating = false;
        _isAttacking = false;
    }

    // =========================================================================
    // Attaque ennemie → heros
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

        // ─── v13 FIX : publier TOUJOURS pour feedback visuel ───
        // Meme si finalDmg=0 (defense totale), on publie pour que :
        //  - HeroCombat joue l animation hurt (flinch)
        //  - HeartBarUI shake meme sans perte de PV
        //  - La notification s affiche
        EventBus.Publish(new OnPlayerDamaged
        {
            Damage = finalDmg,
            CurrentHP = 0,
            MaxHP = 0
        });

        string msg = finalDmg > 0
            ? GetEnemyName() + " attaque ! -" + finalDmg + " PV"
            : GetEnemyName() + " attaque ! (bloque)";

        EventBus.Publish(new OnNotification
        {
            Message = msg,
            Type = NotificationType.Damage
        });
    }

    // =========================================================================
    // Degats et mort
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

        // v13 Jour 11 FIX : transition FSM APRES avoir lance l animation,
        // pour ne pas risquer d interrompre quoi que ce soit.
        if (_sm != null) _sm.TryTransitionTo(EnemyPhase.Dead, "EnemyInstance.Die");
    }

    /// <summary>
    /// Notifie la grille active que cet ennemi est vaincu.
    /// Fonctionne pour MainGrid, UndergroundGrid, ArenaGrid et future grid.
    /// </summary>
    private void NotifyEnemyDefeatedToActiveGrid()
    {
        var active = ActiveGrid.Current;
        if (active == null) return;

        // Marquer la case Empty dans la grille active
        var cell = active.GetCell(_gridX, _gridY);
        if (cell != null) cell.Content = CellContent.Empty;

        // Dispatcher selon le type concret (OnEnemyDefeated n est pas dans IGridContext)
        switch (active)
        {
            case GridManager gm:
                gm.OnEnemyDefeated(_gridX, _gridY);
                break;
            case UndergroundGrid ug:
                ug.OnEnemyDefeated(_gridX, _gridY);
                break;
            case ArenaGrid ag:
                ag.OnEnemyDefeated(_gridX, _gridY);
                break;
            // IndoorTileMapGrid : actuellement pas de combat Indoor → rien
            default:
                Debug.Log($"<color=#FFAA00>[EnemyInstance]</color> " +
                          $"Pas de OnEnemyDefeated pour grille {active.GetType().Name}");
                break;
        }
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

    /// Shake de recul quand l ennemi recoit des degats.
    /// v11 : amplitude augmentee pour meilleure lisibilite pixel art.
   
    private IEnumerator HitShake()
    {
        var orig = _originalPos;
        float amp = 0.10f; // v11 : augmente de 0.06 → 0.10
        for (int i = 0; i < 4; i++) // v11 : 4 iterations au lieu de 3
        {
            transform.position = orig + new Vector3(amp, 0f, 0f);
            yield return WaitCache.Get(0.04f);
            transform.position = orig + new Vector3(-amp, 0f, 0f);
            yield return WaitCache.Get(0.04f);
        }
        transform.position = orig;
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

    private readonly List<GameObject> _perceptionOverlays = new List<GameObject>();
    private static Sprite _sharedOverlaySprite;
    public void OnPointerEnter(PointerEventData e)
    {
        if (_isDead) return;
        if (_sr != null) _sr.color = Color.Lerp(_originalColor, Color.yellow, 0.3f);
        ShowLabel();
        ShowPerceptionOverlay();
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (!_isDead && _sr != null)
            _sr.color = _originalColor;
        HideLabel();
        HidePerceptionOverlay();
    }

    /// Affiche un overlay rouge semi-transparent sur les cases
    /// dans le rayon de perception de l ennemi (Manhattan distance).
    /// Utilise perceptionFogColor de EnemyDatabase.
    private void ShowPerceptionOverlay()
    {
        HidePerceptionOverlay();

        var data = EnemyDatabase.Instance?.Get(EnemyType);
        int radius = data?.perceptionRadius ?? 3;
        Color overlayColor = data?.perceptionFogColor
            ?? new Color(1f, 0f, 0f, 0.15f);

        // Creer le sprite partage si necessaire
        if (_sharedOverlaySprite == null)
        {
            var tex = new Texture2D(4, 4);
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = Color.white;
            tex.SetPixels(px);
            tex.Apply();
            _sharedOverlaySprite = Sprite.Create(
                tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        }

        float cellSize = GridManager.Instance?.CellStep ?? 1.05f;

        // ─── v13 FIX FINAL : utiliser WorldToGrid pour obtenir la case
        // reellement sous le sprite, en tenant compte de l offset grille ───
        var activeGrid = ActiveGrid.Current;

        int centerX, centerY;
        if (activeGrid != null)
        {
            var centerGrid = activeGrid.WorldToGrid(transform.position);
            centerX = centerGrid.x;
            centerY = centerGrid.y;

            // Securite : si hors bornes, retomber sur les coords logiques
            if (!activeGrid.IsInBounds(centerX, centerY))
            {
                centerX = _gridX;
                centerY = _gridY;
            }
        }
        else
        {
            centerX = _gridX;
            centerY = _gridY;
        }
        // ─── fin FIX ───

        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int manhattan = Mathf.Abs(dx) + Mathf.Abs(dy);
                if (manhattan > radius) continue;
                if (dx == 0 && dy == 0) continue;

                int cx = centerX + dx;
                int cy = centerY + dy;

                if (activeGrid == null) continue;
                if (!activeGrid.IsInBounds(cx, cy)) continue;

                var cell = activeGrid.GetCell(cx, cy);
                if (cell == null || !cell.IsRevealed) continue;

                // Ne pas overlay les cases montagne/eau
                if (cell.IsMountainReserved || cell.IsCoastWater) continue;

                // Ne pas overlay les murs cave
                if (activeGrid is UndergroundGrid ug && !ug.IsFloor(cx, cy)) continue;

                // ─── v13 FIX : utiliser GridToWorld pour appliquer l offset ───
                Vector3 worldPos = activeGrid.GridToWorld(cx, cy, -0.04f);

                var go = new GameObject("PerceptionOverlay");
                go.transform.position = worldPos;
                go.transform.localScale = new Vector3(cellSize, cellSize, 1f);

                var overlaysr = go.AddComponent<SpriteRenderer>();
                overlaysr.sprite = _sharedOverlaySprite;
                overlaysr.color = overlayColor;
                overlaysr.sortingLayerName = "CellContent";
                overlaysr.sortingOrder = 5;

                _perceptionOverlays.Add(go);
            }
        }
    }
    private void HidePerceptionOverlay()

    {
        for (int i = _perceptionOverlays.Count - 1; i >= 0; i--)
        {
            if (_perceptionOverlays[i] != null)
                Destroy(_perceptionOverlays[i]);
        }
        _perceptionOverlays.Clear();
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

    // Sprite blanc partage — cree une seule fois pour TOUS les ennemis
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
    // Donnees par type
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
            CellContent.Enemy_Spider => "Araignee",
            CellContent.Enemy_Bat => "Chauve-Souris",
            CellContent.Enemy_GoblinLance => "Goblin Lance",
            CellContent.Enemy_GoblinMasse => "Goblin Masse",
            _ => "Ennemi"
        };
    }
}
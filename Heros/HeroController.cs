using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// HeroController - Input, mouvement fluide, sprites.
/// Le pathfinding est delegue a HeroPathfinder.
///
/// CORRECTIONS CAVE (chapitre 3) :
///   1. Update() utilise ActiveGrid.Current.WorldToGrid() pour la conversion
///      des clics souris — tient compte du _worldOriginY de l'UndergroundGrid.
///   2. GridToWorld() utilise ActiveGrid.Current.GridToWorld() pour le mouvement
///      fluide — le héros se déplace dans le bon espace monde en cave.
///   3. PathfindAndMove/ApproachAndReveal/NavigateLoop acceptent IGridContext
///      au lieu de GridManager — fonctionnent sur surface ET en cave.
/// </summary>
public class HeroController : MonoBehaviour
{
    public static HeroController Instance { get; private set; }

    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------

    [Header("=== Sprites Idle ===")]
    [SerializeField] private Sprite _idleFront;
    [SerializeField] private Sprite _idleBack;
    [SerializeField] private Sprite _idleRight;
    [SerializeField] private Sprite _idleLeft;

    [Header("=== Sprites Course (6 frames) ===")]
    [SerializeField] private Sprite[] _runFront = new Sprite[6];
    [SerializeField] private Sprite[] _runBack = new Sprite[6];
    [SerializeField] private Sprite[] _runRight = new Sprite[6];
    [SerializeField] private Sprite[] _runLeft = new Sprite[6];

    [Header("=== Mouvement ===")]
    [SerializeField, Range(1f, 12f)] private float _moveSpeed = 5f;
    [SerializeField, Range(0.04f, 0.2f)] private float _frameTime = 0.08f;

    [Header("=== Debug ===")]
    [SerializeField] private bool _debugPath = false;

    // -------------------------------------------------------------------------
    // Etat
    // -------------------------------------------------------------------------

    private static readonly List<HeroMoveSegment> _moveSegments = new List<HeroMoveSegment>(8);
    private static readonly Collider2D[] _physicsBuffer = new Collider2D[32];

    private SpriteRenderer _sr;
    private Vector2Int _gridPos;
    private Vector2Int _lastDir = Vector2Int.down;
    private float _cellStep = 1.05f;
    private int _runFrame = 0;
    private float _frameTimer = 0f;
    private Mouse _mouse;
    private Camera _cam;
    private HeroState _state;
    private HeroPathfinder _pf;

    // -------------------------------------------------------------------------
    // Proprietes publiques
    // -------------------------------------------------------------------------

    public Vector2Int GridPosition => _gridPos;
    public Vector2Int LastDir => _lastDir;
    public bool HasAppeared => _state != null && _state.HasAppeared;
    public bool IsMoving => _state != null && _state.IsMoving;
    public bool IsLocked
    {
        get => _state != null && _state.IsUILocked;
        set { if (_state != null) _state.IsUILocked = value; }
    }

    public void SetGridPosition(Vector2Int pos) => _gridPos = pos;
    public void RestoreIdleSprite() => SetIdleSprite();

    /// <summary>
    /// Téléporte le héros à une position (cave ou retour main grid).
    /// Appelé par CaveManager.EnterCave() et CaveManager.ExitCave().
    /// </summary>
    public void TeleportToCave(int gridX, int gridY, Vector3 worldPos)
    {
        _gridPos = new Vector2Int(gridX, gridY);
        transform.position = worldPos;
        _state.IsMoving = false;
        _state.IsCalculating = false;
        _state.IsUILocked = false;
        _activeTrap = null;
        StopAllCoroutines();
        SetIdleSprite();
    }

    /// <summary>
    /// Déplace le héros vers une case adjacente à l'entrée cave.
    /// </summary>
    public void ApproachCaveEntrance(int entranceGridX, int entranceGridY)
    {
        if (_state == null || !_state.CanMove) return;
        var gm = GridManager.Instance;
        if (gm == null) return;
        var target = new Vector2Int(entranceGridX, entranceGridY);
        _state.CollectOnArrival = false;
        StartCoroutine(ApproachAndReveal(_gridPos, target, gm));
    }

    // ── Grille active ─────────────────────────────────────────────────────────
    private IGridContext GetActiveGrid() => ActiveGrid.Current;

    public void SetIdleFacing(bool faceRight)
    {
        _lastDir = faceRight ? Vector2Int.right : Vector2Int.left;
        SetIdleSprite();
    }

    public void SetIdleSprite()
    {
        if (_sr == null) return;
        var d = DominantDir(_lastDir);
        Sprite s = d == Vector2Int.down ? _idleFront
                 : d == Vector2Int.up ? _idleBack
                 : d == Vector2Int.right ? _idleRight : _idleLeft;
        if (s != null) _sr.sprite = s;
    }

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _sr = GetComponent<SpriteRenderer>();
        _mouse = Mouse.current;
        _cam = Camera.main ?? FindObjectOfType<Camera>();
        _cellStep = GridManager.Instance?.CellStep ?? 1.05f;
        _state = HeroState.Instance ?? FindObjectOfType<HeroState>();
        _pf = HeroPathfinder.Instance ?? FindObjectOfType<HeroPathfinder>();

        if (_state == null) Debug.LogError("[Hero] HeroState manquant sur le GO!");
        if (_pf == null) Debug.LogError("[Hero] HeroPathfinder manquant sur le GO!");

        if (_sr != null) _sr.enabled = false;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnGridGenerated>(OnGridGen);
        EventBus.Subscribe<OnFirstClick>(OnFirstClick);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnGridGenerated>(OnGridGen);
        EventBus.Unsubscribe<OnFirstClick>(OnFirstClick);
    }

    private void OnGridGen(OnGridGenerated e)
    {
        _cellStep = GridManager.Instance?.CellStep ?? _cellStep;
        if (_state != null && _state.HasAppeared)
            transform.position = GridToWorld(_gridPos);
    }

    private void OnFirstClick(OnFirstClick e)
    {
        if (_state == null || _state.HasAppeared) return;
        _state.HasAppeared = true;
        _gridPos = new Vector2Int(e.X, e.Y);
        transform.position = GridToWorld(_gridPos);
        if (_sr != null) _sr.enabled = true;
        SetIdleSprite();
    }

    // =========================================================================
    // INPUT
    // =========================================================================

    private void Update()
    {
        if (_state == null || !_state.HasAppeared) return;
        if (_mouse == null) { _mouse = Mouse.current; return; }
        if (!_mouse.leftButton.wasPressedThisFrame) return;
        if (IsOverUI()) return;

        // Piege actif - clics pour se debattre
        if (_activeTrap != null)
        {
            bool still = _activeTrap.OnHeroStruggle();
            if (!still)
            {
                _activeTrap = null;
                _state.IsUILocked = false;
                _state.IsMoving = false;
                _state.IsCalculating = false;
            }
            return;
        }

        if (!_state.CanMove) return;

        if (_pf == null)
        {
            _pf = HeroPathfinder.Instance ?? FindObjectOfType<HeroPathfinder>();
            if (_pf == null) { Debug.LogError("[Hero] HeroPathfinder null!"); return; }
        }

        if (_cam == null) _cam = Camera.main ?? FindObjectOfType<Camera>();
        if (_cam == null) return;

        float camZ = Mathf.Abs(_cam.transform.position.z);
        var sp = _mouse.position.ReadValue();
        var wp = _cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, camZ));

        // ── CORRECTION 1 ─────────────────────────────────────────────────────
        // Utiliser la grille ACTIVE pour convertir les coords monde → grille.
        // En cave, ActiveGrid.Current est UndergroundGrid — sa méthode WorldToGrid
        // soustrait _worldOriginY et donne les bonnes coordonnées locales.
        // En surface, ActiveGrid.Current est GridManager — comportement identique à avant.
        var activeGrid = ActiveGrid.Current;
        if (activeGrid == null) return;

        var targetGridPos = activeGrid.WorldToGrid(wp);
        int tx = targetGridPos.x;
        int ty = targetGridPos.y;

        if (!activeGrid.IsInBounds(tx, ty)) return;

        var target = new Vector2Int(tx, ty);
        if (target == _gridPos) return;

        var cell = activeGrid.GetCell(tx, ty);

        // En cave : bloquer les murs (pas les ennemis — on veut les attaquer)
        // En surface : bloquer les ennemis visibles via HasEnemyAt (comportement original)
        bool isCave = activeGrid is UndergroundGrid;
        if (isCave)
        {
            // Mur cave → ignorer le clic (non cliquable, géré par UndergroundCellView)
            if (activeGrid.IsBlocked(tx, ty))
            {
                var caveCell = activeGrid.GetCell(tx, ty);
                // Si c'est un ennemi révélé → laisser passer (on veut l'attaquer)
                bool isRevealedEnemy = caveCell != null && caveCell.IsRevealed && caveCell.IsEnemy;
                if (!isRevealedEnemy) return;
            }
        }
        else
        {
            if (HasEnemyAt(tx, ty)) return;
        }

        // Montagne : bloquer sur les cases montagne (surface uniquement)
        if (cell != null && cell.IsMountainReserved) return;

        // Combat : rengainer avant de bouger
        var combat = HeroCombat.Instance;
        if (combat != null && combat.IsInCombat)
        {
            // Capturer activeGrid pour la lambda (évite closure sur variable loop)
            var gridCapture = activeGrid;
            combat.RequestFlee(() =>
            {
                if (cell == null || !cell.IsRevealed)
                    StartCoroutine(ApproachAndReveal(_gridPos, target, gridCapture));
                else
                {
                    _state.CollectOnArrival = HeroInteraction.Instance != null
                        && HeroInteraction.Instance.HasGroundItemAt(target);
                    StartCoroutine(PathfindAndMove(_gridPos, target, gridCapture));
                }
            });
            return;
        }

        if (cell == null || !cell.IsRevealed)
        {
            _state.CollectOnArrival = false;
            StartCoroutine(ApproachAndReveal(_gridPos, target, activeGrid));
            return;
        }

        _state.CollectOnArrival = HeroInteraction.Instance != null
                               && HeroInteraction.Instance.HasGroundItemAt(target);
        StartCoroutine(PathfindAndMove(_gridPos, target, activeGrid));
    }

    // =========================================================================
    // DEPLACEMENT VERS CASE REVELEE
    // IGridContext au lieu de GridManager — fonctionne surface ET cave
    // =========================================================================

    private IEnumerator PathfindAndMove(Vector2Int start, Vector2Int target, IGridContext gm)
    {
        if (_state.IsCalculating) yield break;
        if (_pf == null)
        {
            _pf = HeroPathfinder.Instance ?? FindObjectOfType<HeroPathfinder>();
            if (_pf == null) { Debug.LogError("[Hero] HeroPathfinder null!"); yield break; }
        }
        _state.IsCalculating = true;

        _pf.BuildBlockedCache();

        // En cave : utiliser gm.IsBlocked() directement — respecte murs ET ennemis,
        // pas le check IsRevealed de HeroPathfinder (inutile en cave, pas d'arbres)
        bool inCave = gm is UndergroundGrid;
        Vector2Int adj;
        if (inCave)
        {
            adj = gm.IsBlocked(target.x, target.y)
                ? FindAdjacentFreeCave(target, gm)
                : target;
        }
        else
        {
            adj = _pf.IsBlocked(target, gm, _gridPos)
                ? _pf.FindNearestFree(target, start, gm)
                : target;
        }
        if (adj == start) { _state.IsCalculating = false; yield break; }

        yield return null;

        List<Vector2Int> path;
        if (inCave)
            // A* cave : seul gm.IsBlocked() bloque (murs + ennemis révélés, pas IsRevealed)
            path = RunAStarCave(start, adj, gm);
        else
            path = _pf.RunAStar(start, adj, gm, _gridPos);

        if (path != null && path.Count > 0)
        {
            _state.IsCalculating = false;
            var segs = BuildSegments(start, path);
            yield return StartCoroutine(MoveAlongSegments(segs));
            yield break;
        }

        _state.IsCalculating = false;
        yield return StartCoroutine(NavigateLoop(start, adj, gm));
    }

    private IEnumerator NavigateLoop(Vector2Int start, Vector2Int target, IGridContext gm)
    {
        if (_pf == null)
        {
            _pf = HeroPathfinder.Instance ?? FindObjectOfType<HeroPathfinder>();
            if (_pf == null) yield break;
        }
        var current = start;
        int maxJumps = 5;
        int jumps = 0;

        if (!_pf.IsRevealed(current, gm))
        {
            var near = _pf.FindNearestRevealedFrom(current, gm, current);
            if (near.HasValue)
            {
                var j = HeroJump.Instance;
                if (j != null)
                {
                    j.TryJumpToRevealed(current, near.Value, gm);
                    yield return new WaitUntil(() => !j.IsJumping);
                    current = _gridPos;
                }
            }
        }

        while (current != target && jumps <= maxJumps)
        {
            _pf.BuildBlockedCache();
            var path = _pf.RunAStar(current, target, gm, current);

            if (path != null && path.Count > 0)
            {
                var segs = BuildSegments(current, path);
                yield return StartCoroutine(MoveAlongSegments(segs));
                break;
            }

            if (_debugPath) Debug.Log("[Hero] Pas de chemin depuis " + current);

            var landing = _pf.FindJumpLanding(current, target, gm, current);
            if (!landing.HasValue)
            {
                _pf.LogNoJump(current, target, gm);
                EventBus.Publish(new OnNotification
                {
                    Message = "Inaccessible depuis ici !",
                    Type = NotificationType.Warning
                });
                break;
            }

            var jump = HeroJump.Instance;
            if (jump == null) break;

            var launchpad = _pf.PendingLaunchpad;
            if (launchpad.HasValue && launchpad.Value != current)
            {
                Debug.Log("[Hero] Courir vers launchpad " + launchpad.Value);
                var lpPath = _pf.RunAStar(current, launchpad.Value, gm, current);
                if (lpPath != null && lpPath.Count > 0)
                {
                    yield return StartCoroutine(MoveAlongSegments(BuildSegments(current, lpPath)));
                    current = _gridPos;
                    _pf.BuildBlockedCache();
                }
            }
            else if (Mathf.Max(Mathf.Abs(landing.Value.x - current.x),
                               Mathf.Abs(landing.Value.y - current.y)) > jump.MaxJumpDistance)
            {
                var lp = _pf.FindLaunchPadFor(current, landing.Value, gm,
                    current, jump.MaxJumpDistance);
                if (lp.HasValue && lp.Value != current)
                {
                    var lpPath = _pf.RunAStar(current, lp.Value, gm, current);
                    if (lpPath != null && lpPath.Count > 0)
                    {
                        yield return StartCoroutine(MoveAlongSegments(BuildSegments(current, lpPath)));
                        current = _gridPos;
                        _pf.BuildBlockedCache();
                    }
                }
            }

            Debug.Log("[Hero] Saut auto vers " + landing.Value);
            bool jumped = jump.TryJumpToRevealed(current, landing.Value, gm);
            if (!jumped) { Debug.Log("[Hero] Saut refuse"); break; }

            yield return new WaitUntil(() => !jump.IsJumping);
            current = _gridPos;
            _pf.BuildBlockedCache();
            jumps++;
            yield return null;
        }
    }

    // =========================================================================
    // APPROCHE + DECOUVERTE (case non revelee)
    // IGridContext au lieu de GridManager
    // =========================================================================

    private IEnumerator ApproachAndReveal(Vector2Int start, Vector2Int target, IGridContext gm)
    {
        if (_pf == null)
        {
            _pf = HeroPathfinder.Instance ?? FindObjectOfType<HeroPathfinder>();
            if (_pf == null) yield break;
        }

        // ── CAVE : A* direct vers case adjacente libre ────────────────────────
        if (gm is UndergroundGrid)
        {
            _pf.BuildBlockedCache();
            var adj = FindAdjacentFreeCave(target, gm);
            if (adj == _gridPos) yield break;

            var path = RunAStarCave(start, adj, gm);
            if (path != null && path.Count > 0)
                yield return StartCoroutine(MoveAlongSegments(BuildSegments(start, path)));

            yield return WaitCache.Get(0.05f);

            int d2 = Mathf.Max(Mathf.Abs(target.x - _gridPos.x),
                                Mathf.Abs(target.y - _gridPos.y));
            if (d2 <= 1)
            {
                var result = gm.RevealCell(target.x, target.y);
                // Rafraîchir TOUTES les vues pour que l'ennemi soit spawné
                // via ShowRevealed → SpawnEnemyInstance → Initialize → FallbackAttackDelay
                UndergroundCellView.RefreshAllViews();

                // Si ennemi révélé → NE PAS avancer sur la case (rester adjacent)
                var revealedCell = gm.GetCell(target.x, target.y);
                if (revealedCell != null && (revealedCell.IsEnemy || revealedCell.IsBoss))
                {
                    // L'ennemi est spawné, FallbackAttackDelay lance la première attaque
                    // Le héros reste adjacent — cliquer sur l'ennemi pour attaquer
                    SetIdleSprite();
                }
                else if (result != RevealResult.AlreadyRevealed)
                {
                    // Case vide ou nombre → avancer dessus
                    _state.IsMoving = true;
                    var from = GridToWorld(_gridPos);
                    var to = GridToWorld(target);
                    float dur = Vector3.Distance(from, to) / _moveSpeed;
                    float elapsed = 0f;
                    _lastDir = DominantDir(target - _gridPos);

                    while (elapsed < dur)
                    {
                        elapsed += Time.deltaTime;
                        transform.position = Vector3.Lerp(from, to, Mathf.Clamp01(elapsed / dur));
                        _frameTimer += Time.deltaTime;
                        if (_frameTimer >= _frameTime)
                        { _frameTimer = 0f; _runFrame = (_runFrame + 1) % 6; ApplyRunSprite(_lastDir, _runFrame); }
                        yield return null;
                    }

                    transform.position = to;
                    _gridPos = target;
                    _state.IsMoving = false;
                    _runFrame = 0;
                    SetIdleSprite();
                }
            }
            yield break;
        }

        // ── SURFACE : logique originale ───────────────────────────────────────
        var launchPad = _pf.FindBestLaunchPad(start, target, gm);

        if (!launchPad.HasValue)
        {
            EventBus.Publish(new OnNotification
            {
                Message = "Inaccessible depuis ici !",
                Type = NotificationType.Warning
            });
            yield break;
        }

        var lp = launchPad.Value;

        if (lp != start)
        {
            var path = _pf.RunAStar(start, lp, gm, _gridPos);
            if (path != null && path.Count > 0)
                yield return StartCoroutine(MoveAlongSegments(BuildSegments(start, path)));
        }

        yield return WaitCache.Get(0.06f);

        int distToTarget = Mathf.Max(
            Mathf.Abs(target.x - _gridPos.x),
            Mathf.Abs(target.y - _gridPos.y));

        if (distToTarget <= 1)
        {
            var result = gm.RevealCell(target.x, target.y);
            var cell = gm.GetCell(target.x, target.y);
            bool hasEnemy = cell != null && (cell.IsEnemy || cell.IsBoss);

            if (!hasEnemy)
            {
                _state.IsMoving = true;
                var from = GridToWorld(_gridPos);
                var to = GridToWorld(target);
                float dur = Vector3.Distance(from, to) / _moveSpeed;
                float elapsed = 0f;
                _lastDir = DominantDir(target - _gridPos);

                while (elapsed < dur)
                {
                    elapsed += Time.deltaTime;
                    transform.position = Vector3.Lerp(from, to, Mathf.Clamp01(elapsed / dur));
                    _frameTimer += Time.deltaTime;
                    if (_frameTimer >= _frameTime)
                    {
                        _frameTimer = 0f;
                        _runFrame = (_runFrame + 1) % 6;
                        ApplyRunSprite(_lastDir, _runFrame);
                    }
                    yield return null;
                }

                transform.position = to;
                _gridPos = target;
                _state.IsMoving = false;
                _runFrame = 0;
                SetIdleSprite();

                if (HeroInteraction.Instance != null)
                {
                    var trap = HeroInteraction.Instance.CheckTrap(_gridPos);
                    if (trap != null) _activeTrap = trap;
                }
                if (_activeTrap == null)
                    HeroInteraction.Instance?.TryCollect(_gridPos);
            }
        }
        else if (distToTarget <= 3)
        {
            HeroJump.Instance?.TryJump(_gridPos, target, gm);
        }
        else
        {
            EventBus.Publish(new OnNotification
            {
                Message = "Trop loin pour sauter !",
                Type = NotificationType.Warning
            });
        }
    }

    // =========================================================================
    // MOUVEMENT FLUIDE
    // =========================================================================

    private IEnumerator MoveAlongSegments(List<HeroMoveSegment> segments)
    {
        _state.IsMoving = true;

        foreach (var seg in segments)
        {
            var from = GridToWorld(seg.From);
            var to = GridToWorld(seg.To);
            float dist = Vector3.Distance(from, to);
            float dur = Mathf.Max(dist / _moveSpeed, 0.01f);
            float elapsed = 0f;

            _lastDir = seg.SpriteDir;

            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                transform.position = Vector3.Lerp(from, to, Mathf.Clamp01(elapsed / dur));
                _frameTimer += Time.deltaTime;
                if (_frameTimer >= _frameTime)
                {
                    _frameTimer = 0f;
                    _runFrame = (_runFrame + 1) % 6;
                    ApplyRunSprite(seg.SpriteDir, _runFrame);
                }
                yield return null;
            }

            transform.position = to;
            _gridPos = seg.To;
        }

        _state.IsMoving = false;
        _runFrame = 0;
        SetIdleSprite();

        if (HeroInteraction.Instance != null)
        {
            var trap = HeroInteraction.Instance.CheckTrap(_gridPos);
            if (trap != null) _activeTrap = trap;
        }
        if (_activeTrap == null && _state.CollectOnArrival)
            HeroInteraction.Instance?.TryCollect(_gridPos);

        CheckAdjacentNPC();
    }

    private List<HeroMoveSegment> BuildSegments(Vector2Int start, List<Vector2Int> path)
    {
        _moveSegments.Clear();
        var segs = _moveSegments;
        var cur = start;
        int i = 0;

        while (i < path.Count)
        {
            var firstDir = path[i] - cur;
            int run = 1;
            while (i + run < path.Count
                && (path[i + run] - path[i + run - 1]) == firstDir)
                run++;

            var to = path[i + run - 1];
            segs.Add(new HeroMoveSegment(cur, to, DominantDir(to - cur)));
            cur = to;
            i += run;
        }
        return segs;
    }

    // =========================================================================
    // PIEGE
    // =========================================================================

    private TrapInstance _activeTrap = null;

    // =========================================================================
    // SPRITES
    // =========================================================================

    private void ApplyRunSprite(Vector2Int dir, int frame)
    {
        if (_sr == null) return;
        var arr = GetRunArr(dir);
        if (arr != null && frame < arr.Length && arr[frame] != null)
            _sr.sprite = arr[frame];
    }

    private Sprite[] GetRunArr(Vector2Int d)
    {
        var dom = DominantDir(d);
        if (dom == Vector2Int.up) return _runBack;
        if (dom == Vector2Int.down) return _runFront;
        if (dom == Vector2Int.right) return _runRight;
        return _runLeft;
    }

    private Vector2Int DominantDir(Vector2Int d)
    {
        if (d == Vector2Int.zero) return Vector2Int.down;
        if (Mathf.Abs(d.x) >= Mathf.Abs(d.y))
            return d.x > 0 ? Vector2Int.right : Vector2Int.left;
        return d.y > 0 ? Vector2Int.up : Vector2Int.down;
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private void CheckAdjacentNPC()
    {
        if (_state.CollectOnArrival) return;
        float cs = _cellStep;
        var wp = new Vector2(_gridPos.x * cs, _gridPos.y * cs);
        int _hitCount = Physics2D.OverlapCircleNonAlloc(wp, cs * 1.2f, _physicsBuffer);

        foreach (var h in _physicsBuffer)
        {
            if (h == null) break;
            if (h.gameObject == gameObject) continue;
            var npc = h.GetComponent<NPC>();
            if (npc == null) continue;

            int nx = Mathf.RoundToInt(h.transform.position.x / cs);
            int ny = Mathf.RoundToInt(h.transform.position.y / cs);
            int dist = Mathf.Max(Mathf.Abs(_gridPos.x - nx), Mathf.Abs(_gridPos.y - ny));
            if (dist > 1) continue;

            npc.TriggerInteraction();
            return;
        }
    }

    private bool HasEnemyAt(int x, int y)
    {
        var wp = new Vector2(x * _cellStep, y * _cellStep);
        int _hitCount = Physics2D.OverlapCircleNonAlloc(wp, _cellStep * 0.45f, _physicsBuffer);
        foreach (var h in _physicsBuffer)
        {
            if (h == null) break;
            var ei = h.GetComponent<EnemyInstance>();
            if (ei != null && !ei.IsDead) return true;
        }
        return false;
    }

    private static readonly List<UnityEngine.EventSystems.RaycastResult> _uiRaycastResults
        = new List<UnityEngine.EventSystems.RaycastResult>(8);
    private UnityEngine.EventSystems.PointerEventData _cachedPointerData;

    private bool IsOverUI()
    {
        if (EventSystem.current == null
            || !EventSystem.current.IsPointerOverGameObject()) return false;
        if (_cachedPointerData == null)
            _cachedPointerData = new UnityEngine.EventSystems.PointerEventData(EventSystem.current);
        _cachedPointerData.position = _mouse.position.ReadValue();
        _uiRaycastResults.Clear();
        EventSystem.current.RaycastAll(_cachedPointerData, _uiRaycastResults);
        foreach (var r in _uiRaycastResults)
        {
            if (r.gameObject.GetComponent<UnityEngine.UI.Button>() != null) return true;
            string n = r.gameObject.name.ToLower();
            if (n.Contains("panel") || n.Contains("shop") || n.Contains("camp")
                || n.Contains("inventory") || n.Contains("equip") || n.Contains("slot"))
                return true;
        }
        return false;
    }

    // =========================================================================
    // A* CAVE — utilise gm.IsBlocked() uniquement, pas IsRevealed
    // Permet traversée cases non révélées, bloque murs ET ennemis révélés.
    // =========================================================================

    private List<Vector2Int> RunAStarCave(Vector2Int start, Vector2Int goal, IGridContext gm)
    {
        var open = new List<HeroANode>(64);
        var closed = new HashSet<Vector2Int>();
        var came = new Dictionary<Vector2Int, Vector2Int>();
        var gScore = new Dictionary<Vector2Int, float>();

        gScore[start] = 0f;
        open.Add(new HeroANode(start, _pf.Heuristic(start, goal)));
        int iter = 0;

        while (open.Count > 0 && iter++ < 2000)
        {
            int bi = 0;
            for (int i = 1; i < open.Count; i++)
                if (open[i].F < open[bi].F) bi = i;
            var cur = open[bi]; open.RemoveAt(bi);

            if (cur.Pos == goal) return ReconstructCave(came, goal, start);
            closed.Add(cur.Pos);

            foreach (var d in _caveDirs)
            {
                var n = cur.Pos + d;
                if (!gm.IsInBounds(n.x, n.y)) continue;
                if (closed.Contains(n)) continue;
                if (gm.IsBlocked(n.x, n.y) && n != goal) continue;

                float g = (gScore.TryGetValue(cur.Pos, out float cg) ? cg : float.MaxValue) + 1f;
                if (!gScore.TryGetValue(n, out float old) || g < old)
                {
                    gScore[n] = g;
                    came[n] = cur.Pos;
                    open.Add(new HeroANode(n, g + _pf.Heuristic(n, goal)));
                }
            }
        }
        return null;
    }

    private List<Vector2Int> ReconstructCave(Dictionary<Vector2Int, Vector2Int> came,
        Vector2Int end, Vector2Int start)
    {
        var path = new List<Vector2Int>();
        var cur = end;
        while (came.ContainsKey(cur) && cur != start)
        { path.Insert(0, cur); cur = came[cur]; }
        return path;
    }

    private Vector2Int FindAdjacentFreeCave(Vector2Int target, IGridContext gm)
    {
        foreach (var d in _caveDirs)
        {
            var n = target + d;
            if (gm.IsInBounds(n.x, n.y) && !gm.IsBlocked(n.x, n.y))
                return n;
        }
        return _gridPos;
    }

    private static readonly Vector2Int[] _caveDirs =
        { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    // =========================================================================
    // GRIDTOWORLD — CORRECTION 2
    // =========================================================================

    private Vector3 GridToWorld(Vector2Int p)
    {
        var grid = ActiveGrid.Current;
        if (grid != null) return grid.GridToWorld(p.x, p.y, -0.5f);
        return new Vector3(p.x * _cellStep, p.y * _cellStep, -0.5f);
    }

    // =========================================================================
    // MÉTHODES GRILLE GÉNÉRIQUE — UndergroundCellView et autres grilles
    // Flux identique à HeroController.Update() sur la surface.
    // =========================================================================

    /// <summary>
    /// Déplacer vers une case révélée ou non révélée sur IGridContext.
    /// Si la cible est bloquée (ennemi/mur), s'arrête adjacent.
    /// </summary>
    public void MoveOnGrid(Vector2Int target, IGridContext grid)
    {
        if (_state == null || !_state.CanMove) return;
        _state.CollectOnArrival = false;
        StartCoroutine(PathfindAndMoveGrid(target, grid));
    }

    /// <summary>
    /// S'approcher d'une case non révélée et la révéler.
    /// Si révélation = ennemi → EnemyInstance.TriggerCombat au contact.
    /// </summary>
    public void ApproachAndRevealOnGrid(Vector2Int target, IGridContext grid)
    {
        if (_state == null || !_state.CanMove) return;
        _state.CollectOnArrival = false;
        StartCoroutine(ApproachAndRevealGrid(target, grid));
    }

    /// <summary>
    /// Pathfind + déplacement sur IGridContext.
    /// IsBlocked = gm.IsBlocked() → respecte les murs cave ET ennemis révélés.
    /// Les cases non révélées sont traversables (pas d'arbres en cave).
    /// </summary>
    private IEnumerator PathfindAndMoveGrid(Vector2Int target, IGridContext gm)
    {
        if (_state.IsCalculating) yield break;
        if (_pf == null)
        {
            _pf = HeroPathfinder.Instance ?? FindObjectOfType<HeroPathfinder>();
            if (_pf == null) { Debug.LogError("[Hero] HeroPathfinder null!"); yield break; }
        }
        _state.IsCalculating = true;
        _pf.BuildBlockedCache();

        // Résoudre la case adjacente si la cible est bloquée (ennemi/mur)
        var dest = gm.IsBlocked(target.x, target.y)
            ? FindAdjacentFreeGrid(target, gm) : target;

        if (!dest.HasValue || dest.Value == _gridPos)
        { _state.IsCalculating = false; yield break; }

        yield return null;

        // A* sur la cave — accepte cases non révélées comme traversables
        var path = RunAStarGrid(_gridPos, dest.Value, gm);
        _state.IsCalculating = false;

        if (path != null && path.Count > 0)
        {
            yield return StartCoroutine(MoveAlongSegments(BuildSegments(_gridPos, path)));
        }
        else
        {
            EventBus.Publish(new OnNotification
            { Message = "Inaccessible !", Type = NotificationType.Warning });
        }
    }

    /// <summary>S'approcher et révéler sur IGridContext.</summary>
    private IEnumerator ApproachAndRevealGrid(Vector2Int target, IGridContext gm)
    {
        if (_state.IsCalculating) yield break;
        if (_pf == null)
        {
            _pf = HeroPathfinder.Instance ?? FindObjectOfType<HeroPathfinder>();
            if (_pf == null) yield break;
        }
        _state.IsCalculating = true;
        _pf.BuildBlockedCache();

        // Trouver une case adjacente accessible
        var adj = FindAdjacentFreeGrid(target, gm);
        if (!adj.HasValue || adj.Value == _gridPos)
        { _state.IsCalculating = false; yield break; }

        // Se déplacer vers la case adjacente
        var path = RunAStarGrid(_gridPos, adj.Value, gm);
        _state.IsCalculating = false;

        if (path != null && path.Count > 0)
            yield return StartCoroutine(MoveAlongSegments(BuildSegments(_gridPos, path)));

        yield return WaitCache.Get(0.05f);

        // Révéler si adjacent
        int dist = Mathf.Max(Mathf.Abs(target.x - _gridPos.x),
                             Mathf.Abs(target.y - _gridPos.y));
        if (dist <= 1)
        {
            var result = gm.RevealCell(target.x, target.y);
            UndergroundCellView.RefreshFloorNumbers();

            if (result == RevealResult.EnemyHit)
            {
                // Ennemi révélé → EnemyInstance.TriggerCombat se déclenche tout seul
                // via son detection de proximité héros
            }
        }
    }

    /// <summary>A* cave — cases non révélées traversables, murs bloqués via gm.IsBlocked.</summary>
    private List<Vector2Int> RunAStarGrid(Vector2Int start, Vector2Int goal, IGridContext gm)
    {
        var open = new List<HeroANode>(64);
        var closed = new HashSet<Vector2Int>();
        var came = new Dictionary<Vector2Int, Vector2Int>();
        var gScore = new Dictionary<Vector2Int, float>();

        gScore[start] = 0f;
        open.Add(new HeroANode(start, _pf.Heuristic(start, goal)));
        int iter = 0;

        while (open.Count > 0 && iter++ < 1500)
        {
            int bi = 0;
            for (int i = 1; i < open.Count; i++)
                if (open[i].F < open[bi].F) bi = i;
            var cur = open[bi]; open.RemoveAt(bi);

            if (cur.Pos == goal) return _pf.Reconstruct(came, goal, start);
            closed.Add(cur.Pos);

            foreach (var d in _dirs4Cave)
            {
                var n = cur.Pos + d;
                if (!gm.IsInBounds(n.x, n.y)) continue;
                if (closed.Contains(n)) continue;
                if (gm.IsBlocked(n.x, n.y) && n != goal) continue; // mur ou ennemi

                float g = gScore[cur.Pos] + 1f;
                if (!gScore.TryGetValue(n, out float old) || g < old)
                {
                    gScore[n] = g;
                    came[n] = cur.Pos;
                    float f = g + _pf.Heuristic(n, goal);
                    open.Add(new HeroANode(n, f));
                }
            }
        }
        return null;
    }

    private Vector2Int? FindAdjacentFreeGrid(Vector2Int target, IGridContext gm)
    {
        foreach (var d in _dirs4Cave)
        {
            var n = target + d;
            if (!gm.IsInBounds(n.x, n.y)) continue;
            if (!gm.IsBlocked(n.x, n.y)) return n;
        }
        return null;
    }

    private static readonly Vector2Int[] _dirs4Cave =
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
    };
}

// ---- Structures ----

public struct HeroMoveSegment
{
    public Vector2Int From, To, SpriteDir;
    public HeroMoveSegment(Vector2Int f, Vector2Int t, Vector2Int d)
    { From = f; To = t; SpriteDir = d; }
}

public struct HeroANode
{
    public Vector2Int Pos;
    public float F;
    public HeroANode(Vector2Int p, float f) { Pos = p; F = f; }
}
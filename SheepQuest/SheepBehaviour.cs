using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class SheepBehaviour : MonoBehaviour
{
    // =========================================================================
    // Inspector
    // =========================================================================

    [Header("=== Idle (1 sprite chaque cote) ===")]
    [SerializeField] private Sprite _idleRight;
    [SerializeField] private Sprite _idleLeft;

    [Header("=== Manger Droite (13 frames) ===")]
    [SerializeField] private Sprite[] _eatRight = new Sprite[13];

    [Header("=== Manger Gauche (13 frames) ===")]
    [SerializeField] private Sprite[] _eatLeft = new Sprite[13];

    [SerializeField, Range(0.04f, 0.15f)] private float _eatFrameTime = 0.08f;

    [Header("=== Masticage ===")]
    [SerializeField, Range(2, 12)] private int _chewMin = 4;
    [SerializeField, Range(4, 20)] private int _chewMax = 8;
    [SerializeField, Range(1, 3)] private int _bitesMin = 1;
    [SerializeField, Range(1, 4)] private int _bitesMax = 2;

    [Header("=== Bond Droite (4 frames) ===")]
    [SerializeField] private Sprite[] _hopRight = new Sprite[4];

    [Header("=== Bond Gauche (4 frames) ===")]
    [SerializeField] private Sprite[] _hopLeft = new Sprite[4];

    [SerializeField, Range(0.06f, 0.15f)] private float _hopFrameTime = 0.09f;
    [SerializeField, Range(2, 5)] private int _hopCyclesPerCell = 3;

    [Header("=== Comportement ===")]
    [SerializeField, Range(3f, 30f)] private float _eatIntervalMin = 5f;
    [SerializeField, Range(5f, 60f)] private float _eatIntervalMax = 10f;
    [SerializeField, Range(1, 8)] private int _eatsBeforeMove = 3;
    [SerializeField, Range(2, 6)] private int _wanderRadius = 3;
    [SerializeField, Range(1f, 5f)] private float _moveSpeed = 2f;

    // =========================================================================
    // Etat
    // =========================================================================

    // Buffer statique pour Physics2D NonAlloc — évite allocation à chaque appel
    private static readonly Collider2D[] _physicsBuffer = new Collider2D[32];

    private Vector2Int _spawnPos;
    private Vector2Int _currentPos;
    private SpriteRenderer _sr;
    private bool _facingRight = true;
    private bool _isAnimating = false;
    private int _eatCount = 0;
    private int _eatsTarget = 3;
    private bool _questDone = false;
    private bool _isReturningToFarm = false;
    private bool _isStuck = false;
    private float _cellStep = 1.05f;

    private static int _totalArrived = 0;

    public bool IsReturningToFarm => _isReturningToFarm;
    public bool IsStuck => _isStuck;

    // =========================================================================
    // Dialogues
    // =========================================================================

    private static readonly string[] _stuckDialogues =
    {
        "Ooh... je ne sais pas par ou je suis passe pour atterrir ici.\nTu peux m'aider a retrouver mon chemin ?",
        "Il est vers ou deja le vieux ? J'ai nulle part ou traverser ici...",
        "Ca devrait etre en face mais ca semble un cul-de-sac.\nJe me suis bel et bien perdu !"
    };

    private static readonly string[] _arrivedDialogues =
    {
        "C'etait pas mal comme escapade,\nmais ici on manque de rien !",
        "Alors le vieux ! Je t'ai pas manque ?\nT'aurais pu venir me chercher toi-meme au lieu de demander a un inconnu, non ?",
        "Tout ce temps dehors et rien n'a bouge ici.\nCa doit etre ca la routine. J'ai deja envie de repartir..."
    };

    private const string _lastArrivedDialogue =
        "Heeeh les gars, vous imaginez pas quoi !\nL'herbe est si fraiche ailleurs !";

    // =========================================================================
    // Init
    // =========================================================================

    private void Start()
    {
        _sr = GetComponent<SpriteRenderer>()
           ?? GetComponentInChildren<SpriteRenderer>();

        _cellStep = GridManager.Instance?.CellStep ?? 1.05f;

        int sx = Mathf.RoundToInt(transform.position.x / _cellStep);
        int sy = Mathf.RoundToInt(transform.position.y / _cellStep);
        _spawnPos = new Vector2Int(sx, sy);
        _currentPos = _spawnPos;

        // Direction initiale aleatoire
        _facingRight = Random.value > 0.5f;
        ShowIdle();

        _eatsTarget = Random.Range(2, _eatsBeforeMove + 1);

        StartCoroutine(LifeCycle(Random.Range(0f, _eatIntervalMax)));
        StartCoroutine(CheckQuestComplete());
    }

    // =========================================================================
    // Cycle de vie
    // =========================================================================

    private IEnumerator LifeCycle(float initialDelay)
    {
        yield return WaitCache.Get(initialDelay);

        while (true)
        {
            yield return WaitCache.Get(
                Random.Range(_eatIntervalMin, _eatIntervalMax));

            if (_isAnimating) continue;

            yield return StartCoroutine(EatAnimation());
            _eatCount++;

            if (_eatCount >= _eatsTarget)
            {
                _eatCount = 0;
                _eatsTarget = Random.Range(2, _eatsBeforeMove + 1);
                yield return WaitCache.Get(0.5f);
                yield return StartCoroutine(WanderMove());
            }

            if (_questDone)
            {
                yield return StartCoroutine(MoveToFarm());
                yield break;
            }
        }
    }

    // =========================================================================
    // Manger
    // =========================================================================

    private IEnumerator EatAnimation()
    {
        var sprites = _facingRight ? _eatRight : _eatLeft;
        if (sprites == null || sprites.Length < 13) yield break;

        _isAnimating = true;

        // Frames 0-3 : arrachage — tableaux statiques réutilisés
        _biteFrames[0] = sprites[0]; _biteFrames[1] = sprites[1];
        _biteFrames[2] = sprites[2]; _biteFrames[3] = sprites[3];
        _chewFrames[0][0] = sprites[4]; _chewFrames[0][1] = sprites[5]; _chewFrames[0][2] = sprites[6];
        _chewFrames[1][0] = sprites[7]; _chewFrames[1][1] = sprites[8]; _chewFrames[1][2] = sprites[9];
        _chewFrames[2][0] = sprites[10]; _chewFrames[2][1] = sprites[11]; _chewFrames[2][2] = sprites[12];
        var bite = _biteFrames;
        var chews = _chewFrames;

        int bites = Random.Range(_bitesMin, _bitesMax + 1);
        for (int b = 0; b < bites; b++)
        {
            foreach (var sp in bite)
            {
                if (sp != null && _sr != null) _sr.sprite = sp;
                yield return WaitCache.Get(_eatFrameTime);
            }

            int chewCount = Random.Range(_chewMin, _chewMax + 1);
            for (int c = 0; c < chewCount; c++)
            {
                foreach (var sp in chews[c % 3])
                {
                    if (sp != null && _sr != null) _sr.sprite = sp;
                    yield return WaitCache.Get(_eatFrameTime);
                }
            }
        }

        ShowIdle();
        _isAnimating = false;
    }

    // =========================================================================
    // Errance
    // =========================================================================

    private IEnumerator WanderMove()
    {
        var gm = GridManager.Instance;
        if (gm == null) yield break;

        var dest = ChooseWanderTarget(gm);
        if (!dest.HasValue) yield break;

        yield return StartCoroutine(MoveToCell(dest.Value));
    }

    private Vector2Int? ChooseWanderTarget(GridManager gm)
    {
        _wanderCandidates.Clear();
        var candidates = _wanderCandidates;

        for (int dx = -_wanderRadius; dx <= _wanderRadius; dx++)
            for (int dy = -_wanderRadius; dy <= _wanderRadius; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var c = new Vector2Int(_spawnPos.x + dx, _spawnPos.y + dy);
                if (!InBounds(c, gm)) continue;
                var cell = gm.GetCell(c.x, c.y);
                if (cell == null || !cell.IsRevealed) continue;
                if (cell.IsEnemy || cell.IsTrap || cell.IsDangerous) continue;
                if (IsCellOccupied(c)) continue;

                float score = -Vector2Int.Distance(c, _spawnPos) * 0.6f
                            - Vector2Int.Distance(c, _currentPos) * 0.4f
                            + Random.Range(-0.5f, 0.5f);
                candidates.Add((c, score));
            }

        if (candidates.Count == 0) return null;
        candidates.Sort((a, b) => b.score.CompareTo(a.score));
        return candidates[0].pos;
    }

    // =========================================================================
    // Deplacement fluide
    // =========================================================================

    private IEnumerator MoveToCell(Vector2Int target)
    {
        _isAnimating = true;
        int dx = target.x - _currentPos.x;
        int dy = target.y - _currentPos.y;
        // Orienter selon direction dominante
        if (Mathf.Abs(dx) >= Mathf.Abs(dy) && dx != 0)
            _facingRight = dx > 0;
        // Si vertical dominant on garde l orientation horizontale courante

        var hops = _facingRight ? _hopRight : _hopLeft;
        Vector3 from = new Vector3(_currentPos.x * _cellStep,
                                    _currentPos.y * _cellStep,
                                    transform.position.z);
        Vector3 to = new Vector3(target.x * _cellStep,
                                    target.y * _cellStep,
                                    transform.position.z);

        float dist = Vector3.Distance(from, to);
        float dur = Mathf.Max(dist / _moveSpeed, 0.1f);
        float elapsed = 0f;
        float frameT = 0f;
        int frame = 0;
        float timePerFrame = _hopFrameTime;

        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            frameT += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dur);

            float arc = Mathf.Sin(t * Mathf.PI * _hopCyclesPerCell) * 0.08f;
            transform.position = Vector3.Lerp(from, to, t)
                               + new Vector3(0f, arc, 0f);

            if (frameT >= timePerFrame && hops != null && hops.Length > 0)
            {
                frameT = 0f;
                frame = (frame + 1) % hops.Length;
                if (hops[frame] != null && _sr != null) _sr.sprite = hops[frame];
            }

            yield return null;
        }

        transform.position = to;
        _currentPos = target;
        ShowIdle();
        _isAnimating = false;
    }

    // =========================================================================
    // Retour ferme - A* sur cases revelees
    // =========================================================================

    private static readonly HashSet<Vector2Int> _takenFarmSpots = new HashSet<Vector2Int>();

    private IEnumerator MoveToFarm()
    {
        _isReturningToFarm = true;

        var gm = GridManager.Instance;
        if (gm == null) yield break;

        var farm = ShepherdFarmSpawner.Instance;
        if (farm == null) yield break;

        Vector3 fw = farm.FarmWorldPos;
        var farmCenter = new Vector2Int(
            Mathf.RoundToInt(fw.x / _cellStep),
            Mathf.RoundToInt(fw.y / _cellStep));

        // Trouver une case libre autour de la ferme (radius 2-4)
        var dest = FindFarmSpot(farmCenter, gm);
        if (!dest.HasValue) { OnSheepStuck(); yield break; }

        _takenFarmSpots.Add(dest.Value);

        var path = FindPath(_currentPos, dest.Value, gm);
        if (path == null || path.Count == 0)
        {
            _takenFarmSpots.Remove(dest.Value);
            OnSheepStuck();
            yield break;
        }

        foreach (var step in path)
            yield return StartCoroutine(MoveToCell(step));

        // Centrer la ronde locale sur cette position
        _spawnPos = dest.Value;
        _isReturningToFarm = false;
        OnSheepArrived();
        StartCoroutine(LifeCycleFarm());
    }

    private Vector2Int? FindFarmSpot(Vector2Int center, GridManager gm)
    {
        _radius2.Clear(); _radius3.Clear();
        var radius2 = _radius2;
        var radius3 = _radius3;

        for (int r = 2; r <= 3; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    var c = new Vector2Int(center.x + dx, center.y + dy);
                    if (!InBounds(c, gm)) continue;
                    var cell = gm.GetCell(c.x, c.y);
                    if (cell == null || !cell.IsRevealed) continue;
                    if (cell.IsEnemy || cell.IsTrap || cell.IsDangerous) continue;
                    if (IsCellOccupied(c)) continue;
                    if (_takenFarmSpots.Contains(c)) continue;
                    if (r == 2) radius2.Add(c);
                    else radius3.Add(c);
                }

        // 75% radius 2, 25% radius 3
        bool preferRadius2 = Random.value < 0.75f;

        if (preferRadius2 && radius2.Count > 0)
            return radius2[Random.Range(0, radius2.Count)];
        if (radius3.Count > 0)
            return radius3[Random.Range(0, radius3.Count)];
        if (radius2.Count > 0)
            return radius2[Random.Range(0, radius2.Count)];
        return null;
    }

    // Buffers listes statiques — évitent allocations lors des décisions de déplacement
    private static readonly List<(Vector2Int pos, float score)> _wanderCandidates
        = new List<(Vector2Int, float)>(16);
    private static readonly List<Vector2Int> _radius2 = new List<Vector2Int>(16);
    private static readonly List<Vector2Int> _radius3 = new List<Vector2Int>(24);

    // Tableaux d'animation statiques — évitent new Sprite[] à chaque EatAnimation
    private static readonly Sprite[] _biteFrames = new Sprite[4];
    private static readonly Sprite[][] _chewFrames = new Sprite[3][]
        { new Sprite[3], new Sprite[3], new Sprite[3] };

    // Buffers statiques A* — réutilisés à chaque appel, zéro allocation GC
    private static readonly List<(Vector2Int pos, float f)> _astarOpen = new List<(Vector2Int, float)>(64);
    private static readonly HashSet<Vector2Int> _astarClosed = new HashSet<Vector2Int>();
    private static readonly Dictionary<Vector2Int, Vector2Int> _astarFrom = new Dictionary<Vector2Int, Vector2Int>();
    private static readonly Dictionary<Vector2Int, float> _astarG = new Dictionary<Vector2Int, float>();
    private static readonly List<Vector2Int> _astarPath = new List<Vector2Int>(32);
    private static readonly List<Vector2Int> _astarNeighbourBuf = new List<Vector2Int>(4);

    // A* simple sur cases revelees
    private List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal, GridManager gm)
    {
        // Réutiliser les buffers — Clear() au lieu de new
        _astarOpen.Clear();
        _astarClosed.Clear();
        _astarFrom.Clear();
        _astarG.Clear();
        _astarG[start] = 0f;

        var open = _astarOpen;
        var closed = _astarClosed;
        var cameFrom = _astarFrom;
        var gScore = _astarG;

        open.Add((start, Heuristic(start, goal)));
        int safety = 0;

        while (open.Count > 0 && safety++ < 1000)
        {
            // Trouver le meilleur
            int bi = 0;
            for (int i = 1; i < open.Count; i++)
                if (open[i].f < open[bi].f) bi = i;
            var cur = open[bi].pos;
            open.RemoveAt(bi);

            if (cur == goal)
            {
                // Reconstruire chemin dans le buffer statique
                _astarPath.Clear();
                var c = goal;
                while (cameFrom.ContainsKey(c)) { _astarPath.Insert(0, c); c = cameFrom[c]; }
                return _astarPath;
            }

            closed.Add(cur);

            foreach (var n in GetNeighbours(cur, gm))
            {
                if (closed.Contains(n)) continue;
                float g = gScore.GetValueOrDefault(cur, float.MaxValue) + 1f;
                if (g < gScore.GetValueOrDefault(n, float.MaxValue))
                {
                    gScore[n] = g;
                    cameFrom[n] = cur;
                    open.Add((n, g + Heuristic(n, goal)));
                }
            }
        }

        return null;
    }

    private IEnumerable<Vector2Int> GetNeighbours(Vector2Int pos, GridManager gm)
    {
        var dirs = new[]
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
        };
        foreach (var d in dirs)
        {
            var n = pos + d;
            if (!InBounds(n, gm)) continue;
            var cell = gm.GetCell(n.x, n.y);
            if (cell == null || !cell.IsRevealed) continue;
            if (cell.IsEnemy || cell.IsTrap) continue;
            // Bloquer cases avec accessoires/PNJ (cabane, paille, etc.)
            if (IsCellOccupied(n)) continue;
            yield return n;
        }
    }

    private float Heuristic(Vector2Int a, Vector2Int b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    private IEnumerator LifeCycleFarm()
    {
        int eatCount = 0;
        int eatTarget = Random.Range(2, _eatsBeforeMove + 1);

        while (true)
        {
            yield return WaitCache.Get(
                Random.Range(_eatIntervalMin, _eatIntervalMax));
            if (_isAnimating) continue;

            yield return StartCoroutine(EatAnimation());
            eatCount++;

            if (eatCount >= eatTarget)
            {
                eatCount = 0;
                eatTarget = Random.Range(2, _eatsBeforeMove + 1);
                yield return WaitCache.Get(0.4f);
                var gm = GridManager.Instance;
                if (gm != null)
                {
                    var dest = ChooseWanderTarget(gm);
                    if (dest.HasValue)
                        yield return StartCoroutine(MoveToCell(dest.Value));
                }
            }
        }
    }

    // =========================================================================
    // Quete
    // =========================================================================

    private IEnumerator CheckQuestComplete()
    {
        while (!_questDone)
        {
            yield return WaitCache.Get(5f);
            var qm = QuestManager.Instance;
            if (qm != null && qm.QuestComplete)
                _questDone = true;
        }
    }

    // =========================================================================
    // Dialogues
    // =========================================================================

    private void OnSheepStuck()
    {
        _isStuck = true;
        ShowIdle();
        var npc = GetComponent<NPC>();
        if (npc != null)
            npc.SetStuckDialogue(
                _stuckDialogues[Random.Range(0, _stuckDialogues.Length)]);
    }

    private void OnSheepArrived()
    {
        _totalArrived++;
        var qm = QuestManager.Instance;
        bool isLast = qm != null && _totalArrived >= qm.TotalSheep;
        string text = isLast
            ? _lastArrivedDialogue
            : _arrivedDialogues[Mathf.Min(_totalArrived - 1, _arrivedDialogues.Length - 1)];

        var npc = GetComponent<NPC>();
        if (npc != null)
        {
            npc.SetArrivedDialogue(text);
            // Valider automatiquement dans la quete si le mouton etait deja trouve
            // (mouton retrouve en chemin et raccompagne)
            npc.ValidateSheepIfFound();
        }
    }

    // =========================================================================
    // API publique
    // =========================================================================

    public void TriggerReturnToFarm()
    {
        if (_isReturningToFarm) return;
        _questDone = true;
        _isReturningToFarm = true;
        StopAllCoroutines();
        StartCoroutine(MoveToFarm());
    }

    /// <summary>
    /// Tente de relancer le retour si le mouton etait bloque.
    /// Retourne true si un chemin a ete trouve.
    /// </summary>
    public bool TryRestartReturn()
    {
        if (!_isStuck) return false;

        var gm = GridManager.Instance;
        var farm = ShepherdFarmSpawner.Instance;
        if (gm == null || farm == null) return false;

        // Verifier qu un chemin existe maintenant
        Vector3 fw = farm.FarmWorldPos;
        var dest = new Vector2Int(
            Mathf.RoundToInt(fw.x / _cellStep),
            Mathf.RoundToInt(fw.y / _cellStep));

        // Chercher case revelee adjacente pour commencer
        var dirs = new[]
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
        };
        bool hasPath = false;
        foreach (var d in dirs)
        {
            var n = _currentPos + d;
            if (n.x < 0 || n.y < 0 || n.x >= gm.Width || n.y >= gm.Height) continue;
            var cell = gm.GetCell(n.x, n.y);
            if (cell != null && cell.IsRevealed && !IsCellOccupied(n))
            {
                hasPath = true;
                break;
            }
        }

        if (!hasPath) return false;

        // Relancer le retour
        _isStuck = false;
        StopAllCoroutines();
        StartCoroutine(MoveToFarm());
        return true;
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private void ShowIdle()
    {
        if (_sr == null) return;
        var sp = _facingRight ? _idleRight : _idleLeft;
        if (sp != null) _sr.sprite = sp;
    }

    private bool InBounds(Vector2Int p, GridManager gm)
        => p.x >= 0 && p.y >= 0 && p.x < gm.Width && p.y < gm.Height;

    private bool IsCellOccupied(Vector2Int pos)
    {
        var wp = new Vector2(pos.x * _cellStep, pos.y * _cellStep);
        int _hitCount = Physics2D.OverlapCircleNonAlloc(wp, _cellStep * 0.3f, _physicsBuffer);
        var hits = new System.ArraySegment<Collider2D>(_physicsBuffer, 0, _hitCount);
        foreach (var h in hits)
        {
            if (h.gameObject == gameObject) continue;
            if (h.GetComponent<NPC>() != null) return true;
            if (h.GetComponent<WorldObject>() != null) return true;
            if (h.GetComponent<EnemyInstance>() != null) return true;
        }
        return false;
    }
}
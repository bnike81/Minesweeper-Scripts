using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// HeroPathfinder - Tout ce qui concerne le calcul de chemin et navigation.
/// Separe de HeroController pour la lisibilite.
/// Utilise par HeroController via HeroPathfinder.Instance.
/// </summary>
public class HeroPathfinder : MonoBehaviour
{
    public static HeroPathfinder Instance { get; private set; }

    [Header("=== Pathfinding ===")]
    [Tooltip("Penalite virage 90deg")]
    [SerializeField, Range(0f, 3f)] private float _turnPenalty = 0.5f;
    [Tooltip("Bonus continuite direction")]
    [SerializeField, Range(0f, 1f)] private float _diagBonus = 0.1f;

    // Cache obstacles - reconstruit avant chaque A*
    private static readonly HashSet<Vector2Int> _blockedCache = new HashSet<Vector2Int>();
    private bool _cacheBuilt = false;
    private float _cellStep = 1.05f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        _cellStep = GridManager.Instance?.CellStep ?? 1.05f;
    }

    private void OnEnable() { EventBus.Subscribe<OnGridGenerated>(OnGrid); }
    private void OnDisable() { EventBus.Unsubscribe<OnGridGenerated>(OnGrid); }
    private void OnGrid(OnGridGenerated e)
        => _cellStep = GridManager.Instance?.CellStep ?? _cellStep;

    // =========================================================================
    // API publique - appelee par HeroController
    // =========================================================================

    public void BuildBlockedCache()
    {
        _blockedCache.Clear();
        float step = _cellStep;

        foreach (var wo in FindObjectsOfType<WorldObject>())
        {
            int wx = Mathf.RoundToInt(wo.transform.position.x / step);
            int wy = Mathf.RoundToInt(wo.transform.position.y / step);
            _blockedCache.Add(new Vector2Int(wx, wy));
        }
        foreach (var npc in FindObjectsOfType<NPC>())
        {
            int wx = Mathf.RoundToInt(npc.transform.position.x / step);
            int wy = Mathf.RoundToInt(npc.transform.position.y / step);
            _blockedCache.Add(new Vector2Int(wx, wy));
        }
        foreach (var ei in FindObjectsOfType<EnemyInstance>())
        {
            int wx = Mathf.RoundToInt(ei.transform.position.x / step);
            int wy = Mathf.RoundToInt(ei.transform.position.y / step);
            _blockedCache.Add(new Vector2Int(wx, wy));
        }
        foreach (var m in FindObjectsOfType<MerchantNPC>())
        {
            int wx = Mathf.RoundToInt(m.transform.position.x / step);
            int wy = Mathf.RoundToInt(m.transform.position.y / step);
            _blockedCache.Add(new Vector2Int(wx, wy));
        }
        foreach (var c in FindObjectsOfType<CampfireNPC>())
        {
            int wx = Mathf.RoundToInt(c.transform.position.x / step);
            int wy = Mathf.RoundToInt(c.transform.position.y / step);
            _blockedCache.Add(new Vector2Int(wx, wy));
        }
        _cacheBuilt = true;
    }

    public bool IsBlocked(Vector2Int pos, IGridContext gm, Vector2Int heroPos)
    {
        if (!InBounds(pos, gm)) return true;
        if (pos == heroPos) return false;
        // Cases montagne = infranchissables
        var cell = gm.GetCell(pos.x, pos.y);
        if (cell != null && cell.IsMountainReserved) return true;
        if (!IsRevealed(pos, gm)) return true;
        return _cacheBuilt && _blockedCache.Contains(pos);
    }

    // Surcharge pour compatibilité GridManager direct
    public bool IsBlocked(Vector2Int pos, GridManager gm, Vector2Int heroPos)
        => IsBlocked(pos, (IGridContext)gm, heroPos);

    public bool IsRevealed(Vector2Int p, IGridContext gm)
    {
        if (!InBounds(p, gm)) return false;
        var cell = gm.GetCell(p.x, p.y);
        return cell != null && cell.IsRevealed;
    }

    public bool InBounds(Vector2Int p, IGridContext gm)
        => p.x >= 0 && p.y >= 0 && p.x < gm.Width && p.y < gm.Height;

    public float Heuristic(Vector2Int a, Vector2Int b)
    {
        int dx = Mathf.Abs(a.x - b.x);
        int dy = Mathf.Abs(a.y - b.y);
        return dx + dy - 0.586f * Mathf.Min(dx, dy);
    }

    // =========================================================================
    // A* Pathfinding
    // =========================================================================

    // Buffers statiques A* � z�ro allocation � chaque d�placement h�ros
    private static readonly List<HeroANode> _astarOpen = new List<HeroANode>(64);
    private static readonly HashSet<Vector2Int> _astarClosed = new HashSet<Vector2Int>();
    private static readonly Dictionary<Vector2Int, Vector2Int> _astarCameFrom = new Dictionary<Vector2Int, Vector2Int>();
    private static readonly Dictionary<Vector2Int, float> _astarGScore = new Dictionary<Vector2Int, float>();
    private static readonly Dictionary<Vector2Int, Vector2Int> _astarDirMap = new Dictionary<Vector2Int, Vector2Int>();
    private static readonly List<Vector2Int> _astarResult = new List<Vector2Int>(32);

    public List<Vector2Int> RunAStar(Vector2Int start, Vector2Int goal,
        IGridContext gm, Vector2Int heroPos)
    {
        _astarOpen.Clear();
        _astarClosed.Clear();
        _astarCameFrom.Clear();
        _astarGScore.Clear();
        _astarGScore[start] = 0f;
        _astarDirMap.Clear();
        _astarDirMap[start] = Vector2Int.zero;

        var open = _astarOpen;
        var closed = _astarClosed;
        var cameFrom = _astarCameFrom;
        var gScore = _astarGScore;
        var dirMap = _astarDirMap;

        open.Add(new HeroANode(start, Heuristic(start, goal)));
        int iter = 0;

        while (open.Count > 0 && iter++ < 1500)
        {
            int bi = 0;
            for (int i = 1; i < open.Count; i++)
                if (open[i].F < open[bi].F) bi = i;
            var cur = open[bi]; open.RemoveAt(bi);

            if (cur.Pos == goal) return Reconstruct(cameFrom, goal, start);
            closed.Add(cur.Pos);

            var prevDir = dirMap.GetValueOrDefault(cur.Pos, Vector2Int.zero);
            foreach (var (next, cost) in GetNeighbours(cur.Pos, gm, heroPos))
            {
                if (closed.Contains(next)) continue;
                var newDir = next - cur.Pos;
                float penalty = CalcTurnPenalty(prevDir, newDir);
                float bonus = (prevDir != Vector2Int.zero && newDir == prevDir)
                                ? _diagBonus : 0f;
                float g = gScore.GetValueOrDefault(cur.Pos, float.MaxValue)
                                + cost + penalty - bonus;

                if (g < gScore.GetValueOrDefault(next, float.MaxValue))
                {
                    gScore[next] = g;
                    cameFrom[next] = cur.Pos;
                    dirMap[next] = newDir;
                    open.Add(new HeroANode(next, g + Heuristic(next, goal)));
                }
            }
        }
        return null;
    }

    private float CalcTurnPenalty(Vector2Int prev, Vector2Int next)
    {
        if (prev == Vector2Int.zero || next == prev) return 0f;
        float dot = Vector2.Dot((Vector2)prev, (Vector2)next);
        float angle = Mathf.Acos(Mathf.Clamp(
            dot / (prev.magnitude * next.magnitude), -1f, 1f)) * Mathf.Rad2Deg;
        if (angle >= 80f) return _turnPenalty * 3f;
        if (angle >= 40f) return _turnPenalty * 0.5f;
        return 0f;
    }

    private IEnumerable<(Vector2Int, float)> GetNeighbours(Vector2Int pos,
        IGridContext gm, Vector2Int heroPos)
    {
        var card = new[]
        {
            Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left
        };
        var diag = new[]
        {
            new Vector2Int(1,1), new Vector2Int(-1,1),
            new Vector2Int(1,-1), new Vector2Int(-1,-1)
        };

        bool startUnrevealed = !IsRevealed(pos, gm);

        foreach (var d in card)
        {
            var n = pos + d;
            if (!InBounds(n, gm)) continue;
            if (IsBlocked(n, gm, heroPos)) continue;
            if (!IsRevealed(n, gm) && !startUnrevealed) continue;
            yield return (n, 1f);
        }
        foreach (var d in diag)
        {
            var n = pos + d;
            var c1 = new Vector2Int(pos.x + d.x, pos.y);
            var c2 = new Vector2Int(pos.x, pos.y + d.y);
            if (!InBounds(n, gm)) continue;
            if (IsBlocked(n, gm, heroPos)) continue;
            if (!IsRevealed(n, gm)) continue;
            if (IsBlocked(c1, gm, heroPos) || IsBlocked(c2, gm, heroPos)) continue;
            yield return (n, 1.2f);
        }
    }

    // =========================================================================
    // Navigation avec sauts
    // =========================================================================

    public IEnumerator NavigateWithJumps(Vector2Int start, Vector2Int target,
        IGridContext gm, System.Action<Vector2Int> onPositionUpdate)
    {
        var current = start;
        int maxJumps = 5;
        int jumps = 0;

        // Si heros sur case non revelee - sauter vers case revelee adjacente
        if (!IsRevealed(current, gm))
        {
            var nearRevealed = FindNearestRevealedFrom(current, gm, current);
            if (nearRevealed.HasValue)
            {
                var jump = HeroJump.Instance;
                if (jump != null)
                {
                    jump.TryJumpToRevealed(current, nearRevealed.Value, gm);
                    yield return new WaitUntil(() => !jump.IsJumping);
                    current = HeroController.Instance.GridPosition;
                    onPositionUpdate?.Invoke(current);
                }
            }
        }

        while (current != target && jumps <= maxJumps)
        {
            BuildBlockedCache();
            var path = RunAStar(current, target, gm, current);

            if (path != null && path.Count > 0)
            {
                // Chemin direct trouve - signaler a HeroController
                onPositionUpdate?.Invoke(current);
                yield break; // HeroController gere MoveAlongSegments
            }

            // Pas de chemin - chercher saut
            Debug.Log("[Hero] Pas de chemin depuis " + current + " vers " + target);

            var jumpLanding = FindJumpLanding(current, target, gm, current);
            if (!jumpLanding.HasValue)
            {
                LogNoJump(current, target, gm);
                break;
            }

            Debug.Log("[Hero] Saut auto vers " + jumpLanding.Value);
            var heroJump = HeroJump.Instance;
            if (heroJump == null) break;

            // Courir vers launchpad si necessaire
            int distToLanding = Mathf.Max(
                Mathf.Abs(jumpLanding.Value.x - current.x),
                Mathf.Abs(jumpLanding.Value.y - current.y));

            if (distToLanding > heroJump.MaxJumpDistance)
            {
                var lp = FindLaunchPadFor(current, jumpLanding.Value, gm,
                    current, heroJump.MaxJumpDistance);
                if (lp.HasValue && lp.Value != current)
                {
                    // Signaler launchpad a HeroController pour qu il coure dessus
                    onPositionUpdate?.Invoke(lp.Value);
                    yield break;
                }
            }

            bool jumped = heroJump.TryJumpToRevealed(current, jumpLanding.Value, gm);
            if (!jumped) { Debug.Log("[Hero] Saut refuse"); break; }

            yield return new WaitUntil(() => !heroJump.IsJumping);
            current = HeroController.Instance.GridPosition;
            onPositionUpdate?.Invoke(current);
            BuildBlockedCache();
            jumps++;
            yield return null;
        }
    }

    // =========================================================================
    // Helpers de navigation
    // =========================================================================

    /// <summary>
    /// Cherche une destination de saut valide depuis from vers target.
    /// Cherche d abord directement depuis from, puis cherche un launchpad
    /// sur le bord de la zone revelee depuis lequel sauter.
    /// </summary>
    public Vector2Int? FindJumpLanding(Vector2Int from, Vector2Int target,
        IGridContext gm, Vector2Int heroPos)
    {
        // 1. Cherche direct depuis from (cas heros au bord)
        var direct = ScanJumpFromPos(from, target, gm, heroPos);
        if (direct.HasValue) return direct;

        // 2. Heros loin du bord : chercher launchpad sur le bord dans
        //    toutes les directions, puis scanner depuis ces launchpads
        var dirs = new[]
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right,
            new Vector2Int(1,1), new Vector2Int(-1,1),
            new Vector2Int(1,-1), new Vector2Int(-1,-1)
        };

        Vector2Int? bestLanding = null;
        Vector2Int? bestLaunchpad = null;
        float bestScore = float.MaxValue;

        foreach (var dir in dirs)
        {
            // Trouver la derniere case revelee libre dans cette direction
            // avant de tomber dans le fog ou hors grille = launchpad potentiel
            Vector2Int? launchpad = null;
            for (int d = 1; d <= 10; d++)
            {
                var c = from + dir * d;
                if (!InBounds(c, gm)) break;
                var cell = gm.GetCell(c.x, c.y);
                if (cell == null) break;

                if (!cell.IsRevealed) break; // Bord du fog - launchpad = case precedente
                if (!IsBlocked(c, gm, heroPos)) launchpad = c;
            }

            if (!launchpad.HasValue) continue;

            // Scanner depuis ce launchpad
            var landing = ScanJumpFromPos(launchpad.Value, target, gm, heroPos);
            if (!landing.HasValue) continue;

            // Scorer par distance totale (launchpad + landing vers target)
            float score = Heuristic(launchpad.Value, from) * 0.3f
                        + Heuristic(landing.Value, target);
            if (score < bestScore)
            {
                bestScore = score;
                bestLanding = landing;
                bestLaunchpad = launchpad;
            }
        }

        // Stocker le launchpad pour que NavigateLoop puisse y courir
        _pendingLaunchpad = bestLaunchpad;
        return bestLanding;
    }

    // Launchpad intermediaire trouve lors du dernier FindJumpLanding
    public Vector2Int? PendingLaunchpad { get; private set; }
    private Vector2Int? _pendingLaunchpad
    {
        set => PendingLaunchpad = value;
        get => PendingLaunchpad;
    }

    /// <summary>Scanne depuis pos dans toutes les directions pour trouver
    /// une case revelee apres une zone fog.</summary>
    private Vector2Int? ScanJumpFromPos(Vector2Int pos, Vector2Int target,
        IGridContext gm, Vector2Int heroPos)
    {
        var dirs = new[]
        {
            new Vector2Int(System.Math.Sign(target.x - pos.x),
                           System.Math.Sign(target.y - pos.y)),
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right,
            new Vector2Int(1,1), new Vector2Int(-1,1),
            new Vector2Int(1,-1), new Vector2Int(-1,-1)
        };

        Vector2Int? best = null;
        float bestScore = float.MaxValue;

        foreach (var dir in dirs)
        {
            bool passedFog = false;
            for (int d = 1; d <= 3; d++)
            {
                var c = pos + dir * d;
                if (!InBounds(c, gm)) break;
                var cell = gm.GetCell(c.x, c.y);
                if (cell == null) break;

                if (!cell.IsRevealed) { passedFog = true; continue; }

                if (passedFog && !IsBlocked(c, gm, heroPos))
                {
                    float score = Heuristic(c, target);
                    if (score < bestScore) { bestScore = score; best = c; }
                    break;
                }
                if (!passedFog) continue;
            }
        }
        return best;
    }

    public Vector2Int? FindLaunchPadFor(Vector2Int from, Vector2Int jumpTarget,
        IGridContext gm, Vector2Int heroPos, int maxDist)
    {
        Vector2Int? best = null;
        float bestDist = float.MaxValue;

        for (int dx2 = -maxDist; dx2 <= maxDist; dx2++)
            for (int dy2 = -maxDist; dy2 <= maxDist; dy2++)
            {
                var c = new Vector2Int(jumpTarget.x + dx2, jumpTarget.y + dy2);
                if (!InBounds(c, gm)) continue;
                var cell = gm.GetCell(c.x, c.y);
                if (cell == null || !cell.IsRevealed) continue;
                if (IsBlocked(c, gm, heroPos) && c != from) continue;

                int distToTarget = Mathf.Max(Mathf.Abs(c.x - jumpTarget.x),
                                             Mathf.Abs(c.y - jumpTarget.y));
                if (distToTarget < 1 || distToTarget > maxDist) continue;

                float d = Heuristic(c, from);
                if (d < bestDist) { bestDist = d; best = c; }
            }
        return best;
    }

    public Vector2Int? FindNearestRevealedFrom(Vector2Int pos, IGridContext gm,
        Vector2Int heroPos)
    {
        var dirs = new[]
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right,
            new Vector2Int(1,1), new Vector2Int(-1,1),
            new Vector2Int(1,-1), new Vector2Int(-1,-1)
        };
        foreach (var d in dirs)
        {
            var n = pos + d;
            if (!InBounds(n, gm)) continue;
            var cell = gm.GetCell(n.x, n.y);
            if (cell != null && cell.IsRevealed && !IsBlocked(n, gm, heroPos))
                return n;
        }
        return null;
    }

    public Vector2Int? FindBestLaunchPad(Vector2Int hero, Vector2Int target,
        IGridContext gm)
    {
        Vector2Int? best = null;
        float bestScore = float.MaxValue;

        for (int maxD = 1; maxD <= 3; maxD++)
            for (int dx2 = -maxD; dx2 <= maxD; dx2++)
                for (int dy2 = -maxD; dy2 <= maxD; dy2++)
                {
                    if (Mathf.Max(Mathf.Abs(dx2), Mathf.Abs(dy2)) != maxD) continue;
                    var c = new Vector2Int(target.x + dx2, target.y + dy2);
                    if (!InBounds(c, gm)) continue;
                    var cell = gm.GetCell(c.x, c.y);
                    if (cell == null || !cell.IsRevealed) continue;
                    if (IsBlocked(c, gm, hero) && c != hero) continue;

                    float dh = Mathf.Max(Mathf.Abs(c.x - hero.x), Mathf.Abs(c.y - hero.y));
                    float score = maxD * 100f + dh;
                    if (score < bestScore) { bestScore = score; best = c; }
                }
        return best;
    }

    public Vector2Int FindNearestFree(Vector2Int blocked, Vector2Int from,
        IGridContext gm)
    {
        var dir = new Vector2Int(
            (int)Mathf.Sign(from.x - blocked.x),
            (int)Mathf.Sign(from.y - blocked.y));
        for (int i = 1; i <= 4; i++)
        {
            var c = blocked + dir * i;
            if (InBounds(c, gm) && IsRevealed(c, gm)
                && !IsBlocked(c, gm, from)) return c;
        }
        return from;
    }

    public List<Vector2Int> Reconstruct(Dictionary<Vector2Int, Vector2Int> came,
        Vector2Int end, Vector2Int start)
    {
        _astarResult.Clear();
        var path = _astarResult;
        var cur = end;
        while (came.ContainsKey(cur) && cur != start)
        { path.Insert(0, cur); cur = came[cur]; }
        return path;
    }

    public void LogNoJump(Vector2Int current, Vector2Int target,
        IGridContext gm)
    {
        Debug.Log("[Hero] Aucun saut depuis " + current + " vers " + target);
        var dirs = new[] {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (var d in dirs)
        {
            string line = "  " + d + ": ";
            for (int dd = 1; dd <= 3; dd++)
            {
                var cc = current + d * dd;
                if (!InBounds(cc, gm)) { line += "[BORD]"; break; }
                var c2 = gm.GetCell(cc.x, cc.y);
                if (c2 == null) { line += "[NULL]"; break; }
                line += c2.IsRevealed
                    ? (IsBlocked(cc, gm, current) ? "[REV-BLK]" : "[REV-OK]")
                    : "[FOG]";
                line += " ";
            }
            Debug.Log(line);
        }
    }
}
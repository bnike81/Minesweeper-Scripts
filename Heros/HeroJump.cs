using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// HeroJump - Saut vers case non decouverte.
/// Appele par HeroController.ApproachAndJump apres approche.
/// Accepte dist 1 a maxJumpDistance depuis la position du heros.
/// Arc parabolique : envol frame 2, atterrissage frame 6.
/// </summary>
public class HeroJump : MonoBehaviour
{
    public static HeroJump Instance { get; private set; }

    [Header("=== Sprites Saut Face Droite (7 frames) ===")]
    [SerializeField] private Sprite[] _jumpFrontRight = new Sprite[7];
    [Header("=== Sprites Saut Face Gauche ===")]
    [SerializeField] private Sprite[] _jumpFrontLeft = new Sprite[7];
    [Header("=== Sprites Saut Dos Droite ===")]
    [SerializeField] private Sprite[] _jumpBackRight = new Sprite[7];
    [Header("=== Sprites Saut Dos Gauche ===")]
    [SerializeField] private Sprite[] _jumpBackLeft = new Sprite[7];
    [Header("=== Sprites Saut Lateral Droite ===")]
    [SerializeField] private Sprite[] _jumpSideRight = new Sprite[7];
    [Header("=== Sprites Saut Lateral Gauche ===")]
    [SerializeField] private Sprite[] _jumpSideLeft = new Sprite[7];

    [Header("=== Parametres ===")]
    [SerializeField, Range(1, 3)] private int _maxJumpDistance = 3;
    [SerializeField, Range(0.2f, 3f)] private float _jumpArcHeight = 1.2f;
    [SerializeField, Range(0.3f, 1.5f)] private float _jumpDuration = 0.65f;
    [SerializeField, Range(0.03f, 0.15f)] private float _frameTime = 0.07f;

    public bool IsJumping { get; private set; } = false;
    public int MaxJumpDistance => _maxJumpDistance;

    private HeroController _hero;
    private SpriteRenderer _heroSR;
    private float _cellStep = 1.05f;

    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _hero = HeroController.Instance;
        _heroSR = _hero?.GetComponent<SpriteRenderer>();
        _cellStep = ActiveGrid.Current?.CellStep ?? 1.05f;
    }

    private void OnEnable() { EventBus.Subscribe<OnGridGenerated>(OnGrid); }
    private void OnDisable() { EventBus.Unsubscribe<OnGridGenerated>(OnGrid); }
    private void OnGrid(OnGridGenerated e) { _cellStep = ActiveGrid.Current?.CellStep ?? _cellStep; }

    // -------------------------------------------------------------------------
    // API publique
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lance le saut. Appelee apres que le heros soit arrive au launchpad.
    /// dist 1 = saut court, dist 2-3 = saut avec arc.
    /// </summary>
    public bool TryJump(Vector2Int from, Vector2Int target, IGridContext gm)
    {
        if (IsJumping) return false;

        var cell = gm.GetCell(target.x, target.y);
        if (cell == null || cell.IsRevealed) return false;

        int dist = Mathf.Max(
            Mathf.Abs(target.x - from.x),
            Mathf.Abs(target.y - from.y));

        if (dist < 1 || dist > _maxJumpDistance)
        {
            EventBus.Publish(new OnNotification
            {
                Message = "Trop loin pour sauter ! (max " + _maxJumpDistance + " cases)",
                Type = NotificationType.Warning
            });
            return false;
        }

        StartCoroutine(JumpRoutine(from, target, gm));
        return true;
    }

    /// <summary>
    /// Saut automatique vers une case DEJA REVELEE de l autre cote d arbres.
    /// Utilise par NavigateWithJumps - pas de revelation, juste traversee.
    /// </summary>
    public bool TryJumpToRevealed(Vector2Int from, Vector2Int target, IGridContext gm)
    {
        if (IsJumping) return false;

        var cell = gm.GetCell(target.x, target.y);
        if (cell == null) return false;
        // Accepte cases revelees ET non revelees

        int dist = Mathf.Max(
            Mathf.Abs(target.x - from.x),
            Mathf.Abs(target.y - from.y));

        if (dist < 1 || dist > _maxJumpDistance) return false;

        StartCoroutine(JumpRoutine(from, target, gm));
        return true;
    }

    /// <summary>
    /// Trouve le meilleur launchpad depuis hero vers target.
    /// Case revelee a dist 1-3 de target, la plus proche du heros.
    /// </summary>
    public Vector2Int? FindLaunchPad(Vector2Int hero, Vector2Int target, IGridContext gm)
    {
        Vector2Int? best = null;
        float bestScore = float.MaxValue;

        for (int maxD = 1; maxD <= _maxJumpDistance; maxD++)
            for (int dx = -maxD; dx <= maxD; dx++)
                for (int dy = -maxD; dy <= maxD; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != maxD) continue;

                    var c = new Vector2Int(target.x + dx, target.y + dy);
                    if (!InBounds(c, gm)) continue;

                    var cell = gm.GetCell(c.x, c.y);
                    if (cell == null || !cell.IsRevealed) continue;

                    float distFromHero = Mathf.Max(
                        Mathf.Abs(c.x - hero.x),
                        Mathf.Abs(c.y - hero.y));

                    // Score : priorite dist 1 de target, puis proximite heros
                    float score = maxD * 100f + distFromHero;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }
        return best;
    }

    // -------------------------------------------------------------------------
    // Animation saut
    // -------------------------------------------------------------------------

    private IEnumerator JumpRoutine(Vector2Int from, Vector2Int landing, IGridContext gm)
    {
        IsJumping = true;
        if (_hero != null) _hero.IsLocked = true;

        var sprites = GetJumpSprites(landing - from);
        Vector3 start = GridToWorld(from);
        Vector3 end = GridToWorld(landing);

        int frame = 0;
        bool revealed = false;
        float elapsed = 0f;

        while (elapsed < _jumpDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _jumpDuration);

            int targetFrame = Mathf.Min((int)(t * 7f), 6);
            if (targetFrame != frame)
            {
                frame = targetFrame;
                if (sprites != null && frame < sprites.Length && sprites[frame] != null)
                    if (_heroSR != null) _heroSR.sprite = sprites[frame];
            }

            // Arc actif entre frame 2 et 6
            float arcT = Mathf.Clamp01(Mathf.InverseLerp(2f / 7f, 6f / 7f, t));
            float arcY = Mathf.Sin(arcT * Mathf.PI) * _jumpArcHeight;

            // Deplacement actif entre frame 1 et 6
            float moveT = Mathf.Clamp01(Mathf.InverseLerp(1f / 7f, 6f / 7f, t));
            var pos = Vector3.Lerp(start, end, moveT);
            pos.y += arcY;
            pos.z = -0.5f;

            if (_hero != null) _hero.transform.position = pos;

            // Reveler a l atterrissage seulement si case non encore revelee
            // (saut manuel du joueur - pas le saut automatique sur case revelee)
            if (!revealed && frame >= 6)
            {
                revealed = true;
                var landCell = gm.GetCell(landing.x, landing.y);
                if (landCell != null && !landCell.IsRevealed)
                    gm.RevealCell(landing.x, landing.y);
                if (_hero != null) _hero.SetGridPosition(landing);
            }

            yield return null;
        }

        if (_hero != null) _hero.transform.position = end;
        if (!revealed)
        {
            var lc = gm.GetCell(landing.x, landing.y);
            if (lc != null && !lc.IsRevealed) gm.RevealCell(landing.x, landing.y);
        }
        if (_hero != null) _hero.SetGridPosition(landing);

        IsJumping = false;
        if (_hero != null)
        {
            _hero.IsLocked = false;
            _hero.RestoreIdleSprite();
        }
    }

    // -------------------------------------------------------------------------

    private Sprite[] GetJumpSprites(Vector2Int dir)
    {
        bool right = dir.x >= 0;
        if (Mathf.Abs(dir.x) > Mathf.Abs(dir.y))
            return right ? _jumpSideRight : _jumpSideLeft;
        if (dir.y > 0) return right ? _jumpBackRight : _jumpBackLeft;
        return right ? _jumpFrontRight : _jumpFrontLeft;
    }

    private bool InBounds(Vector2Int p, IGridContext gm)
        => p.x >= 0 && p.y >= 0 && p.x < gm.Width && p.y < gm.Height;

    private Vector3 GridToWorld(Vector2Int p)
        => new Vector3(p.x * _cellStep, p.y * _cellStep, -0.5f);
}
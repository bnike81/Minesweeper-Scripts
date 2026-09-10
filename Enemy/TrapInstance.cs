using UnityEngine;
using System.Collections;

/// <summary>
/// TrapInstance - Piege a loup sur la grille.
/// 
/// ETATS :
///   Open      : piege ouvert, dangereux
///   Closing   : animation fermeture (3 sprites)
///   Closed    : piege ferme, desamorce
/// 
/// LOGIQUE :
///   Clic 1 : heros arrive -> piege se referme -> -1 HP -> bloque
///   Clic 2 : heros se debat -> -1 HP -> toujours bloque
///   Clic 3 : heros se libere -> piege desamorce -> mouvement libre
/// </summary>
public class TrapInstance : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------

    [Header("=== Sprites ===")]
    [Tooltip("Piege ouvert - etat de base")]
    [SerializeField] private Sprite _spriteOpen;
    [Tooltip("Piege en train de se refermer")]
    [SerializeField] private Sprite _spriteClosing;
    [Tooltip("Piege ferme - desamorce")]
    [SerializeField] private Sprite _spriteClosed;

    [Header("=== Parametres ===")]
    [Tooltip("Degats infligés a chaque etape (1 et 2)")]
    [SerializeField, Range(1, 3)] private int _damagePerHit = 1;
    [Tooltip("Duree de l animation de fermeture")]
    [SerializeField, Range(0.1f, 0.6f)] private float _closeAnimDuration = 0.3f;
    [Tooltip("Duree du shake camera au declenchement")]
    [SerializeField, Range(0f, 0.3f)] private float _shakeDuration = 0.15f;

    [Header("=== Sorting ===")]
    [SerializeField] private string _sortingLayer = "CellContent";
    [SerializeField] private int _sortingOrder = 3;

    // -------------------------------------------------------------------------
    // Etat interne
    // -------------------------------------------------------------------------

    public enum TrapState { Open, Closing, Closed }
    public TrapState State { get; private set; } = TrapState.Open;

    // Etape de liberation : 0 = pas encore pris, 1 = pris (1HP), 2 = debat (2HP), 3 = libre
    private int _escapeStep = 0;

    private SpriteRenderer _sr;
    private Vector2Int _gridPos;
    private float _cellStep = 1.05f;

    // -------------------------------------------------------------------------

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        if (_sr != null)
        {
            _sr.sortingLayerName = _sortingLayer;
            _sr.sortingOrder = _sortingOrder;
        }
    }

    private void Start()
    {
        _cellStep = GridManager.Instance?.CellStep ?? 1.05f;
        int x = Mathf.RoundToInt(transform.position.x / _cellStep);
        int y = Mathf.RoundToInt(transform.position.y / _cellStep);
        _gridPos = new Vector2Int(x, y);

        if (_sr != null && _spriteOpen != null)
            _sr.sprite = _spriteOpen;
    }

    // -------------------------------------------------------------------------
    // Initialisation depuis le spawner
    // -------------------------------------------------------------------------

    public void Initialize(Vector2Int gridPos, float cellStep)
    {
        _gridPos = gridPos;
        _cellStep = cellStep;
        transform.position = new Vector3(
            gridPos.x * cellStep,
            gridPos.y * cellStep,
            -0.2f);

        if (_sr == null) _sr = GetComponent<SpriteRenderer>();
        if (_sr != null && _spriteOpen != null) _sr.sprite = _spriteOpen;
        State = TrapState.Open;
    }

    // -------------------------------------------------------------------------
    // API publique - appelee par HeroController a l arrivee sur la case
    // -------------------------------------------------------------------------

    /// <summary>
    /// Retourne true si le piege est encore actif et bloque le heros.
    /// </summary>
    public bool OnHeroStepOn()
    {
        if (State == TrapState.Closed) return false; // Desamorce - pas de blocage

        if (_escapeStep == 0)
        {
            // Premier contact : piege se referme
            _escapeStep = 1;
            StartCoroutine(TriggerSequence());
            return true; // Bloque le heros
        }

        return false;
    }

    /// <summary>
    /// Appelee par HeroController a chaque clic supplementaire pendant blocage.
    /// Retourne true si le heros est encore bloque.
    /// </summary>
    public bool OnHeroStruggle()
    {
        // Seulement actif si piege s est declenche
        if (_escapeStep == 0) return false;

        if (_escapeStep == 1)
        {
            // Deuxieme clic : se debat, -1 HP
            _escapeStep = 2;
            DamageHero();
            ShowStruggleEffect();
            return true; // Encore bloque
        }

        if (_escapeStep == 2)
        {
            // Troisieme clic : libere !
            _escapeStep = 3;
            Disarm();
            return false;
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Sequences
    // -------------------------------------------------------------------------

    private IEnumerator TriggerSequence()
    {
        State = TrapState.Closing;

        // Frame 1 : sprite fermeture
        if (_sr != null && _spriteClosing != null)
            _sr.sprite = _spriteClosing;

        // Son de claquement
        EventBus.Publish(new OnNotification
        {
            Message = "PIEGE ! Cliquer pour se liberer...",
            Type = NotificationType.Warning
        });

        yield return new WaitForSeconds(_closeAnimDuration);

        // Frame 2 : sprite ferme
        if (_sr != null && _spriteClosed != null)
            _sr.sprite = _spriteClosed;

        // Infliger degats
        DamageHero();

        // Bloquer le heros via HeroState
        if (HeroState.Instance != null)
            HeroState.Instance.IsUILocked = true;

        // Garder en etat Closing pour signaler que c'est actif
        // (pas Closed qui signifie desamorce)
        State = TrapState.Closing;
    }

    private void DamageHero()
    {
        // Infliger degats via EventBus - comme EnemyInstance
        EventBus.Publish(new OnPlayerDamaged
        {
            Damage = _damagePerHit,
            CurrentHP = 0,
            MaxHP = 0
        });

        // Effet visuel shake sur le heros
        StartCoroutine(ShakeHero());
    }

    private void ShowStruggleEffect()
    {
        // Petit shake supplementaire pour le debat
        StartCoroutine(ShakeHero());
        EventBus.Publish(new OnNotification
        {
            Message = "Encore un clic pour se liberer !",
            Type = NotificationType.Warning
        });
    }

    private void Disarm()
    {
        State = TrapState.Closed;

        if (_sr != null && _spriteClosed != null)
            _sr.sprite = _spriteClosed;

        // Debloquer le heros
        if (HeroState.Instance != null)
            HeroState.Instance.IsUILocked = false;

        EventBus.Publish(new OnNotification
        {
            Message = "Libere du piege !",
            Type = NotificationType.Info
        });

        Debug.Log("[Trap] Piege desamorce en " + _gridPos);
    }

    private IEnumerator ShakeHero()
    {
        var hero = HeroController.Instance;
        if (hero == null) yield break;

        var originalPos = hero.transform.position;
        float elapsed = 0f;
        float intensity = 0.04f;

        while (elapsed < _shakeDuration)
        {
            elapsed += Time.deltaTime;
            float x = originalPos.x + Random.Range(-intensity, intensity);
            float y = originalPos.y + Random.Range(-intensity, intensity);
            hero.transform.position = new Vector3(x, y, originalPos.z);
            yield return null;
        }

        hero.transform.position = originalPos;
    }

    // -------------------------------------------------------------------------
    // Proprietes
    // -------------------------------------------------------------------------

    public Vector2Int GridPosition => _gridPos;
    public bool IsArmed => State == TrapState.Open || State == TrapState.Closing;
}
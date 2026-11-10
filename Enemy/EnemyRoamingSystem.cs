using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// EnemyRoamingSystem — Systeme de ronde ennemie.
///
/// Les ennemis sur cases NON revelees se deplacent periodiquement
/// vers une case adjacente non revelee, simulant une patrouille invisible.
///
/// REGLES :
///   - Mode libre  : cycle tous les 3-5 clics de revelation du heros
///   - Mode combat : cycle tous les 7-10 tours de combat
///   - 15% des ennemis eligibles sont selectionnes aleatoirement par cycle
///   - Deplacement d'UNE case adjacente non revelee, sans chevauchement
///   - Animation shake feuillage sur la case de DEPART (foret/plage uniquement)
///   - Grotte : ronde active mais AUCUN feedback visuel
///   - Exclusion : buissons, cases frontieres foret/plage
///   - Ours : zone 2x2 shake special (3 hauteurs x 2 colonnes)
///
/// SETUP :
///   1. Creer un GameObject vide "EnemyRoamingSystem" dans la scene Core
///   2. Ajouter ce script
///   3. Assigner les 10 arrays de sprites shake dans l'Inspector
///
/// DEPENDANCES (patches a appliquer) :
///   - GridManager.MoveEnemyOnGrid(fromX, fromY, toX, toY) — Option B
///   - ITreeLayer.TreeLayerRenderer { get; } — propriete exposee
///   - ForestCellView / BeachCellView : implementer TreeLayerRenderer
/// </summary>
public class EnemyRoamingSystem : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────
    public static EnemyRoamingSystem Instance { get; private set; }

    // ─── Inspector — Seuils ───────────────────────────────────────────────────

    [Header("=== Seuils de ronde ===")]
    [Tooltip("Actions min/max en mode libre avant un cycle de ronde")]
    [SerializeField] private int _freeMinActions = 3;
    [SerializeField] private int _freeMaxActions = 5;

    [Tooltip("Actions min/max en mode combat avant un cycle de ronde")]
    [SerializeField] private int _combatMinActions = 7;
    [SerializeField] private int _combatMaxActions = 10;

    [Header("=== Pourcentage de ronde ===")]
    [Tooltip("Pourcentage d'ennemis eligibles qui font leur ronde par cycle")]
    [SerializeField, Range(0.05f, 0.5f)] private float _roamPercent = 0.15f;

    // ─── Inspector — Shake Arbre Foret (2 sprites par array) ──────────────────

    [Header("=== Shake Arbre Foret ===")]
    [Tooltip("Tronc secoue (depart en bas de foret, case revelee en-dessous)")]
    [SerializeField] private Sprite[] _forestShakeTronc;

    [Tooltip("Cime secouee (depart en haut de foret, case revelee au-dessus)")]
    [SerializeField] private Sprite[] _forestShakeCime;

    [Tooltip("Canope bas : couche avant (cime) secoue, tronc arriere stable")]
    [SerializeField] private Sprite[] _forestShakeCanopeBas;

    [Tooltip("Canope haut : les deux couches secouent (cime devant + tronc derriere)")]
    [SerializeField] private Sprite[] _forestShakeCanopeHaut;

    [Tooltip("Canope ensemble : tout secoue — utilise pour zone ours 2x2")]
    [SerializeField] private Sprite[] _forestShakeCanopeEnsemble;

    // ─── Inspector — Shake Palmier Plage (2 sprites par array) ────────────────

    [Header("=== Shake Palmier Plage ===")]
    [SerializeField] private Sprite[] _palmShakeTronc;
    [SerializeField] private Sprite[] _palmShakeCime;
    [SerializeField] private Sprite[] _palmShakeCanopeBas;
    [SerializeField] private Sprite[] _palmShakeCanopeHaut;
    [SerializeField] private Sprite[] _palmShakeCanopeEnsemble;

    // ─── Inspector — Timing animation ─────────────────────────────────────────

    [Header("=== Animation ===")]
    [Tooltip("Duree par frame de l'animation shake (secondes)")]
    [SerializeField] private float _shakeFrameTime = 0.12f;

    [Tooltip("Nombre d'oscillations (1 oscillation = frame1 puis frame2)")]
    [SerializeField] private int _shakeOscillations = 2;

    [Header("=== Vagues de ronde ===")]
    [Tooltip("Nombre de vagues par cycle de ronde (les ennemis sont repartis en lots)")]
    [SerializeField, Range(2, 8)] private int _waveCount = 5;

    [Tooltip("Delai minimum entre deux vagues (secondes)")]
    [SerializeField] private float _waveDelayMin = 1f;

    [Tooltip("Delai maximum entre deux vagues (secondes)")]
    [SerializeField] private float _waveDelayMax = 3f;

    [Header("=== Debug ===")]
    [SerializeField] private bool _enableDebugLogs = false;

    // ─── Enum interne — type de shake ─────────────────────────────────────────

    private enum ShakeType
    {
        Tronc,            // Case de depart = bas de foret (Trunk)
        Cime,             // Case de depart = haut de foret (Cime)
        CanopeBas,        // Case au-dessus du depart : seule la couche avant secoue
        CanopeHaut,       // Case du depart ou en-dessous : les deux couches secouent
        CanopeEnsemble    // Zone ours : tout secoue (les deux couches pleinement)
    }

    // ─── Etat interne ─────────────────────────────────────────────────────────

    private int _actionCounter = 0;
    private int _currentThreshold;
    private int _lastActionFrame = -1; // debounce : 1 clic = 1 action
    private bool _isRoamingInProgress = false; // verrou anti-concurrence des vagues

    // Cooldown : position → cycles restants avant re-eligibilite
    private readonly Dictionary<(int x, int y), int> _roamCooldowns = new();

    [Header("=== Cooldown ronde ===")]
    [Tooltip("Nombre de cycles de ronde qu'un ennemi doit attendre avant de refaire une ronde")]
    [SerializeField] private int _roamCooldownCycles = 2;

    private static readonly (int dx, int dy)[] Directions =
    {
        (-1, -1), (0, -1), (1, -1),
        (-1,  0),          (1,  0),
        (-1,  1), (0,  1), (1,  1)
    };

    // Buffers reutilisables
    private readonly List<(int x, int y, CellContent content)> _eligibleBuffer = new(32);
    private readonly List<(int dx, int dy)> _validDirsBuffer = new List<(int, int)>(8);

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnCellsRevealed>(OnCellsRevealed);
        EventBus.Subscribe<OnHeroActionPerformed>(OnHeroAction);
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnCellsRevealed>(OnCellsRevealed);
        EventBus.Unsubscribe<OnHeroActionPerformed>(OnHeroAction);
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
    }

    private void OnRunStarted(OnRunStarted _)
    {
        _actionCounter = 0;
        _lastActionFrame = -1;
        _isRoamingInProgress = false;
        _roamCooldowns.Clear();
        StopAllCoroutines(); // arreter les vagues en cours si nouvelle partie
        RollThreshold();
    }

    // =========================================================================
    // COMPTAGE DES ACTIONS
    // =========================================================================

    /// <summary>
    /// Appelee par le TurnManager a la fin de chaque tour ennemi en combat.
    /// Compte comme une action pour le systeme de ronde.
    /// </summary>
    public void NotifyCombatTurnComplete()
    {
        IncrementAction();
    }

    /// <summary>
    /// Appelee quand le heros effectue une action validee (clic → deplacement,
    /// revelation, collecte, interaction). Publie par HeroController.Update().
    /// Couvre TOUS les types de clics en mode libre.
    /// </summary>
    private void OnHeroAction(OnHeroActionPerformed _)
    {
        // En combat, seul NotifyCombatTurnComplete compte
        if (TurnManager.Instance != null && TurnManager.Instance.IsActive)
            return;

        // 1 clic = 1 action : ignorer les doublons du meme frame
        if (Time.frameCount == _lastActionFrame) return;
        _lastActionFrame = Time.frameCount;

        IncrementAction();
    }

    /// <summary>
    /// OnCellsRevealed sert de filet de securite : si OnHeroActionPerformed
    /// n'a pas ete publie (ex: revelation declenchee par un autre systeme),
    /// on compte quand meme l'action.
    /// </summary>
    private void OnCellsRevealed(OnCellsRevealed evt)
    {
        if (evt.IsSummary) return;

        // En combat, seul NotifyCombatTurnComplete compte
        if (TurnManager.Instance != null && TurnManager.Instance.IsActive)
            return;

        // Si deja compte par OnHeroAction dans ce frame, ignorer
        if (Time.frameCount == _lastActionFrame) return;
        _lastActionFrame = Time.frameCount;

        IncrementAction();
    }

    private void IncrementAction()
    {
        _actionCounter++;

        if (_enableDebugLogs)
            Log($"Action {_actionCounter}/{_currentThreshold}");

        if (_actionCounter >= _currentThreshold)
        {
            _actionCounter = 0;
            ExecuteRoamingCycle();
            RollThreshold();
        }
    }

    private void RollThreshold()
    {
        bool inCombat = TurnManager.Instance != null && TurnManager.Instance.IsActive;
        int min = inCombat ? _combatMinActions : _freeMinActions;
        int max = inCombat ? _combatMaxActions : _freeMaxActions;
        _currentThreshold = Random.Range(min, max + 1);

        if (_enableDebugLogs)
            Log($"Nouveau seuil : {_currentThreshold} (combat={inCombat})");
    }

    // =========================================================================
    // CYCLE DE RONDE
    // =========================================================================

    private void ExecuteRoamingCycle()
    {
        // Empecher un nouveau cycle tant que le precedent n'est pas termine
        if (_isRoamingInProgress)
        {
            if (_enableDebugLogs) Log("Cycle ignore — vagues precedentes en cours");
            return;
        }

        var gm = GridManager.Instance;
        if (gm == null) return;

        int w = gm.Width;
        int h = gm.Height;

        // 0. Tick des cooldowns : decrementer puis retirer les expires
        TickCooldowns();

        // 1. Parcourir TOUTE la grille pour trouver les ennemis caches
        //    (les ennemis non reveles n'ont pas d'EnemyInstance)
        _eligibleBuffer.Clear();

        int totalHidden = 0;
        int buissonCount = 0;
        int frontierCount = 0;
        int cooldownCount = 0;
        int noAdjacentCount = 0;

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                var cell = gm.GetCell(x, y);
                if (cell == null) continue;
                if (cell.IsRevealed) continue;
                if (!cell.IsEnemy && !cell.IsBoss) continue;

                totalHidden++;

                // Exclusion : buissons (case isolee entre revelees)
                if (IsBuisson(gm, x, y)) { buissonCount++; continue; }

                // Exclusion : cases frontieres foret/plage
                if (IsFrontierCell(gm, x, y)) { frontierCount++; continue; }

                // Exclusion : cooldown (a deja fait sa ronde recemment)
                if (_roamCooldowns.ContainsKey((x, y))) { cooldownCount++; continue; }

                // Verifier qu'il y a au moins une case adjacente valide
                if (HasValidAdjacentCell(gm, x, y))
                    _eligibleBuffer.Add((x, y, cell.Content));
                else
                    noAdjacentCount++;
            }
        }

        if (_enableDebugLogs)
            Log($"Diagnostic : cachés={totalHidden}, buissons={buissonCount}, " +
                $"frontière={frontierCount}, cooldown={cooldownCount}, " +
                $"pas_adj={noAdjacentCount}, éligibles={_eligibleBuffer.Count}");

        if (_eligibleBuffer.Count == 0)
        {
            if (_enableDebugLogs) Log("Aucun ennemi eligible pour la ronde");
            return;
        }

        // 2. Selectionner 15% (minimum 1) avec DISPERSION spatiale
        int count = Mathf.Max(1, Mathf.RoundToInt(_eligibleBuffer.Count * _roamPercent));
        var selected = SelectDispersed(_eligibleBuffer, count);

        if (_enableDebugLogs)
            Log($"Ronde : {selected.Count}/{_eligibleBuffer.Count} ennemis selectionnes (disperses)");

        // 3. Repartir en vagues et lancer la coroutine echelonnee
        //    Copier la selection car le buffer sera reutilise
        var snapshot = new List<(int x, int y, CellContent content)>(selected);
        _isRoamingInProgress = true;
        StartCoroutine(ExecuteRoamingWaves(snapshot, gm));
    }

    /// <summary>
    /// Execute la ronde en plusieurs vagues decalees dans le temps.
    /// Chaque vague deplace un lot d'ennemis disperses, avec un delai
    /// aleatoire de 1 a 3 secondes entre chaque vague.
    /// </summary>
    private IEnumerator ExecuteRoamingWaves(
        List<(int x, int y, CellContent content)> enemies, GridManager gm)
    {
        if (enemies.Count == 0) yield break;

        // Repartir les ennemis en vagues (min 1 ennemi par vague)
        int waves = Mathf.Min(_waveCount, enemies.Count);
        int baseSize = enemies.Count / waves;
        int remainder = enemies.Count % waves;

        int index = 0;
        for (int w = 0; w < waves; w++)
        {
            // Les premieres 'remainder' vagues prennent un ennemi de plus
            int waveSize = baseSize + (w < remainder ? 1 : 0);

            if (_enableDebugLogs)
                Log($"Vague {w + 1}/{waves} : {waveSize} ennemis");

            for (int i = 0; i < waveSize && index < enemies.Count; i++, index++)
            {
                var (ex, ey, content) = enemies[index];
                // Verifier que l'ennemi est toujours la (pas revele entre-temps)
                var cell = gm.GetCell(ex, ey);
                if (cell != null && !cell.IsRevealed && (cell.IsEnemy || cell.IsBoss))
                    MoveHiddenEnemy(ex, ey, content, gm);
            }

            // Delai aleatoire avant la prochaine vague (sauf apres la derniere)
            if (w < waves - 1)
            {
                float delay = Random.Range(_waveDelayMin, _waveDelayMax);
                yield return new WaitForSeconds(delay);
            }
        }

        _isRoamingInProgress = false;

        if (_enableDebugLogs)
            Log("Toutes les vagues de ronde terminees");
    }

    // =========================================================================
    // DEPLACEMENT D'UN ENNEMI
    // =========================================================================

    /// <summary>
    /// Deplace un ennemi CACHE (pas d'EnemyInstance, juste CellContent dans la grille).
    /// Swap le contenu via GridManager, puis lance l'animation shake.
    /// </summary>
    private void MoveHiddenEnemy(int oldX, int oldY, CellContent content, GridManager gm)
    {
        // Trouver les cases adjacentes valides
        _validDirsBuffer.Clear();
        int w = gm.Width;
        int h = gm.Height;

        foreach (var (dx, dy) in Directions)
        {
            int nx = oldX + dx;
            int ny = oldY + dy;

            if (!MinesweeperLogic.IsInBounds(nx, ny, w, h)) continue;

            var targetCell = gm.GetCell(nx, ny);
            if (targetCell == null) continue;

            // La case cible doit etre : non revelee, vide
            if (targetCell.IsRevealed) continue;
            if (targetCell.IsEnemy || targetCell.IsBoss
                || targetCell.IsTrap || targetCell.IsTreasure) continue;
            if (targetCell.IsMountainReserved) continue;

            _validDirsBuffer.Add((dx, dy));
        }

        if (_validDirsBuffer.Count == 0) return;

        // Choisir une direction aleatoire
        var chosen = _validDirsBuffer[Random.Range(0, _validDirsBuffer.Count)];
        int newX = oldX + chosen.dx;
        int newY = oldY + chosen.dy;

        // ── Option B : deleguer le swap de contenu a GridManager ──
        gm.MoveEnemyOnGrid(oldX, oldY, newX, newY);

        // Enregistrer le cooldown sur la NOUVELLE position
        _roamCooldowns[(newX, newY)] = _roamCooldownCycles;

        // Pas de SetGridPosition — l'ennemi cache n'a pas d'EnemyInstance.
        // Si un EnemyInstance existe (ennemi revele qui fait sa ronde), le mettre a jour
        var ei = FindEnemyInstanceAt(oldX, oldY);
        if (ei != null) ei.SetGridPosition(newX, newY);

        // ── Animation shake feuillage sur la case de DEPART ──
        bool isBear = (content == CellContent.Enemy_Bear);
        TriggerFoliageShake(oldX, oldY, isBear, gm);

        if (_enableDebugLogs)
            Log($"{content} deplace ({oldX},{oldY}) -> ({newX},{newY})");
    }

    /// <summary>
    /// Cherche un EnemyInstance a une position donnee (pour les rares cas
    /// ou un ennemi revele serait eligible — par securite).
    /// </summary>
    private EnemyInstance FindEnemyInstanceAt(int x, int y)
    {
        foreach (var ei in EnemyInstance.All)
        {
            if (ei != null && !ei.IsDead && ei.GridX == x && ei.GridY == y)
                return ei;
        }
        return null;
    }

    // =========================================================================
    // SHAKE FEUILLAGE — DISPATCH
    // =========================================================================

    /// <summary>
    /// Point d'entree du shake. Determine si on joue un shake standard
    /// (2 cases) ou un shake ours (3 hauteurs x 2 colonnes).
    /// Aucun visuel en grotte.
    /// </summary>
    private void TriggerFoliageShake(int departX, int departY, bool isBear, GridManager gm)
    {
        // Pas d'animation en grotte
        if (CaveManager.Instance != null && CaveManager.Instance.IsInCave)
            return;

        var departCell = gm.GetCell(departX, departY);
        if (departCell == null) return;

        BiomeType biome = departCell.Biome;
        if (biome != BiomeType.Forest && biome != BiomeType.Beach)
            return;

        if (isBear)
            TriggerBearShake(departX, departY, gm);
        else
            TriggerStandardShake(departX, departY, gm);
    }

    // =========================================================================
    // SHAKE STANDARD — 2 cases affectees
    // =========================================================================

    /// <summary>
    /// Shake standard (loup, bandit, etc.) : 2 cases affectees.
    ///
    /// Selon le TreeLayerType de la case de depart :
    ///   Canopy (milieu foret) : y0 = canope haut, y+1 = canope bas
    ///   Trunk  (bas de foret) : y0 = tronc,       y+1 = canope bas
    ///   Cime   (haut de foret) : y0 = cime,        y-1 = canope haut
    /// </summary>
    private void TriggerStandardShake(int x, int y, GridManager gm)
    {
        var layerType = ComputeTreeLayerType(gm, x, y);

        switch (layerType)
        {
            case ForestCellView.TreeLayerType.Canopy:
                // Milieu de foret : les deux couches de y0 secouent,
                // y+1 seulement la couche avant (cime de l'arbre de y0)
                PlayShakeOnCell(x, y, ShakeType.CanopeHaut);
                PlayShakeOnCell(x, y + 1, ShakeType.CanopeBas);
                break;

            case ForestCellView.TreeLayerType.Trunk:
                // Bas de foret : y0 est un tronc expose,
                // y+1 est la cime de cet arbre (couche avant secoue)
                PlayShakeOnCell(x, y, ShakeType.Tronc);
                PlayShakeOnCell(x, y + 1, ShakeType.CanopeBas);
                break;

            case ForestCellView.TreeLayerType.Cime:
                // Haut de foret : y0 est une cime exposee,
                // y-1 contient le tronc de cet arbre (les deux couches secouent)
                PlayShakeOnCell(x, y, ShakeType.Cime);
                PlayShakeOnCell(x, y - 1, ShakeType.CanopeHaut);
                break;

            case ForestCellView.TreeLayerType.Buisson:
                // Ne devrait pas arriver (exclu du roaming)
                break;
        }
    }

    // =========================================================================
    // SHAKE OURS — zone 2x2, 3 hauteurs x 2 colonnes
    // =========================================================================

    /// <summary>
    /// Shake ours (Enemy_Bear) : zone 2x2 → 3 rangees de hauteur x 2 colonnes.
    ///   y+1 = canope bas   (les deux colonnes)
    ///   y0  = canope ensemble (les deux colonnes)
    ///   y-1 = canope haut  (les deux colonnes)
    /// Colonne 2 : x+1 si valide, sinon x-1.
    /// </summary>
    private void TriggerBearShake(int x, int y, GridManager gm)
    {
        // Choisir la deuxieme colonne
        int x2;
        if (MinesweeperLogic.IsInBounds(x + 1, y, gm.Width, gm.Height))
            x2 = x + 1;
        else if (MinesweeperLogic.IsInBounds(x - 1, y, gm.Width, gm.Height))
            x2 = x - 1;
        else
            x2 = x; // Fallback : une seule colonne

        int[] columns = (x2 != x) ? new[] { x, x2 } : new[] { x };

        foreach (int col in columns)
        {
            PlayShakeOnCell(col, y + 1, ShakeType.CanopeBas);
            PlayShakeOnCell(col, y, ShakeType.CanopeEnsemble);
            PlayShakeOnCell(col, y - 1, ShakeType.CanopeHaut);
        }
    }

    // =========================================================================
    // SHAKE — ANIMATION SUR UNE CELLULE
    // =========================================================================

    /// <summary>
    /// Joue l'animation shake sur une cellule specifique.
    /// Accede au TreeLayerRenderer via ITreeLayer pour swapper les sprites.
    /// Determine le biome de la cellule elle-meme (pas celui du depart).
    /// </summary>
    private void PlayShakeOnCell(int x, int y, ShakeType shakeType)
    {
        var mg = MainGrid.Instance;
        if (mg == null) return;

        var gm = GridManager.Instance;
        if (gm == null) return;

        // Verifier les bornes
        if (!MinesweeperLogic.IsInBounds(x, y, gm.Width, gm.Height)) return;

        // Verifier que la cellule est cachee
        var cell = gm.GetCell(x, y);
        if (cell == null || cell.IsRevealed) return;

        // Biome de CETTE cellule (peut etre different du depart pour l'ours)
        BiomeType cellBiome = cell.Biome;
        if (cellBiome != BiomeType.Forest && cellBiome != BiomeType.Beach)
            return;

        // Obtenir le SpriteRenderer du tree layer via ITreeLayer
        var view = mg.GetCellView(x, y);
        if (view == null) return;
        if (!(view is ITreeLayer treeLayer)) return;

        var renderer = treeLayer.TreeLayerRenderer;
        if (renderer == null || !renderer.enabled) return;

        // Obtenir les 2 sprites de shake correspondants
        Sprite[] shakeSprites = GetShakeSprites(cellBiome, shakeType);
        if (shakeSprites == null || shakeSprites.Length < 2) return;

        StartCoroutine(PlayShakeCoroutine(renderer, shakeSprites));
    }

    /// <summary>
    /// Coroutine : oscille entre 2 sprites shake puis restaure l'original.
    /// Toutes les cellules affectees secouent en parallele (coroutines simultanees).
    /// </summary>
    private IEnumerator PlayShakeCoroutine(SpriteRenderer renderer, Sprite[] shakeSprites)
    {
        if (renderer == null) yield break;

        // Sauvegarder le sprite d'origine pour restauration
        Sprite originalSprite = renderer.sprite;

        // Oscillations : frame1 → frame2 → frame1 → frame2 → ...
        for (int i = 0; i < _shakeOscillations; i++)
        {
            if (renderer == null) yield break;
            renderer.sprite = shakeSprites[0];
            yield return new WaitForSeconds(_shakeFrameTime);

            if (renderer == null) yield break;
            renderer.sprite = shakeSprites[1];
            yield return new WaitForSeconds(_shakeFrameTime);
        }

        // Restaurer le sprite original
        if (renderer != null)
            renderer.sprite = originalSprite;
    }

    // ─── Selection des sprites shake par biome + type ─────────────────────────

    private Sprite[] GetShakeSprites(BiomeType biome, ShakeType type)
    {
        if (biome == BiomeType.Forest)
        {
            return type switch
            {
                ShakeType.Tronc => _forestShakeTronc,
                ShakeType.Cime => _forestShakeCime,
                ShakeType.CanopeBas => _forestShakeCanopeBas,
                ShakeType.CanopeHaut => _forestShakeCanopeHaut,
                ShakeType.CanopeEnsemble => _forestShakeCanopeEnsemble,
                _ => null
            };
        }

        if (biome == BiomeType.Beach)
        {
            return type switch
            {
                ShakeType.Tronc => _palmShakeTronc,
                ShakeType.Cime => _palmShakeCime,
                ShakeType.CanopeBas => _palmShakeCanopeBas,
                ShakeType.CanopeHaut => _palmShakeCanopeHaut,
                ShakeType.CanopeEnsemble => _palmShakeCanopeEnsemble,
                _ => null
            };
        }

        return null;
    }

    // =========================================================================
    // DETECTION DES TYPES DE CASES
    // =========================================================================

    /// <summary>
    /// Calcule le TreeLayerType d'une case cachee.
    /// Reproduit exactement la logique de MainGrid.RefreshTreeColumn.
    /// </summary>
    private ForestCellView.TreeLayerType ComputeTreeLayerType(GridManager gm, int x, int y)
    {
        bool belowRevealed = (y == 0) || gm.Grid[x, y - 1].IsRevealed;
        bool aboveRevealed = (y >= gm.Height - 1) || gm.Grid[x, y + 1].IsRevealed;

        if (belowRevealed && aboveRevealed) return ForestCellView.TreeLayerType.Buisson;
        if (belowRevealed) return ForestCellView.TreeLayerType.Trunk;
        if (aboveRevealed) return ForestCellView.TreeLayerType.Cime;
        return ForestCellView.TreeLayerType.Canopy;
    }

    /// <summary>
    /// Detecte si la case est un buisson (isolee entre cases revelees).
    /// Les ennemis dans les buissons ne font pas de ronde.
    /// </summary>
    private bool IsBuisson(GridManager gm, int x, int y)
    {
        return ComputeTreeLayerType(gm, x, y) == ForestCellView.TreeLayerType.Buisson;
    }

    /// <summary>
    /// Detecte si la case porte un sprite de transition canopee arbre↔palmier.
    /// Seules les transitions VERTICALES (y-1, y+1) produisent ces sprites
    /// (logique MainGrid.SetCanopyTransition). Les voisins horizontaux et
    /// diagonaux d'un biome different n'affectent pas les sprites canopee.
    /// Les ennemis sur ces cases ne font pas de ronde
    /// (les sprites de transition ne matchent pas les sprites shake).
    /// </summary>
    private bool IsFrontierCell(GridManager gm, int x, int y)
    {
        var cell = gm.GetCell(x, y);
        if (cell == null) return false;

        BiomeType myBiome = cell.Biome;
        if (myBiome != BiomeType.Forest && myBiome != BiomeType.Beach)
            return false;

        // Verifier uniquement les voisins verticaux (y-1 et y+1)
        // C'est la que MainGrid genere les sprites de transition canopee
        var below = gm.GetCell(x, y - 1);
        if (below != null)
        {
            if (myBiome == BiomeType.Forest && below.Biome == BiomeType.Beach)
                return true;
            if (myBiome == BiomeType.Beach && below.Biome == BiomeType.Forest)
                return true;
        }

        var above = gm.GetCell(x, y + 1);
        if (above != null)
        {
            if (myBiome == BiomeType.Forest && above.Biome == BiomeType.Beach)
                return true;
            if (myBiome == BiomeType.Beach && above.Biome == BiomeType.Forest)
                return true;
        }

        return false;
    }

    // =========================================================================
    // UTILITAIRES
    // =========================================================================

    private bool HasValidAdjacentCell(GridManager gm, int cx, int cy)
    {
        int w = gm.Width;
        int h = gm.Height;

        foreach (var (dx, dy) in Directions)
        {
            int nx = cx + dx;
            int ny = cy + dy;
            if (!MinesweeperLogic.IsInBounds(nx, ny, w, h)) continue;

            var cell = gm.GetCell(nx, ny);
            if (cell == null) continue;

            if (!cell.IsRevealed
                && !cell.IsEnemy && !cell.IsBoss
                && !cell.IsTrap && !cell.IsTreasure
                && !cell.IsMountainReserved)
                return true;
        }
        return false;
    }

    // ─── Selection dispersee ────────────────────────────────────────────────

    private readonly List<(int x, int y, CellContent content)> _selectedBuffer = new(16);

    /// <summary>
    /// Selectionne 'count' ennemis parmi les eligibles en maximisant
    /// la distance entre eux (algorithme glouton furthest-point).
    ///   1. Premier ennemi choisi au hasard
    ///   2. Chaque suivant = celui le plus eloigne de TOUS les deja choisis
    /// Resultat : repartition bien etalee sur toute la grille.
    /// </summary>
    private List<(int x, int y, CellContent content)> SelectDispersed(
        List<(int x, int y, CellContent content)> candidates, int count)
    {
        _selectedBuffer.Clear();
        if (candidates.Count == 0 || count <= 0) return _selectedBuffer;

        // 1. Premier au hasard
        int firstIdx = Random.Range(0, candidates.Count);
        _selectedBuffer.Add(candidates[firstIdx]);

        // Marquer les indices deja pris
        var used = new HashSet<int> { firstIdx };

        // 2. Chaque suivant = le plus eloigne de tous les selectionnes
        while (_selectedBuffer.Count < count && used.Count < candidates.Count)
        {
            int bestIdx = -1;
            float bestMinDist = -1f;

            for (int i = 0; i < candidates.Count; i++)
            {
                if (used.Contains(i)) continue;

                // Distance minimum vers le plus proche deja selectionne
                float minDist = float.MaxValue;
                foreach (var sel in _selectedBuffer)
                {
                    float dx = candidates[i].x - sel.x;
                    float dy = candidates[i].y - sel.y;
                    float dist = dx * dx + dy * dy; // distance au carre suffit
                    if (dist < minDist) minDist = dist;
                }

                // On veut celui dont la distance min est la PLUS GRANDE
                if (minDist > bestMinDist)
                {
                    bestMinDist = minDist;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0) break;
            _selectedBuffer.Add(candidates[bestIdx]);
            used.Add(bestIdx);
        }

        return _selectedBuffer;
    }

    /// <summary>
    /// Decremente tous les cooldowns de 1.
    /// Retire les entrees tombees a 0 (l'ennemi redevient eligible).
    /// Appelee au debut de chaque cycle de ronde.
    /// </summary>
    private void TickCooldowns()
    {
        if (_roamCooldowns.Count == 0) return;

        var expired = new List<(int, int)>();

        // Passe 1 : identifier les expires et decrementer les autres
        var keys = new List<(int x, int y)>(_roamCooldowns.Keys);
        foreach (var key in keys)
        {
            int remaining = _roamCooldowns[key] - 1;
            if (remaining <= 0)
                expired.Add(key);
            else
                _roamCooldowns[key] = remaining;
        }

        // Passe 2 : retirer les expires
        foreach (var key in expired)
            _roamCooldowns.Remove(key);

        if (_enableDebugLogs && expired.Count > 0)
            Log($"Cooldowns expirés : {expired.Count}, restants : {_roamCooldowns.Count}");
    }

    private void Log(string msg)
    {
        Debug.Log($"<color=#FF8800>[EnemyRoamingSystem]</color> {msg}");
    }
}
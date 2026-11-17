using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// CombatArena — Orchestrateur de l arène de combat.
///
/// Composant central du mode arène (test et futur mode Colisée).
/// Configuration via Inspector : taille grille, couleur sol, position héros,
/// liste d ennemis à placer.
///
/// Flow d init :
///   Start() → yield jusqu a ce que Core/UI/Hero soient chargés
///          → ArenaGrid.Build(width, height)
///          → BuildFloor() : instancie les ArenaCellView
///          → PlaceHero(heroStartPos)
///          → PlaceEnemies() : spawn chaque entry de la liste
///          → fini — le combat se jouera naturellement au clic
///
/// REUTILISABLE :
///   - En scène TestArena.unity (standalone, utilise ArenaBootstrap)
///   - Dans Indoor (futur mode Colisée en ville) sans ArenaBootstrap
///
/// MODE STAGE (chantier Stage Arena, étape 2) :
///   Si activeStage est assigné, l arène est construite depuis ce StageConfig :
///   cases cachées, terrain, ennemis cachés, chiffres du démineur.
///   Touches : F1-F9 = stage de la palette, F10 = retour à l arène plate,
///   1-9 = config d ennemis par-dessus le stage, 0 = ennemis peints du stage.
/// </summary>
public class CombatArena : MonoBehaviour
{
    [Header("Grille")]
    [Min(3)] public int gridWidth = 10;
    [Min(3)] public int gridHeight = 10;
    [Tooltip("Espacement entre centres des cases. Identique aux autres grilles.")]
    public float cellStep = 1.05f;
    [Tooltip("Taille visuelle d une case. Si < cellStep, laisse un gap visible entre cases.")]
    public float cellVisualSize = 1.0f;
    [Tooltip("Couleur de fond des cases.")]
    public Color floorColor = new Color(0.82f, 0.74f, 0.58f);
    [Tooltip("Si vrai, decale la grille pour que son centre soit a (0,0). Sinon elle commence a (0,0).")]
    public bool centerGridOnZero = true;

    [Header("Caméra")]
    [Tooltip("Si vrai, positionne la Main Camera pour englober toute la grille.")]
    public bool autoPositionCamera = true;
    [Tooltip("Marge autour de la grille (en cases).")]
    public float cameraMargin = 1.5f;

    [Header("Héros")]
    [Tooltip("Position initiale du héros dans la grille (x, y).")]
    public Vector2Int heroStartPos = new Vector2Int(5, 1);

    [Header("Ennemis — liste à spawner au start")]
    public List<EnemySpawnEntry> enemies = new List<EnemySpawnEntry>();

    [Header("Références (optionnel — auto si null)")]
    public ArenaGrid arenaGrid;
    public ArenaEnemyPlacer enemyPlacer;
    public Transform cellsParent;    // Parent GO pour les ArenaCellView instances

    [Header("Config externe (optionnel — si assignée, override les champs ci-dessus)")]
    [Tooltip("ScriptableObject ArenaConfig. Si assigné, ses valeurs écrasent " +
             "les champs locaux de CombatArena au BuildArena().")]
    public ArenaConfig activeConfig;

    [Header("Configs disponibles (switch runtime par touches 1-9)")]
    [Tooltip("Liste des configs accessibles par touches 1 à 9 pendant Play. " +
             "Touche R : reset avec config active. Touche 0 : repasser aux champs locaux.")]
    public List<ArenaConfig> availableConfigs = new List<ArenaConfig>();

    [Tooltip("Afficher le mapping touches ↔ configs au Start.")]
    public bool logKeyBindings = true;

    [Header("Stages (grilles peintes — touches F1 à F9)")]
    [Tooltip("Stage chargé au démarrage. Vide = arène plate classique.")]
    public StageConfig activeStage;
    [Tooltip("Palette de stages : le n°1 se charge avec F1, le n°2 avec F2, etc. F10 = arène plate.")]
    public StagePalette stagePalette;
    [Tooltip("Arrondit le pas de grille au pixel comme dans l aventure (1.05 → 1.0625). " +
             "À laisser coché pour que les sprites de côte et de montagne s alignent.")]
    public bool stagePixelSnap = true;
    [Tooltip("Place la case (0,0) du stage à l origine du monde, comme dans l aventure. " +
             "La caméra se centre toute seule.")]
    public bool stageOriginAtZero = true;

    [System.Serializable]
    public class EnemySpawnEntry
    {
        public CellContent enemyType = CellContent.Enemy_Wolf;
        public Vector2Int position = new Vector2Int(5, 5);
    }

    // Trackés pour le Reset
    private readonly List<GameObject> _spawnedCells = new List<GameObject>();
    private readonly List<EnemyInstance> _spawnedEnemies = new List<EnemyInstance>();

    // Offset de la grille en monde (calculé si centerGridOnZero=true)
    private Vector2 _gridOffset;

    // Vues et ennemis du stage en cours (mode stage uniquement)
    private StageRuntime _stageRuntime;

    private static readonly Key[] _stageKeys =
    {
        Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9
    };

    private IEnumerator Start()
    {
        // Attendre que Core/UI/Hero soient chargés (via ArenaBootstrap en standalone)
        yield return WaitForDependencies();

        // Auto-wire si pas setup dans Inspector
        if (arenaGrid == null) arenaGrid = GetComponent<ArenaGrid>() ?? gameObject.AddComponent<ArenaGrid>();
        if (enemyPlacer == null) enemyPlacer = GetComponent<ArenaEnemyPlacer>() ?? gameObject.AddComponent<ArenaEnemyPlacer>();

        BuildArena();

        if (logKeyBindings) LogKeyBindings();
    }

    /// <summary>
    /// Switch runtime via touches clavier.
    ///   F1-F9 : charger le stage stagePalette.stages[index-1]
    ///   F10   : quitter le mode stage (arène plate)
    ///   1-9   : charger la config availableConfigs[index-1]
    ///           (en mode stage : ses ennemis remplacent ceux du stage)
    ///   0     : arène plate → champs locaux ; mode stage → ennemis peints du stage
    ///   R     : reset avec le stage / la config actifs
    /// </summary>
    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        // Touches F1-F9 → stage de la palette
        for (int i = 0; i < _stageKeys.Length; i++)
        {
            if (!kb[_stageKeys[i]].wasPressedThisFrame) continue;

            var stage = stagePalette != null ? stagePalette.Get(i) : null;
            if (stage != null)
            {
                Debug.Log($"<color=#FFDD44>[CombatArena]</color> Switch → stage F{i + 1} '{stage.displayName}'");
                SwitchStage(stage);
            }
            else
            {
                Debug.Log($"<color=#FF8800>[CombatArena]</color> Touche F{i + 1} — " +
                          "pas de stage à cet emplacement de la palette");
            }
            return;
        }

        // Touche F10 → quitter le mode stage
        if (kb[Key.F10].wasPressedThisFrame)
        {
            if (activeStage != null)
            {
                Debug.Log("<color=#FFDD44>[CombatArena]</color> Sortie du mode stage → arène plate");
                SwitchStage(null);
            }
            return;
        }

        // Touches 1-9 → config availableConfigs[0..8]
        for (int i = 0; i < 9; i++)
        {
            var key = (Key)((int)Key.Digit1 + i);
            if (kb[key].wasPressedThisFrame)
            {
                if (i < availableConfigs.Count && availableConfigs[i] != null)
                {
                    Debug.Log($"<color=#FFDD44>[CombatArena]</color> Switch → config #{i + 1} " +
                              $"'{availableConfigs[i].displayName}'");
                    SwitchConfig(availableConfigs[i]);
                }
                else
                {
                    Debug.Log($"<color=#FF8800>[CombatArena]</color> Touche {i + 1} — " +
                              $"pas de config assignée a availableConfigs[{i}]");
                }
                return;
            }
        }

        // Touche 0 → repasser aux champs locaux
        if (kb[Key.Digit0].wasPressedThisFrame)
        {
            Debug.Log(activeStage != null
                ? "<color=#FFDD44>[CombatArena]</color> Switch → ennemis peints du stage"
                : "<color=#FFDD44>[CombatArena]</color> Switch → champs locaux (aucune config)");
            activeConfig = null;
            ResetArena();
            return;
        }

        // Touche R → reset avec la config active
        if (kb[Key.R].wasPressedThisFrame)
        {
            Debug.Log("<color=#FFDD44>[CombatArena]</color> Reset arena");
            ResetArena();
            return;
        }
    }

    /// <summary>Affiche le mapping touches ↔ configs en console.</summary>
    private void LogKeyBindings()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<color=#FFDD44>[CombatArena]</color> Touches arène :");
        sb.AppendLine("  0 → champs locaux (pas de config)");
        for (int i = 0; i < 9; i++)
        {
            if (i < availableConfigs.Count && availableConfigs[i] != null)
                sb.AppendLine($"  {i + 1} → '{availableConfigs[i].displayName}'");
            else
                sb.AppendLine($"  {i + 1} → (libre)");
        }
        sb.AppendLine("  R → reset avec config active");
        for (int i = 0; i < _stageKeys.Length; i++)
        {
            var stage = stagePalette != null ? stagePalette.Get(i) : null;
            if (stage != null)
                sb.AppendLine($"  F{i + 1} → stage '{stage.displayName}' ({stage.width}x{stage.height})");
        }
        sb.AppendLine("  F10 → arène plate (quitter le mode stage)");
        Debug.Log(sb.ToString());
    }

    /// <summary>Attend que HeroController et TurnManager soient disponibles.</summary>
    private IEnumerator WaitForDependencies()
    {
        float timeout = 5f;
        while (timeout > 0f)
        {
            if (HeroController.Instance != null && TurnManager.Instance != null)
                yield break;
            timeout -= Time.deltaTime;
            yield return null;
        }
        Debug.LogWarning("<color=#FF8800>[CombatArena]</color> Dependencies non trouvées après 5s " +
                         $"(Hero={HeroController.Instance != null}, TM={TurnManager.Instance != null})");
    }

    // =========================================================================
    // Construction
    // =========================================================================

    public void BuildArena()
    {
        // Mode stage : la grille vient du StageConfig
        if (activeStage != null)
        {
            if (BuildStage(activeStage)) return;

            Debug.LogError($"<color=#FF4444>[CombatArena]</color> Stage '{activeStage.displayName}' " +
                           "invalide — retour à l arène plate");
            activeStage = null;
        }

        // Si une config externe est assignee, appliquer ses valeurs
        if (activeConfig != null)
        {
            ApplyConfig(activeConfig);
            Debug.Log($"<color=#00FF88>[CombatArena]</color> Config appliquée : '{activeConfig.displayName}'");
        }

        Debug.Log($"<color=#00FF88>[CombatArena]</color> Build arena {gridWidth}x{gridHeight} " +
                  $"with {enemies.Count} enemies");

        // Calculer l offset pour centrer la grille sur (0,0) si demandé
        _gridOffset = centerGridOnZero
            ? new Vector2(-(gridWidth - 1) * cellStep * 0.5f, -(gridHeight - 1) * cellStep * 0.5f)
            : Vector2.zero;

        // 1. Grille logique
        arenaGrid.Build(gridWidth, gridHeight, cellStep, _gridOffset);

        // 2. Visuel (sprites de sol)
        BuildFloor();

        // 3. Marquer ennemis dans la grille (avant placement visuel)
        foreach (var e in enemies)
        {
            if (!arenaGrid.IsInBounds(e.position.x, e.position.y))
            {
                Debug.LogWarning($"[CombatArena] {e.enemyType} position ({e.position.x},{e.position.y}) hors limites");
                continue;
            }
            arenaGrid.MarkEnemyAt(e.position.x, e.position.y, e.enemyType);
        }

        // 4. Placer le héros
        PlaceHero();

        // 5. Spawn les ennemis (prefab + EnemyInstance.Initialize)
        foreach (var e in enemies)
        {
            if (!arenaGrid.IsInBounds(e.position.x, e.position.y)) continue;
            // Interdire sur la case du héros
            if (e.position == heroStartPos)
            {
                Debug.LogWarning($"[CombatArena] {e.enemyType} à position hero — skip");
                continue;
            }
            var ei = enemyPlacer.PlaceEnemy(e.enemyType, e.position.x, e.position.y, arenaGrid);
            if (ei != null) _spawnedEnemies.Add(ei);
        }

        // 6. Positionner la caméra pour englober toute la grille
        if (autoPositionCamera) PositionCamera();

        Debug.Log($"<color=#00FF88>[CombatArena]</color> Arena prête — " +
                  $"{_spawnedEnemies.Count} ennemi(s) actif(s)");
    }

    /// <summary>
    /// Copie les valeurs d une ArenaConfig vers les champs locaux.
    /// Appele automatiquement au BuildArena si activeConfig est assigne.
    /// </summary>
    public void ApplyConfig(ArenaConfig cfg)
    {
        if (cfg == null) return;

        gridWidth = cfg.gridWidth;
        gridHeight = cfg.gridHeight;
        cellStep = cfg.cellStep;
        cellVisualSize = cfg.cellVisualSize;
        floorColor = cfg.floorColor;
        centerGridOnZero = cfg.centerGridOnZero;
        heroStartPos = cfg.heroStartPos;

        // Convertir ArenaConfig.EnemySpawnEntry → CombatArena.EnemySpawnEntry
        enemies.Clear();
        if (cfg.enemies != null)
        {
            foreach (var e in cfg.enemies)
            {
                if (e == null) continue;
                enemies.Add(new EnemySpawnEntry
                {
                    enemyType = e.enemyType,
                    position = e.position
                });
            }
        }
    }

    /// <summary>Change la config active ET reset l arene avec.</summary>
    public void SwitchConfig(ArenaConfig cfg)
    {
        activeConfig = cfg;
        ResetArena();
    }

    // =========================================================================
    // Mode stage
    // =========================================================================

    /// <summary>
    /// Charge un stage (null = retour à l arène plate) et reset l arène.
    /// Un changement de stage revient toujours aux ennemis peints dans le stage ;
    /// les touches 1-9 appliquent ensuite une config d ennemis par-dessus.
    /// </summary>
    public void SwitchStage(StageConfig stage)
    {
        activeStage = stage;
        activeConfig = null;
        ResetArena();
    }

    /// <summary>Construit l arène depuis un StageConfig. False si le stage est invalide.</summary>
    private bool BuildStage(StageConfig stage)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        stage.Validate(errors, warnings);

        foreach (var w in warnings)
            Debug.LogWarning($"<color=#FFAA00>[Stage]</color> '{stage.displayName}' : {w}");
        if (errors.Count > 0)
        {
            foreach (var e in errors)
                Debug.LogError($"<color=#FF4444>[Stage]</color> '{stage.displayName}' : {e}");
            return false;
        }

        gridWidth = stage.width;
        gridHeight = stage.height;
        heroStartPos = stage.heroStart;

        float step = stagePixelSnap ? ArenaGrid.SnapStep(cellStep) : cellStep;
        _gridOffset = stageOriginAtZero
            ? Vector2.zero
            : new Vector2(-(gridWidth - 1) * step * 0.5f, -(gridHeight - 1) * step * 0.5f);

        // 1. Grille logique : terrain, cases cachées, contenus, chiffres
        arenaGrid.BuildFromStage(stage, step, _gridOffset);

        // 2. Config d ennemis par-dessus le stage (touches 1-9)
        if (activeConfig != null) ApplyEnemyConfigOnStage(activeConfig);

        // 3. Vues + ennemis déjà visibles (les ennemis cachés apparaîtront à la révélation)
        EnsureCellsParent();
        if (_stageRuntime == null) _stageRuntime = GetComponent<StageRuntime>();
        if (_stageRuntime == null) _stageRuntime = gameObject.AddComponent<StageRuntime>();
        _stageRuntime.Build(arenaGrid, cellsParent, enemyPlacer, cellVisualSize);

        // 4. Héros + caméra
        PlaceHero();
        if (autoPositionCamera) PositionCamera();

        Debug.Log($"<color=#00FF88>[CombatArena]</color> Stage prêt : '{stage.displayName}' " +
                  $"{gridWidth}x{gridHeight} — {_stageRuntime.SpawnedEnemyCount} ennemi(s) visible(s)" +
                  (activeConfig != null ? $" — ennemis de la config '{activeConfig.displayName}'" : "") +
                  "\n" + stage.ToAscii());
        return true;
    }

    /// <summary>
    /// Remplace les ennemis du stage par ceux d une ArenaConfig.
    /// La taille et le départ du héros restent ceux du stage.
    /// Un ennemi posé sur une case cachée sera caché ; sur un obstacle, il est ignoré.
    /// </summary>
    private void ApplyEnemyConfigOnStage(ArenaConfig cfg)
    {
        arenaGrid.ClearEnemies();

        int placed = 0, skipped = 0;
        if (cfg.enemies != null)
        {
            foreach (var e in cfg.enemies)
            {
                if (e == null) continue;

                bool onHero = e.position == heroStartPos;
                if (onHero || !arenaGrid.MarkEnemyAt(e.position.x, e.position.y, e.enemyType))
                {
                    skipped++;
                    Debug.LogWarning($"<color=#FFAA00>[Stage]</color> {e.enemyType} en " +
                                     $"({e.position.x},{e.position.y}) ignoré : " +
                                     (onHero ? "case du héros" : "hors grille ou obstacle"));
                    continue;
                }
                placed++;
            }
        }

        arenaGrid.RecomputeNumbers();
        Debug.Log($"<color=#00FF88>[CombatArena]</color> Config '{cfg.displayName}' appliquée sur le stage : " +
                  $"{placed} ennemi(s) posé(s), {skipped} ignoré(s)");
    }

    private void EnsureCellsParent()
    {
        if (cellsParent != null) return;
        var parentGO = new GameObject("Cells");
        parentGO.transform.SetParent(transform, false);
        cellsParent = parentGO.transform;
    }

    /// <summary>Instancie les ArenaCellView colorés pour chaque case.</summary>
    private void BuildFloor()
    {
        EnsureCellsParent();

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                var go = new GameObject($"ArenaCell_{x}_{y}");
                go.transform.SetParent(cellsParent, false);
                // Utiliser GridToWorld pour appliquer l offset
                go.transform.position = arenaGrid.GridToWorld(x, y, 0.1f);

                var view = go.AddComponent<ArenaCellView>();
                view.Setup(x, y, cellVisualSize, floorColor);

                _spawnedCells.Add(go);
            }
        }
    }

    /// <summary>Positionne la Main Camera pour que toute la grille soit visible.</summary>
    private void PositionCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[CombatArena] Camera.main null — impossible de la repositionner");
            return;
        }
        if (!cam.orthographic)
        {
            Debug.LogWarning("[CombatArena] Main Camera non-orthographique — laissee intacte");
            return;
        }

        // Pas réel de la grille (en mode stage il peut être arrondi au pixel)
        float step = arenaGrid != null && arenaGrid.IsBuilt ? arenaGrid.CellStep : cellStep;

        // Centre de la grille
        float centerX = (gridWidth - 1) * step * 0.5f + _gridOffset.x;
        float centerY = (gridHeight - 1) * step * 0.5f + _gridOffset.y;

        cam.transform.position = new Vector3(centerX, centerY, -10f);

        // Taille ortho pour englober grille + marge (largeur ou hauteur dominante)
        float halfH = (gridHeight * 0.5f + cameraMargin) * step;
        float halfW = (gridWidth * 0.5f + cameraMargin) * step / cam.aspect;
        cam.orthographicSize = Mathf.Max(halfH, halfW);

        Debug.Log($"<color=#00FF88>[CombatArena]</color> Camera positionnée à ({centerX:F2},{centerY:F2}) " +
                  $"size={cam.orthographicSize:F2}");
    }

    /// <summary>Téléporte le héros à la position de départ.</summary>
    private void PlaceHero()
    {
        var hero = HeroController.Instance;
        if (hero == null)
        {
            Debug.LogWarning("[CombatArena] HeroController.Instance null — impossible de placer le héros");
            return;
        }

        if (!arenaGrid.IsInBounds(heroStartPos.x, heroStartPos.y))
        {
            Debug.LogWarning($"[CombatArena] heroStartPos ({heroStartPos.x},{heroStartPos.y}) hors limites");
            heroStartPos = new Vector2Int(gridWidth / 2, 1);
        }

        var worldPos = arenaGrid.GridToWorld(heroStartPos.x, heroStartPos.y);
        hero.transform.position = worldPos;
        hero.SetGridPosition(heroStartPos);

        // v13 arena : forcer l apparition (dans le jeu normal c est OnFirstClick
        // qui fait HasAppeared=true + enable SpriteRenderer).
        var state = HeroState.Instance;
        if (state != null) state.HasAppeared = true;

        var sr = hero.GetComponent<SpriteRenderer>();
        if (sr != null) sr.enabled = true;

        hero.RestoreIdleSprite();

        Debug.Log($"<color=#00FF88>[CombatArena]</color> Hero placé à grid({heroStartPos.x},{heroStartPos.y}) " +
                  $"world({worldPos.x:F2},{worldPos.y:F2}) HasAppeared=true, SR enabled");
    }

    // =========================================================================
    // Reset (sans relancer Play)
    // =========================================================================

    [ContextMenu("Reset Arena")]
    public void ResetArena()
    {
        foreach (var ei in _spawnedEnemies)
            if (ei != null) Destroy(ei.gameObject);
        _spawnedEnemies.Clear();

        foreach (var cell in _spawnedCells)
            if (cell != null) Destroy(cell);
        _spawnedCells.Clear();

        // Vues et ennemis du stage précédent (sans effet en arène plate)
        if (_stageRuntime != null) _stageRuntime.Clear();

        arenaGrid?.Clear();

        BuildArena();
    }
}
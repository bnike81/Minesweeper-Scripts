using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ShepherdFarmSpawner - Place proceduralement la ferme du vieux berger
/// dans le niveau 1, et les moutons egares aux niveaux 1 et 2.
///
/// Logique de placement de la ferme :
///   - Cherche une zone libre d'au moins 5x5 cases dans la grille niveau 1
///   - Place la cabane (2x2 cases) en fond de la zone
///   - Dispose les accessoires (tonneau, paille x2) en arc devant la cabane
///   - Place le berger devant la cabane ou parmi ses affaires
///   - Place 1 mouton proche de la ferme dans le secteur du berger
///   - Jamais dans le coin superieur gauche (premier clic safe)
/// </summary>
public class ShepherdFarmSpawner : MonoBehaviour
{
    public static ShepherdFarmSpawner Instance { get; private set; }

    // -------------------------------------------------------------------------
    // Inspector - Prefabs
    // -------------------------------------------------------------------------

    [Header("=== Prefabs NPC ===")]
    [SerializeField] private GameObject _shepherdPrefab;
    [SerializeField] private GameObject _sheepPrefab;

    [Header("=== Prefabs Decors Ferme ===")]
    [SerializeField] private GameObject _cabinPrefab;      // 32x32 - occupe 2x2 cases
    [SerializeField] private GameObject _barrelPrefab;     // 16x16 - 1 case
    [SerializeField] private GameObject _haySmallPrefab;   // 16x16 - 1 case
    [SerializeField] private GameObject _hayBigPrefab;     // 24x24 - 1 case (deborde un peu)

    // =========================================================================
    // PLACEMENT DE LA FERME
    // =========================================================================

    [Header("--- Ferme : Zone de Recherche ---")]
    [Tooltip("Taille interne de la ferme en cases (cabane + accessoires)")]
    [SerializeField] private int _minFarmZoneSize = 4;

    [Tooltip("Marge minimale depuis le bord de la grille")]
    [SerializeField, Range(1, 5)] private int _edgeMargin = 2;

    [Tooltip("Rayon sans danger autour de la ferme. Recommande 2.5 a 3.5")]
    [SerializeField, Range(1f, 10f)] private float _farmProtectionRadius = 4.0f;

    [Header("--- Ferme : Probabilites de Zone ---")]
    [Tooltip("Chance que la ferme soit placee dans un COIN de la grille (loin du centre)")]
    [SerializeField, Range(0f, 1f)] private float _cornerBias = 0.45f;

    [Tooltip("Chance que la ferme soit sur un BORD (mais pas dans un coin)")]
    [SerializeField, Range(0f, 1f)] private float _edgeBias = 0.30f;
    // Reste = centre (1 - cornerBias - edgeBias)
    // Ex defaut : 45% coin + 30% bord + 25% centre

    [Tooltip("Taille de la zone consideree comme 'coin' (en % de la grille par cote)")]
    [SerializeField, Range(0.1f, 0.5f)] private float _cornerZoneRatio = 0.30f;

    [Tooltip("Taille de la zone consideree comme 'bord' (en % de la grille par cote)")]
    [SerializeField, Range(0.1f, 0.4f)] private float _edgeZoneRatio = 0.20f;

    // =========================================================================
    // PLACEMENT DES MOUTONS
    // =========================================================================

    [Header("--- Moutons : Distance depuis la Ferme ---")]
    [Tooltip("Distance MINIMUM des moutons depuis la ferme (cases Chebyshev)")]
    [SerializeField, Range(3, 12)] private int _sheepMinDistFromFarm = 7;

    [Tooltip("Distance MAXIMUM des moutons depuis la ferme (cases Chebyshev)")]
    [SerializeField, Range(6, 15)] private int _sheepMaxDistFromFarm = 12;

    [Tooltip("Distance MINIMUM entre deux moutons (evite qu'ils soient cote a cote)")]
    [SerializeField, Range(2, 8)] private int _sheepMinDistBetween = 4;

    [Header("--- Moutons : Zone de Securite ---")]
    [Tooltip("Rayon sans danger autour de chaque mouton. Recommande 1.2 a 2.0")]
    [SerializeField, Range(0.5f, 3f)] private float _sheepProtectionRadius = 1.5f;

    [Tooltip("Distance min d un ennemi autour d un mouton - empeche un voisin ennemi direct")]
    [SerializeField, Range(1, 4)] private int _sheepEnemySafeRadius = 2;

    [Tooltip("Nombre max de tentatives pour placer un mouton avant abandon")]
    [SerializeField, Range(30, 150)] private int _sheepPlacementMaxTries = 100;

    // =========================================================================
    // BERGER
    // =========================================================================

    [Header("--- Berger ---")]
    [Tooltip("Distance max du berger par rapport a la cabane")]
    [SerializeField, Range(1, 4)] private int _shepherdMaxDist = 2;

    // =========================================================================
    // TAILLE DES SPRITES
    // =========================================================================

    [Header("--- Tailles ---")]
    [SerializeField, Range(0.5f, 3f)] private float _cabinScale = 1.5f;
    [SerializeField, Range(0.2f, 1.5f)] private float _accessoryScale = 0.5f;
    [SerializeField, Range(0.2f, 2f)] private float _shepherdScale = 0.5f;
    [SerializeField, Range(0.2f, 2f)] private float _sheepScale = 0.5f;

    // =========================================================================
    // GRAINE
    // =========================================================================

    [Header("--- Graine ---")]
    [Tooltip("0 = aleatoire a chaque partie. Valeur fixe = meme ferme reproductible")]
    [SerializeField] private int _seed = 0;

    // -------------------------------------------------------------------------
    // Etat interne
    // -------------------------------------------------------------------------

    private System.Random _rng;
    private float _cellStep;
    private Vector2Int _farmOrigin;      // Case coin bas-gauche de la ferme
    private List<Vector2Int> _farmCells; // Toutes les cases occupees par la ferme
    private List<GameObject> _spawnedObjects = new();
    private List<Vector2Int> _allSheepPositions = new();

    /// <summary>Centre de la ferme pour que les moutons puissent y revenir</summary>
    public Vector2Int FarmOrigin => _farmOrigin;
    public Vector3 FarmWorldPos => new Vector3(
        (_farmOrigin.x + 1) * (_cellStep > 0 ? _cellStep : 1.05f),
        (_farmOrigin.y + 1) * (_cellStep > 0 ? _cellStep : 1.05f), 0f);

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Initialisation de secusite : si OnRunStarted arrive avant Awake
        // ou si l'ordre d'execution est imprevu, _rng est toujours valide
        _rng = new System.Random(System.Environment.TickCount);
        _farmCells = new List<Vector2Int>();
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnGridDataReady>(OnGridDataReady);
        EventBus.Subscribe<OnGridGenerated>(OnGridGenerated);
        EventBus.Subscribe<OnGridExtending>(OnGridExtending);
        EventBus.Subscribe<OnGridExtended>(OnGridExtended);
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnGridDataReady>(OnGridDataReady);
        EventBus.Unsubscribe<OnGridGenerated>(OnGridGenerated);
        EventBus.Unsubscribe<OnGridExtending>(OnGridExtending);
        EventBus.Unsubscribe<OnGridExtended>(OnGridExtended);
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
    }

    private void OnRunStarted(OnRunStarted evt)
    {
        // On reinitialise seulement la graine ici
        // ClearSpawned se fait dans OnGridGenerated pour eviter
        // que le clear arrive APRES le spawn (ordre des handlers)
        int seed = _seed != 0 ? _seed : UnityEngine.Random.Range(1, 99999);
        _rng = new System.Random(seed);
        _farmCells = new List<Vector2Int>();
    }

    /// <summary>
    /// Recu AVANT SpawnCellViews - on calcule la position de la ferme
    /// et on reserve la bulle dans GridManager (biome Prairie + exclusion dangers)
    /// </summary>
    private void OnGridDataReady(OnGridDataReady evt)
    {
        _cellStep = GridManager.Instance != null
            ? GridManager.Instance.CellStep : 1.05f;

        // Calculer la position de la ferme
        Vector2Int? origin = FindFarmZone(evt.Width, evt.Height);
        if (!origin.HasValue)
        {
            Debug.LogWarning("[ShepherdFarm] Impossible de trouver une zone de ferme !");
            _farmOrigin = new Vector2Int(evt.Width / 2 - 2, evt.Height / 2 - 2);
        }
        else
        {
            _farmOrigin = origin.Value;
        }

        // Centre de la ferme
        int cx = _farmOrigin.x + _minFarmZoneSize / 2;
        int cy = _farmOrigin.y + _minFarmZoneSize / 2;

        // Cercle de protection - exclut les dangers, garde la foret visuellement
        GridManager.Instance?.ReserveCircle(cx, cy, _farmProtectionRadius);

    }

    private void OnGridGenerated(OnGridGenerated evt)
    {
        // Clear en premier : detruit les objets de la run precedente
        ClearSpawned();
        _farmCells = new List<Vector2Int>();
        _allSheepPositions = new List<Vector2Int>();

        _cellStep = GridManager.Instance != null
            ? GridManager.Instance.CellStep : 1.05f;

        // _farmOrigin est deja calcule dans OnGridDataReady
        SpawnFarmAtOrigin(evt.Width, evt.Height);
    }

    private void OnGridExtending(OnGridExtending evt)
    {
        // Spawner les moutons niv2 dans la NOUVELLE zone avant qu elle soit generee
        // OnGridExtending donne OldHeight et NewHeight - on place dans cette zone
        if (evt.Level != 2) return; // Seulement au niveau 2


        _cellStep = GridManager.Instance?.CellStep ?? _cellStep;
        SpawnSheepInZone(
            QuestManager.Instance?.SheepOnLevel2 ?? 2,
            evt.Width,
            evt.NewHeight,
            evt.OldHeight,          // yMin = debut de la nouvelle zone
            QuestManager.Instance?.SheepOnLevel1 ?? 2);
    }

    private void OnGridExtended(OnGridExtended evt)
    {
        // Ne plus spawner ici - gere dans OnGridExtending
    }

    // -------------------------------------------------------------------------
    // Spawn de la ferme (niveau 1)
    // -------------------------------------------------------------------------

    private void SpawnFarmAtOrigin(int gridWidth, int gridHeight)
    {
        // _farmOrigin est deja calcule et reserve dans OnGridDataReady

        // 2. Placer la cabane (2x2 cases, en haut de la zone)
        Vector2Int cabinPos = new Vector2Int(
            _farmOrigin.x + 1,
            _farmOrigin.y + 2);
        SpawnDecorWithOrder(_cabinPrefab, cabinPos, "Cabane", orderInLayer: 4);
        _farmCells.Add(cabinPos);
        _farmCells.Add(cabinPos + Vector2Int.right);

        // ── Porte de la cabane ────────────────────────────────────────────────
        // La "porte" est la case devant la cabane (cabinPos.y - 1 = juste en dessous).
        // On ajoute un BuildingDoorTrigger sur le sprite de la cabane ET
        // on enregistre la position dans IndoorManager.
        Vector2Int doorPos = new Vector2Int(cabinPos.x, cabinPos.y - 1);
        AttachCabinDoor(cabinPos, doorPos);

        // 3. Disposer les accessoires en arc devant la cabane
        SpawnFarmAccessories(cabinPos);

        // 4. Placer le berger devant la cabane
        Vector2Int shepherdPos = GetShepherdPosition(cabinPos);
        SpawnShepherd(shepherdPos);

        // 5. Placer 1-2 moutons proches de la ferme (niveau 1)
        int sheepOnL1 = QuestManager.Instance != null
            ? QuestManager.Instance.SheepOnLevel1 : 2;
        SpawnSheepNearFarm(sheepOnL1, gridWidth, gridHeight, 0);

    }

    // -------------------------------------------------------------------------
    // Accessoires en arc
    // -------------------------------------------------------------------------

    private void SpawnFarmAccessories(Vector2Int cabinPos)
    {
        // Cabane occupe (cabinPos.x, cabinPos.y) et (cabinPos.x+1, cabinPos.y)
        //
        // REGLES :
        //   Tonneau  : partout SAUF cases laterales adjacentes (X-1,Y) et (X+2,Y)
        //   Paille   : partout
        //   Devant   (Y-1, Y-2)          -> order 6 (par dessus cabane)
        //   Lateral  (X-1 ou X+2, meme Y): order 3 (derriere cabane)
        //   Derriere (Y+1)               : order 3 (derriere cabane)

        // Tous les emplacements possibles avec leur order
        // (offset depuis cabinPos, orderInLayer)
        // Carte complete :
        //   Y+1                          : interdit (toit)
        //   Y=0  (-1,0) et (2,0)         : PAILLE UNIQUEMENT  order 3
        //   Y=0  (-2,0) et (3,0)         : accessoires         order 3
        //   Y-1  (-1,-1) et (2,-1)       : accessoires         order 6
        //   Y-1  (0,-1) et (1,-1)        : fermier uniquement
        //   Y-2  (-1,-2)(0,-2)(1,-2)     : accessoires ou fermier  order 6
        // Zone stricte :
        // Y=0  : X-1 et X+1 = paille uniquement / X-2 et X+2 = accessoires
        // Y-1  : X-1, X0, X+1
        // Y-2  : X-1, X0, X+1
        var allSlots = new List<(Vector2Int offset, int order, bool hayOnly)>
        {
            // Y=0 directement adjacent cabane = paille uniquement
            (new Vector2Int(-1,  0), 3, true),   // X-1 Y=0
            (new Vector2Int( 2,  0), 3, true),   // X+1 Y=0
            // Y=0 plus loin = accessoires
            (new Vector2Int(-2,  0), 3, false),  // X-2 Y=0
            (new Vector2Int( 3,  0), 3, false),  // X+2 Y=0
            // Y-1 : X-1 et X+1 seulement (X0 = porte, fermier uniquement)
            (new Vector2Int(-1, -1), 6, false),  // X-1 Y-1
            (new Vector2Int( 1, -1), 6, false),  // X+1 Y-1
            // Y-2 trois cases
            (new Vector2Int(-1, -2), 6, false),  // X-1 Y-2
            (new Vector2Int( 0, -2), 6, false),  // X0  Y-2
            (new Vector2Int( 1, -2), 6, false),  // X+1 Y-2
        };
        ShuffleList(allSlots);

        var gm = GridManager.Instance;

        // Slots valides = libres, dans la grille, avec flag hayOnly
        var validSlots = new List<(Vector2Int pos, int order, bool hayOnly)>();
        foreach (var (off, ord, hayOnly) in allSlots)
        {
            var pos = cabinPos + off;
            if (_farmCells.Contains(pos)) continue;
            if (gm != null && !IsValidCell(pos, gm.Width, gm.Height)) continue;
            validSlots.Add((pos, ord, hayOnly));
        }

        // 1. Paille grande -> slot hayOnly en priorite, sinon n importe ou
        bool hayBigPlaced = false;
        foreach (var (pos, ord, hayOnly) in validSlots)
        {
            if (!hayOnly) continue; // priorite aux slots paille uniquement
            SpawnDecorWithOrder(_hayBigPrefab, pos, "PailleGrande", ord);
            _farmCells.Add(pos);
            validSlots.RemoveAll(s => s.pos == pos);
            hayBigPlaced = true;
            break;
        }
        if (!hayBigPlaced)
        {
            foreach (var (pos, ord, _) in validSlots)
            {
                SpawnDecorWithOrder(_hayBigPrefab, pos, "PailleGrande", ord);
                _farmCells.Add(pos);
                validSlots.RemoveAll(s => s.pos == pos);
                break;
            }
        }

        // 2. Paille petite -> slot hayOnly restant en priorite
        bool haySmallPlaced = false;
        foreach (var (pos, ord, hayOnly) in validSlots)
        {
            if (!hayOnly) continue;
            SpawnDecorWithOrder(_haySmallPrefab, pos, "PailleSmall", ord);
            _farmCells.Add(pos);
            validSlots.RemoveAll(s => s.pos == pos);
            haySmallPlaced = true;
            break;
        }
        if (!haySmallPlaced)
        {
            foreach (var (pos, ord, _) in validSlots)
            {
                SpawnDecorWithOrder(_haySmallPrefab, pos, "PailleSmall", ord);
                _farmCells.Add(pos);
                validSlots.RemoveAll(s => s.pos == pos);
                break;
            }
        }

        // 3. Tonneau -> slot non-hayOnly uniquement (jamais adjacent cabane)
        foreach (var (pos, ord, hayOnly) in validSlots)
        {
            if (hayOnly) continue;
            SpawnDecorWithOrder(_barrelPrefab, pos, "Tonneau", ord);
            _farmCells.Add(pos);
            break;
        }
    }

    // -------------------------------------------------------------------------
    // Position du berger
    // -------------------------------------------------------------------------

    private Vector2Int GetShepherdPosition(Vector2Int cabinPos)
    {
        // Regles :
        // - Devant la porte (0,-1) et (1,-1) : berger OK
        // - Loin devant (0,-2) (1,-2)        : berger OK
        // - Lateral (-1,0) (2,0)             : berger OK
        // - Derriere (Y+1) et au dessus      : personne
        // Fermier sur Y-1 (X-1, X0, X+1) et Y-2 (X-1, X0, X+1)
        // Zone stricte de la ferme uniquement
        var candidates = new List<Vector2Int>
        {
            cabinPos + new Vector2Int(-1, -1),  // X-1 Y-1
            cabinPos + new Vector2Int( 0, -1),  // X0  Y-1
            cabinPos + new Vector2Int( 1, -1),  // X+1 Y-1
            cabinPos + new Vector2Int(-1, -2),  // X-1 Y-2
            cabinPos + new Vector2Int( 0, -2),  // X0  Y-2
            cabinPos + new Vector2Int( 1, -2),  // X+1 Y-2
        };
        ShuffleList(candidates);

        foreach (var pos in candidates)
        {
            if (!_farmCells.Contains(pos) && IsValidCell(pos,
                GridManager.Instance.Width, GridManager.Instance.Height))
            {
                _farmCells.Add(pos);
                return pos;
            }
        }

        // Fallback : juste en dessous de la cabane
        return cabinPos + new Vector2Int(0, -1);
    }

    // -------------------------------------------------------------------------
    // Spawn berger et moutons
    // -------------------------------------------------------------------------

    private void SpawnShepherd(Vector2Int gridPos)
    {
        if (_shepherdPrefab == null) return;

        Vector3 worldPos = GridToWorld(gridPos);
        var go = Instantiate(_shepherdPrefab, worldPos, Quaternion.identity, null);
        go.name = "Shepherd";
        go.transform.localScale = Vector3.one * _shepherdScale;

        var npc = go.GetComponent<NPC>();
        npc?.Initialize(NPC.NPCType.Shepherd);

        EnsureCollider(go);

        var wo = go.GetComponent<WorldObject>() ?? go.AddComponent<WorldObject>();
        wo.Initialize(gridPos.x, gridPos.y);

        _spawnedObjects.Add(go);
    }

    private void SpawnSheepNearFarm(int count, int gridWidth, int gridHeight, int indexOffset)
    {
        if (_sheepPrefab == null) return;

        int placed = 0;
        int attempts = 0;
        int maxDist = Mathf.Min(_sheepMaxDistFromFarm,
            Mathf.Min(gridWidth, gridHeight) - _edgeMargin);

        // Liste des positions deja placees pour eviter les moutons trop proches
        var placedPositions = new List<Vector2Int>();

        while (placed < count && attempts < _sheepPlacementMaxTries)
        {
            attempts++;

            int rx = _rng.Next(_edgeMargin, gridWidth - _edgeMargin);
            int ry = _rng.Next(_edgeMargin, gridHeight - _edgeMargin);
            var pos = new Vector2Int(rx, ry);

            // 1. Distance depuis la ferme
            int distFarm = Mathf.Max(
                Mathf.Abs(pos.x - _farmOrigin.x),
                Mathf.Abs(pos.y - _farmOrigin.y));
            if (distFarm < _sheepMinDistFromFarm || distFarm > maxDist) continue;

            // 2. Pas trop pres d'un autre mouton (niveau 1 ET niveau 2 confondus)
            bool tooCloseSheep = false;
            foreach (var other in _allSheepPositions)
            {
                int d = Mathf.Max(Mathf.Abs(pos.x - other.x), Mathf.Abs(pos.y - other.y));
                if (d < _sheepMinDistBetween) { tooCloseSheep = true; break; }
            }
            if (tooCloseSheep) continue;

            if (!IsValidCell(pos, gridWidth, gridHeight, true)) continue;
            if (_farmCells.Contains(pos)) continue;

            // 3. Pas trop pres d un ennemi (verifie les cases voisines de la grille)
            bool tooCloseEnemy = false;
            var gm = GridManager.Instance;
            if (gm != null)
            {
                for (int ex = -_sheepEnemySafeRadius; ex <= _sheepEnemySafeRadius && !tooCloseEnemy; ex++)
                    for (int ey = -_sheepEnemySafeRadius; ey <= _sheepEnemySafeRadius && !tooCloseEnemy; ey++)
                    {
                        var neighbor = new Vector2Int(pos.x + ex, pos.y + ey);
                        if (!MinesweeperLogic.IsInBounds(neighbor.x, neighbor.y, gridWidth, gridHeight)) continue;
                        var neighborCell = gm.GetCell(neighbor.x, neighbor.y);
                        if (neighborCell != null && neighborCell.IsEnemy) tooCloseEnemy = true;
                    }
            }
            if (tooCloseEnemy) continue;

            Vector3 worldPos = GridToWorld(pos);
            var go = Instantiate(_sheepPrefab, worldPos, Quaternion.identity, null);
            go.name = "Sheep_" + (placed + indexOffset);
            go.transform.localScale = Vector3.one * _sheepScale;

            var npc = go.GetComponent<NPC>();
            npc?.Initialize(NPC.NPCType.Sheep, placed + indexOffset);

            EnsureCollider(go);

            var wo = go.GetComponent<WorldObject>() ?? go.AddComponent<WorldObject>();
            wo.Initialize(pos.x, pos.y);

            GridManager.Instance?.ReserveCircle(pos.x, pos.y, _sheepProtectionRadius);

            _spawnedObjects.Add(go);
            _farmCells.Add(pos);
            placedPositions.Add(pos);
            _allSheepPositions.Add(pos);
            placed++;
        }

        if (placed < count)
            Debug.LogWarning("[ShepherdFarm] Seulement " + placed + "/" + count
                + " moutons places apres " + attempts + " tentatives."
                + " Augmente SheepPlacementMaxTries ou reduis les contraintes.");
    }

    private void SpawnLevel2Sheep(int gridWidth, int gridHeight)
    {
        if (QuestManager.Instance == null)
        {
            Debug.LogError("[ShepherdFarm] QuestManager null - moutons niv2 annules");
            return;
        }

        int count = QuestManager.Instance.SheepOnLevel2;
        int indexOffset = QuestManager.Instance.SheepOnLevel1;


        if (count <= 0)
        {
            Debug.LogWarning("[ShepherdFarm] count <= 0, aucun mouton niv2 a spawner");
            return;
        }

        var gm = GridManager.Instance;
        if (gm == null)
        {
            Debug.LogError("[ShepherdFarm] GridManager null");
            return;
        }

        // Utiliser toute la grille pour maximiser les chances
        int ext = GameManager.Instance?.GridHeightExtensionPerLevel ?? 16;
        int yMin = Mathf.Max(0, gridHeight - ext);
        if (gridHeight - yMin - _edgeMargin * 2 < 3) yMin = 0;


        SpawnSheepInZone(count, gridWidth, gridHeight, yMin, indexOffset);
    }

    private void SpawnSheepInZone(int count, int gridWidth, int gridHeight,
        int yMin, int indexOffset)
    {
        if (_sheepPrefab == null)
        {
            Debug.LogError("[ShepherdFarm] _sheepPrefab null !");
            return;
        }

        int ryMin = Mathf.Max(_edgeMargin, yMin);
        int ryMax = gridHeight - _edgeMargin;
        if (ryMin >= ryMax) { ryMin = _edgeMargin; ryMax = gridHeight - _edgeMargin; }

        int placed = 0;
        int attempts = 0;
        // Essais progressifs : d abord avec toutes les contraintes
        // puis on relache progressivement si echec
        int maxTries = _sheepPlacementMaxTries * 3;

        while (placed < count && attempts < maxTries)
        {
            attempts++;

            int rx = _rng.Next(_edgeMargin, gridWidth - _edgeMargin);
            int ry = _rng.Next(ryMin, ryMax);
            var pos = new Vector2Int(rx, ry);

            if (!IsValidCell(pos, gridWidth, gridHeight, true)) continue;
            if (_farmCells.Contains(pos)) continue;

            // Distance minimum entre moutons
            // On relache cette contrainte apres la moitie des essais
            bool checkDist = attempts < maxTries / 2;
            if (checkDist)
            {
                bool tooClose = false;
                foreach (var other in _allSheepPositions)
                {
                    int d = Mathf.Max(Mathf.Abs(pos.x - other.x),
                                      Mathf.Abs(pos.y - other.y));
                    if (d < _sheepMinDistBetween) { tooClose = true; break; }
                }
                if (tooClose) continue;
            }

            // Enemy safe radius - toujours respecte
            bool nearEnemy = false;
            var gm2 = GridManager.Instance;
            if (gm2 != null)
            {
                for (int ex = -_sheepEnemySafeRadius; ex <= _sheepEnemySafeRadius && !nearEnemy; ex++)
                    for (int ey = -_sheepEnemySafeRadius; ey <= _sheepEnemySafeRadius && !nearEnemy; ey++)
                    {
                        var n = new Vector2Int(pos.x + ex, pos.y + ey);
                        if (!MinesweeperLogic.IsInBounds(n.x, n.y, gridWidth, gridHeight)) continue;
                        var nc = gm2.GetCell(n.x, n.y);
                        if (nc != null && nc.IsEnemy) nearEnemy = true;
                    }
            }
            if (nearEnemy) continue;

            // Placer le mouton
            Vector3 worldPos = GridToWorld(pos);
            var go = Instantiate(_sheepPrefab, worldPos, Quaternion.identity, null);
            go.name = "Sheep_" + (placed + indexOffset);
            go.transform.localScale = Vector3.one * _sheepScale;

            var npc = go.GetComponent<NPC>();
            npc?.Initialize(NPC.NPCType.Sheep, placed + indexOffset);

            EnsureCollider(go);

            var wo = go.GetComponent<WorldObject>() ?? go.AddComponent<WorldObject>();
            wo.Initialize(pos.x, pos.y);

            GridManager.Instance?.ReserveCircle(pos.x, pos.y, _sheepProtectionRadius);

            _spawnedObjects.Add(go);
            _farmCells.Add(pos);
            _allSheepPositions.Add(pos);
            placed++;
        }

        if (placed < count)
            Debug.LogWarning("[ShepherdFarm] Seulement " + placed + "/" + count
                + " moutons places apres " + attempts + " tentatives.");
    }


    // -------------------------------------------------------------------------
    // Recherche de zone libre pour la ferme
    // -------------------------------------------------------------------------

    /// <summary>
    /// Choisit une position pour la ferme selon les biais Inspector :
    ///   cornerBias = probabilite d'atterrir dans un coin
    ///   edgeBias   = probabilite d'atterrir sur un bord
    ///   reste      = centre de la grille
    /// </summary>
    private Vector2Int? FindFarmZone(int gridWidth, int gridHeight)
    {
        int minX = _edgeMargin;
        int maxX = gridWidth - _edgeMargin - _minFarmZoneSize;
        int minY = _edgeMargin;
        int maxY = gridHeight - _edgeMargin - _minFarmZoneSize;

        if (maxX <= minX || maxY <= minY)
            return new Vector2Int(gridWidth / 2 - 2, gridHeight / 4);

        // Tailles des zones en cases
        int cornerW = Mathf.Max(1, Mathf.RoundToInt((maxX - minX) * _cornerZoneRatio));
        int cornerH = Mathf.Max(1, Mathf.RoundToInt((maxY - minY) * _cornerZoneRatio));
        int edgeW = Mathf.Max(1, Mathf.RoundToInt((maxX - minX) * _edgeZoneRatio));
        int edgeH = Mathf.Max(1, Mathf.RoundToInt((maxY - minY) * _edgeZoneRatio));

        // Tirage de la zone selon les probabilites
        float roll = (float)_rng.NextDouble();
        float total = _cornerBias + _edgeBias;
        // Clamp pour eviter que corner + edge > 1
        float cornerP = _cornerBias;
        float edgeP = Mathf.Min(_edgeBias, 1f - cornerP);

        int x, y;
        string zone;

        if (roll < cornerP)
        {
            // COIN : choisir un des 4 coins aleatoirement
            zone = "COIN";
            int corner = _rng.Next(4);
            x = (corner == 0 || corner == 2)
                ? _rng.Next(minX, minX + cornerW)
                : _rng.Next(maxX - cornerW, maxX);
            y = (corner == 0 || corner == 1)
                ? _rng.Next(minY, minY + cornerH)
                : _rng.Next(maxY - cornerH, maxY);
        }
        else if (roll < cornerP + edgeP)
        {
            // BORD : choisir un des 4 bords aleatoirement
            zone = "BORD";
            int side = _rng.Next(4);
            if (side == 0) { x = _rng.Next(minX, maxX); y = _rng.Next(minY, minY + edgeH); }
            else if (side == 1) { x = _rng.Next(minX, maxX); y = _rng.Next(maxY - edgeH, maxY); }
            else if (side == 2) { x = _rng.Next(minX, minX + edgeW); y = _rng.Next(minY, maxY); }
            else { x = _rng.Next(maxX - edgeW, maxX); y = _rng.Next(minY, maxY); }
        }
        else
        {
            // CENTRE : zone centrale excentree des bords
            zone = "CENTRE";
            x = _rng.Next(minX + cornerW, maxX - cornerW);
            y = _rng.Next(minY + cornerH, maxY - cornerH);
        }

        // Clamp securite
        x = Mathf.Clamp(x, minX, maxX);
        y = Mathf.Clamp(y, minY, maxY);


        return new Vector2Int(x, y);
    }

    /// <summary>
    /// Verifie que la zone pour la ferme est libre :
    /// - Dans les limites de la grille
    /// - Pas d'arbres (cases foret cachees) dans le rayon buffer
    /// Cela garantit que la ferme est dans un espace ouvert et paisible.
    /// Note : les dangers (ennemis) sont places apres le 1er clic,
    /// donc on verifie seulement le biome pour l'instant.
    /// </summary>
    private bool IsZoneClearForFarm(
        Vector2Int origin, int farmSize, int buffer, int gridWidth, int gridHeight)
    {
        int checkSize = farmSize + buffer * 2;
        int startX = origin.x - buffer;
        int startY = origin.y - buffer;

        for (int dx = 0; dx < checkSize; dx++)
        {
            for (int dy = 0; dy < checkSize; dy++)
            {
                int nx = startX + dx;
                int ny = startY + dy;

                // Hors grille = invalide
                if (!MinesweeperLogic.IsInBounds(nx, ny, gridWidth, gridHeight))
                    return false;

                // On verifie le biome de la case : on veut eviter les forets denses
                // Les cases foret non revelees = arbres = on les evite dans le buffer
                var cell = GridManager.Instance?.GetCell(nx, ny);
                if (cell == null) continue;

                // Dans la zone coeur de la ferme (pas le buffer) : aucun arbre
                bool inFarmCore = (dx >= buffer && dx < buffer + farmSize &&
                                   dy >= buffer && dy < buffer + farmSize);
                if (inFarmCore && cell.Biome == BiomeType.Forest)
                    return false;
            }
        }
        return true;
    }

    // -------------------------------------------------------------------------
    // Spawn decor generique
    // -------------------------------------------------------------------------

    private void SpawnDecor(GameObject prefab, Vector2Int gridPos, string label)
    {
        SpawnDecorWithOrder(prefab, gridPos, label, orderInLayer: 5);
    }

    private void SpawnDecorWithOrder(
        GameObject prefab, Vector2Int gridPos, string label, int orderInLayer)
    {
        if (prefab == null)
        {
            Debug.LogWarning("[ShepherdFarm] Prefab manquant pour : " + label);
            return;
        }

        Vector3 worldPos = GridToWorld(gridPos);
        var go = Instantiate(prefab, worldPos, Quaternion.identity, null);
        go.name = label;

        // Scale selon le type
        if (label == "Cabane")
            go.transform.localScale = Vector3.one * _cabinScale;
        else
            go.transform.localScale = Vector3.one * _accessoryScale;

        // Appliquer le Sorting Layer et Order
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.sortingLayerName = "CellContent";
            sr.sortingOrder = orderInLayer;
        }
        // Aussi sur les enfants si sprite sur enfant
        foreach (var childSr in go.GetComponentsInChildren<SpriteRenderer>())
        {
            childSr.sortingLayerName = "CellContent";
            childSr.sortingOrder = orderInLayer;
        }

        var wo = go.GetComponent<WorldObject>() ?? go.AddComponent<WorldObject>();
        wo.Initialize(gridPos.x, gridPos.y);

        _spawnedObjects.Add(go);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Vector3 GridToWorld(Vector2Int gridPos)
    {
        float step = _cellStep > 0 ? _cellStep : 1.05f;
        return new Vector3(gridPos.x * step, gridPos.y * step, 0f);
    }

    private bool IsValidCell(Vector2Int pos, int gridWidth, int gridHeight,
        bool checkReserved = false)
    {
        if (!MinesweeperLogic.IsInBounds(pos.x, pos.y, gridWidth, gridHeight)) return false;
        // checkReserved uniquement pour les moutons (pas pour les accessoires de la ferme)
        if (checkReserved && GridManager.Instance != null
            && GridManager.Instance.IsCellReserved(pos.x, pos.y)) return false;
        return true;
    }

    private void EnsureCollider(GameObject go)
    {
        if (go.GetComponent<Collider2D>() == null)
        {
            var col = go.AddComponent<BoxCollider2D>();
            col.size = Vector2.one * 0.9f;
        }
    }

    private void ShuffleList<T>(List<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = _rng.Next(n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
    }

    private void ClearSpawned()
    {
        foreach (var go in _spawnedObjects)
        {
            if (go == null) continue;
            // Ne pas detruire les moutons qui rejoignent la ferme
            var sheep = go.GetComponent<SheepBehaviour>();
            if (sheep != null && sheep.IsReturningToFarm) continue;
            Destroy(go);
        }
        // Garder les moutons en retour dans la liste
        _spawnedObjects.RemoveAll(go =>
            go == null ||
            go.GetComponent<SheepBehaviour>() == null ||
            !go.GetComponent<SheepBehaviour>().IsReturningToFarm);
    }

    // ── Porte de la cabane ────────────────────────────────────────────────────

    /// <summary>
    /// Cherche le GO "Cabane" dans les objets spawnés, lui ajoute un
    /// BuildingDoorTrigger et BoxCollider2D, et enregistre dans IndoorManager.
    /// </summary>
    private void AttachCabinDoor(Vector2Int cabinPos, Vector2Int doorPos)
    {
        // Retrouver le GO de la cabane
        GameObject cabinGO = null;
        foreach (var go in _spawnedObjects)
        {
            if (go != null && go.name == "Cabane") { cabinGO = go; break; }
        }

        if (cabinGO == null)
        {
            Debug.LogWarning("[ShepherdFarm] GO Cabane introuvable pour attacher la porte.");
            return;
        }

        // Ajouter collider si absent
        if (cabinGO.GetComponent<BoxCollider2D>() == null)
            cabinGO.AddComponent<BoxCollider2D>();

        // Ajouter le trigger de porte
        if (cabinGO.GetComponent<BuildingDoorTrigger>() == null)
        {
            var trigger = cabinGO.AddComponent<BuildingDoorTrigger>();
            trigger.Initialize(doorPos.x, doorPos.y, "Cabane du Berger");
        }

        // Enregistrer dans IndoorManager
        IndoorManager.Instance?.RegisterBuilding(doorPos.x, doorPos.y, "Cabane du Berger");

        Debug.Log($"[ShepherdFarm] Porte cabane attachée → doorPos=({doorPos.x},{doorPos.y})");
    }
}
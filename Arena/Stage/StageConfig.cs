using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>Une case peinte d un stage.</summary>
[System.Serializable]
public struct StageCell
{
    public BiomeType biome;       // Forest, Beach, Cave
    public TerrainType terrain;   // Sol, Eau, Montagne, MurGrotte
    public bool hidden;           // true = arbre / palmier / fog de grotte
    public CellContent content;   // Empty, ennemi ou piege
}

/// <summary>
/// StageConfig — un stage de combat peint case par case.
///
/// Chantier Stage Arena, etape 1. Remplace ArenaConfig pour tout ce qui est
/// terrain. Une ArenaConfig (liste d ennemis) peut toujours etre appliquee
/// par-dessus un stage avec les touches 1 a 9.
///
/// REGLE CENTRALE : la visibilite d un ennemi decoule de sa case.
///   case cachee  + ennemi → ennemi invisible, apparait a la revelation
///   case revelee + ennemi → ennemi visible des le depart
///
/// DEUX FACONS DE REMPLIR UN STAGE :
///   1. L editeur de stage (etape 4, a venir).
///   2. Le texte ASCII ci-dessous, puis menu ⋮ de l Inspector →
///      "Importer le texte ASCII".
///
/// LEGENDE ASCII (la premiere ligne du texte = le HAUT de la grille) :
///   .   sol revele (plaine)
///   T   case cachee vide (arbre, palmier ou fog selon le biome). '?' accepte aussi.
///   ~   eau            #   mur de grotte            ^   montagne
///   @   depart du heros (un seul)
///   lettre minuscule = visible, MAJUSCULE = cache sous un arbre :
///     w loup        b ours           m mercenaire     s bandit epee
///     a archer      c crabe          x araignee       v chauve-souris
///     l gobelin lance   g gobelin masse   z boss      p piege
/// </summary>
[CreateAssetMenu(menuName = "Arena/Stage Config", fileName = "StageConfig")]
public class StageConfig : ScriptableObject
{
    public const int MinSize = 5;
    public const int MaxSize = 30;
    public const int CurrentVersion = 1;

    [Header("Metadata")]
    public string displayName = "Stage sans nom";
    [TextArea(2, 4)]
    public string notes = "";

    [Header("Grille")]
    [Tooltip("Largeur en cases. Modifiable : le contenu existant est conserve (ancrage bas-gauche).")]
    public int width = 10;
    [Tooltip("Hauteur en cases.")]
    public int height = 10;
    [Tooltip("Biome des nouvelles cases et de l import ASCII.")]
    public BiomeType defaultBiome = BiomeType.Forest;

    [Header("Heros")]
    public Vector2Int heroStart = new Vector2Int(5, 1);

    [Header("Import / export texte (voir la legende en tete du script)")]
    [TextArea(6, 32)]
    public string asciiSource = "";

    [SerializeField, HideInInspector] private int _version = CurrentVersion;

    // Tableau a plat : index = x + y * largeur. Unity ne serialise pas les tableaux 2D.
    [SerializeField, HideInInspector] private StageCell[] _cells;
    // Dimensions avec lesquelles _cells a ete construit (pour re-ajuster si width/height changent).
    [SerializeField, HideInInspector] private int _cellsWidth;
    [SerializeField, HideInInspector] private int _cellsHeight;

    public int Version => _version;

    // =========================================================================
    // Acces aux cases
    // =========================================================================

    public bool IsInside(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;

    public StageCell Get(int x, int y)
    {
        EnsureCells();
        if (!IsInside(x, y)) return DefaultCell();
        return _cells[x + y * width];
    }

    public void Set(int x, int y, StageCell cell)
    {
        EnsureCells();
        if (!IsInside(x, y)) return;
        _cells[x + y * width] = cell;
    }

    /// <summary>Change la taille en conservant le contenu existant (ancrage bas-gauche).</summary>
    public void Resize(int newWidth, int newHeight)
    {
        width = Mathf.Clamp(newWidth, MinSize, MaxSize);
        height = Mathf.Clamp(newHeight, MinSize, MaxSize);
        EnsureCells();
        heroStart.x = Mathf.Clamp(heroStart.x, 0, width - 1);
        heroStart.y = Mathf.Clamp(heroStart.y, 0, height - 1);
    }

    /// <summary>
    /// Garantit que le tableau de cases correspond a width x height.
    /// Si la taille a change (Inspector), les cases communes sont conservees.
    /// </summary>
    public void EnsureCells()
    {
        width = Mathf.Clamp(width, MinSize, MaxSize);
        height = Mathf.Clamp(height, MinSize, MaxSize);

        bool valid = _cells != null
                  && _cellsWidth == width && _cellsHeight == height
                  && _cells.Length == width * height;
        if (valid) return;

        var fresh = new StageCell[width * height];
        var def = DefaultCell();
        for (int i = 0; i < fresh.Length; i++) fresh[i] = def;

        bool oldUsable = _cells != null && _cellsWidth > 0 && _cellsHeight > 0
                      && _cells.Length == _cellsWidth * _cellsHeight;
        if (oldUsable)
        {
            int copyW = Mathf.Min(width, _cellsWidth);
            int copyH = Mathf.Min(height, _cellsHeight);
            for (int x = 0; x < copyW; x++)
                for (int y = 0; y < copyH; y++)
                    fresh[x + y * width] = _cells[x + y * _cellsWidth];
        }

        _cells = fresh;
        _cellsWidth = width;
        _cellsHeight = height;
    }

    private StageCell DefaultCell()
    {
        return new StageCell
        {
            biome = defaultBiome,
            terrain = TerrainType.Sol,
            hidden = false,
            content = CellContent.Empty
        };
    }

    private void OnValidate()
    {
        EnsureCells();
        heroStart.x = Mathf.Clamp(heroStart.x, 0, width - 1);
        heroStart.y = Mathf.Clamp(heroStart.y, 0, height - 1);
    }

    // =========================================================================
    // Validation — partagee par le runtime (CombatArena) et le futur editeur
    // =========================================================================

    public static bool IsEnemyContent(CellContent c)
    {
        int v = (int)c;
        return (v >= 10 && v < 20) || c == CellContent.Enemy_Boss;
    }

    public static bool IsSupportedContent(CellContent c)
        => c == CellContent.Empty || c == CellContent.Number
        || c == CellContent.Trap || IsEnemyContent(c);

    public int CountEnemies()
    {
        EnsureCells();
        int n = 0;
        for (int i = 0; i < _cells.Length; i++)
            if (!TerrainRules.IsObstacle(_cells[i].terrain) && IsEnemyContent(_cells[i].content)) n++;
        return n;
    }

    /// <summary>
    /// Verifie le stage. errors = le stage ne peut pas etre charge.
    /// warnings = charge quand meme, avec correction automatique.
    /// </summary>
    public void Validate(List<string> errors, List<string> warnings)
    {
        EnsureCells();

        if (!IsInside(heroStart.x, heroStart.y))
        {
            errors.Add("Depart du heros (" + heroStart.x + "," + heroStart.y + ") hors de la grille.");
        }
        else
        {
            var hc = Get(heroStart.x, heroStart.y);
            if (TerrainRules.IsObstacle(hc.terrain))
                errors.Add("Depart du heros sur un obstacle (" + hc.terrain + ").");
            else if (hc.content == CellContent.Trap || IsEnemyContent(hc.content))
                errors.Add("Depart du heros sur une case qui contient " + hc.content + ".");
            else if (hc.hidden)
                warnings.Add("Depart du heros sur une case cachee : elle sera revelee au chargement.");
        }

        int onObstacle = 0, unsupported = 0;
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var c = _cells[x + y * width];
                bool hasEntity = c.content != CellContent.Empty && c.content != CellContent.Number;
                if (!hasEntity) continue;
                if (TerrainRules.IsObstacle(c.terrain)) onObstacle++;
                else if (!IsSupportedContent(c.content)) unsupported++;
            }

        if (onObstacle > 0)
            warnings.Add(onObstacle + " contenu(s) pose(s) sur un obstacle : ignore(s).");
        if (unsupported > 0)
            warnings.Add(unsupported + " tresor(s) : non pris en charge en v1, ignore(s).");
        if (CountEnemies() == 0)
            warnings.Add("Aucun ennemi dans le stage.");
    }

    // =========================================================================
    // ASCII — import / export
    // =========================================================================

    private static readonly char[] _enemyLetters =
        { 'w', 'b', 'm', 's', 'a', 'c', 'x', 'v', 'l', 'g', 'z', 'p' };
    private static readonly CellContent[] _enemyContents =
    {
        CellContent.Enemy_Wolf, CellContent.Enemy_Bear, CellContent.Enemy_Mercenary,
        CellContent.Enemy_BanditSword, CellContent.Enemy_BanditArcher, CellContent.Enemy_Crab,
        CellContent.Enemy_Spider, CellContent.Enemy_Bat, CellContent.Enemy_GoblinLance,
        CellContent.Enemy_GoblinMasse, CellContent.Enemy_Boss, CellContent.Trap
    };

    private static bool TryLetterToContent(char lower, out CellContent content)
    {
        for (int i = 0; i < _enemyLetters.Length; i++)
            if (_enemyLetters[i] == lower) { content = _enemyContents[i]; return true; }
        content = CellContent.Empty;
        return false;
    }

    private static bool TryContentToLetter(CellContent content, out char lower)
    {
        for (int i = 0; i < _enemyContents.Length; i++)
            if (_enemyContents[i] == content) { lower = _enemyLetters[i]; return true; }
        lower = '.';
        return false;
    }

    /// <summary>
    /// Remplit le stage depuis un texte ASCII (voir la legende en tete de fichier).
    /// Rien n est modifie si le texte est invalide.
    /// </summary>
    public bool LoadFromAscii(string text, BiomeType biome, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(text)) { error = "Texte vide."; return false; }

        var rows = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Replace("\r", "").Replace(" ", "").Replace("\t", "");
            if (line.Length > 0) rows.Add(line);
        }
        if (rows.Count == 0) { error = "Texte vide."; return false; }

        int h = rows.Count;
        int w = rows[0].Length;
        if (w < MinSize || w > MaxSize || h < MinSize || h > MaxSize)
        {
            error = "Taille " + w + "x" + h + " hors limites (" + MinSize + " a " + MaxSize + ").";
            return false;
        }

        var parsed = new StageCell[w * h];
        var hero = new Vector2Int(-1, -1);
        int heroCount = 0;

        for (int r = 0; r < h; r++)
        {
            if (rows[r].Length != w)
            {
                error = "Ligne " + (r + 1) + " : " + rows[r].Length + " caracteres, attendu " + w + ".";
                return false;
            }

            int y = h - 1 - r; // premiere ligne du texte = haut de la grille
            for (int x = 0; x < w; x++)
            {
                char ch = rows[r][x];
                var cell = new StageCell
                {
                    biome = biome,
                    terrain = TerrainType.Sol,
                    hidden = false,
                    content = CellContent.Empty
                };

                if (ch == '.') { }
                else if (ch == 'T' || ch == '?') cell.hidden = true;
                else if (ch == '~') cell.terrain = TerrainType.Eau;
                else if (ch == '#') cell.terrain = TerrainType.MurGrotte;
                else if (ch == '^') cell.terrain = TerrainType.Montagne;
                else if (ch == '@') { hero = new Vector2Int(x, y); heroCount++; }
                else
                {
                    char lower = char.ToLowerInvariant(ch);
                    CellContent content;
                    if (!TryLetterToContent(lower, out content))
                    {
                        error = "Ligne " + (r + 1) + ", colonne " + (x + 1)
                              + " : caractere '" + ch + "' inconnu.";
                        return false;
                    }
                    cell.content = content;
                    cell.hidden = ch != lower; // MAJUSCULE = cache
                }

                parsed[x + y * w] = cell;
            }
        }

        if (heroCount != 1)
        {
            error = "Il faut exactement un '@' (depart du heros), trouve " + heroCount + ".";
            return false;
        }

        width = w;
        height = h;
        defaultBiome = biome;
        heroStart = hero;
        _cells = parsed;
        _cellsWidth = w;
        _cellsHeight = h;
        _version = CurrentVersion;
        return true;
    }

    /// <summary>Texte ASCII du stage (meme legende que l import).</summary>
    public string ToAscii()
    {
        EnsureCells();
        var sb = new StringBuilder(height * (width + 1));
        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            {
                var c = _cells[x + y * width];
                char ch;
                char letter;
                if (x == heroStart.x && y == heroStart.y) ch = '@';
                else if (c.terrain == TerrainType.Eau) ch = '~';
                else if (c.terrain == TerrainType.MurGrotte) ch = '#';
                else if (c.terrain == TerrainType.Montagne) ch = '^';
                else if (TryContentToLetter(c.content, out letter))
                    ch = c.hidden ? char.ToUpperInvariant(letter) : letter;
                else if (c.hidden) ch = c.biome == BiomeType.Cave ? '?' : 'T';
                else ch = '.';
                sb.Append(ch);
            }
            if (y > 0) sb.Append('\n');
        }
        return sb.ToString();
    }

    [ContextMenu("Importer le texte ASCII")]
    private void ImportAsciiFromInspector()
    {
        string error;
        if (!LoadFromAscii(asciiSource, defaultBiome, out error))
        {
            Debug.LogError("[Stage] Import ASCII refuse pour '" + displayName + "' : " + error, this);
            return;
        }
        MarkDirty();
        Debug.Log("<color=#66DDFF>[Stage]</color> '" + displayName + "' importe : "
                  + width + "x" + height + ", " + CountEnemies() + " ennemi(s).", this);
    }

    [ContextMenu("Exporter vers le texte ASCII")]
    private void ExportAsciiToInspector()
    {
        asciiSource = ToAscii();
        MarkDirty();
    }

    private void MarkDirty()
    {
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}
using UnityEngine;

/// <summary>
/// CoastLayout -- Generation procedurale de la cote.
///
/// REGLES D'ANCRAGE :
///   - Chaque orientation utilise UNIQUEMENT ses propres pieces
///   - South : EdgeS, CornerSW, CornerSE, InnerSW, InnerSE
///   - West  : EdgeW, CornerSW, CornerNW, InnerSW, InnerNW
///   - East  : EdgeE, CornerSE, CornerNE, InnerSE, InnerNE
///
/// REGLES DE SEQUENCE :
///   - Apres Corner -> toujours Edge
///   - Inner commence l'expand, Corner finit l'expand
///   - Corner commence le retract, Inner finit le retract
///   - Edge entoure toujours les transitions
///   - Ouverture et fermeture de cote par Corner
///
/// PROFIL : demi-ovale (sinus) avec transitions +/-1 et espacement minRun.
/// </summary>
public class CoastLayout
{
    public int GridWidth { get; private set; }
    public int GridHeight { get; private set; }

    /// <summary>Profondeur (largeur eau) a chaque position le long de la cote.</summary>
    public int[] Depths { get; private set; }

    /// <summary>true = eau, false = terre.</summary>
    public bool[,] IsWater { get; private set; }

    /// <summary>Type de piece a chaque case.</summary>
    public CoastCellType[,] CellTypes { get; private set; }

    public CoastSide ResolvedSide { get; private set; }
    public int EffectiveStart { get; private set; }
    public int EffectiveLength { get; private set; }

    // =========================================================================
    // FACTORY — point d'entree unique pour les deux generateurs
    // =========================================================================

    /// <summary>
    /// Cree un CoastLayout avec gestion seed + generation.
    /// CoastGenerator ET CoastTestGenerator appellent cette methode
    /// pour garantir un comportement identique.
    /// </summary>
    public static CoastLayout CreateAndGenerate(CoastConfig config, int gridWidth, int gridHeight)
    {
        return CreateAndGenerate(config, gridWidth, gridHeight, -1, -1);
    }

    /// <summary>
    /// Surcharge avec override de startPos et length.
    /// startPosOverride >= 0 remplace config.startPos.
    /// lengthOverride >= 0 remplace config.length.
    /// </summary>
    public static CoastLayout CreateAndGenerate(CoastConfig config, int gridWidth, int gridHeight,
                                                 int startPosOverride, int lengthOverride)
    {
        // Seed pour variete reproductible
        // seed=0 → aleatoire base sur le temps reel + instanceID du config asset
        // (pas Random.Range, car l'etat global peut etre fixe par une generation precedente)
        int seed = config.seed;
        if (seed == 0)
        {
            // Combiner temps reel (unique par session) + instanceID (unique par asset)
            // + frame (unique par moment de generation) pour maximum de variete
            seed = (int)(Time.realtimeSinceStartupAsDouble * 10000.0)
                 ^ (config.GetInstanceID() * 397)
                 ^ (Time.frameCount * 31);
            if (seed == 0) seed = 1; // eviter 0 qui re-bouclerait
        }
        Random.InitState(seed);

        var layout = new CoastLayout();
        layout.Generate(config, gridWidth, gridHeight, startPosOverride, lengthOverride);

        Debug.Log("[CoastLayout] cote=" + layout.ResolvedSide +
                  " grid=" + gridWidth + "x" + gridHeight +
                  " start=" + layout.EffectiveStart +
                  " len=" + layout.EffectiveLength +
                  " seed=" + seed);

        return layout;
    }

    // =========================================================================
    // GENERATION
    // =========================================================================

    public void Generate(CoastConfig config, int gridWidth, int gridHeight,
                         int startPosOverride = -1, int lengthOverride = -1)
    {
        GridWidth = gridWidth;
        GridHeight = gridHeight;

        // Resoudre le cote
        ResolvedSide = config.side;
        if (ResolvedSide == CoastSide.RandomWE)
            ResolvedSide = Random.value < 0.5f ? CoastSide.West : CoastSide.East;

        int borderLen = IsVertical(ResolvedSide) ? gridHeight : gridWidth;

        // Appliquer les overrides si fournis (>= 0)
        int cfgStartPos = startPosOverride >= 0 ? startPosOverride : config.startPos;
        int cfgLength = lengthOverride >= 0 ? lengthOverride : config.length;

        // South = plage horizontale : toujours bord gauche a bord droit
        if (ResolvedSide == CoastSide.South)
        {
            EffectiveStart = 0;
            EffectiveLength = gridWidth;
        }
        else
        {
            EffectiveStart = Mathf.Clamp(cfgStartPos, 0, borderLen - 1);
            EffectiveLength = cfgLength > 0
                ? Mathf.Min(cfgLength, borderLen - EffectiveStart)
                : borderLen - EffectiveStart;
        }

        if (EffectiveLength < 2)
        {
            Depths = new int[] { Mathf.Max(1, config.minDepth) };
            IsWater = new bool[gridWidth, gridHeight];
            CellTypes = new CoastCellType[gridWidth, gridHeight];
            return;
        }

        // Limiter maxDepth a la grille
        int maxAllowed = IsVertical(ResolvedSide)
            ? gridWidth - 1    // laisser au moins 1 colonne de terre
            : gridHeight - 1;

        Depths = GenerateOvalProfile(config, maxAllowed);
        IsWater = new bool[gridWidth, gridHeight];
        CellTypes = new CoastCellType[gridWidth, gridHeight];

        switch (ResolvedSide)
        {
            case CoastSide.West: BuildWest(); break;
            case CoastSide.East: BuildEast(); break;
            case CoastSide.South: BuildSouth(); break;
        }

    }

    // =========================================================================
    // PROFIL DE PROFONDEUR — demi-ovale (sinus) + perturbation aleatoire
    //
    // 1. Courbe cible = sin(t*PI) * (max-min) + min
    // 2. Passe avant : suit la cible avec transitions +/-1, espacement minRun,
    //    et perturbation aleatoire via expandChance / retractChance
    // 3. Passe arriere : idem depuis la fin
    // 4. Resultat = min(avant, arriere) -> garantit retour a minDepth aux 2 bouts
    //
    // La perturbation ajoute de la variete : a chaque step ou la courbe ideale
    // ne demande pas de transition, expandChance/retractChance peuvent quand
    // meme forcer un +1 ou -1, cree des formes organiques differentes a chaque
    // seed tout en respectant les bornes min/max et l'espacement minRun.
    // =========================================================================

    private int[] GenerateOvalProfile(CoastConfig config, int maxAllowed)
    {
        int len = EffectiveLength;
        int minD = Mathf.Max(1, config.minDepth);
        int maxD = Mathf.Clamp(config.maxDepth, minD, maxAllowed);
        int minRun = Mathf.Max(2, config.minRunLength);
        float expChance = config.expandChance;
        float retChance = config.retractChance;

        // Profondeur de depart (peut varier)
        int startD = config.startDepth > 0
            ? Mathf.Clamp(config.startDepth, minD, maxD)
            : minD;

        // Courbe cible
        var ideal = new float[len];
        for (int i = 0; i < len; i++)
        {
            float t = len > 1 ? (float)i / (len - 1) : 0.5f;
            ideal[i] = minD + Mathf.Sin(t * Mathf.PI) * (maxD - minD);
        }

        // Passe avant — suit la cible avec perturbation aleatoire
        var fwd = new int[len];
        int cur = startD;
        int run = minRun; // permettre transition rapidement
        fwd[0] = startD;
        for (int i = 1; i < len; i++)
        {
            run++;
            if (run > minRun)
            {
                bool wantsExpand = ideal[i] > cur + 0.5f;
                bool wantsRetract = ideal[i] < cur - 0.5f;

                // Perturbation : meme si la cible ne demande pas de transition,
                // le hasard peut en forcer une (formes organiques)
                if (!wantsExpand && !wantsRetract)
                {
                    if (cur < maxD && Random.value < expChance) wantsExpand = true;
                    else if (cur > minD && Random.value < retChance) wantsRetract = true;
                }

                if (wantsExpand && cur < maxD) { cur++; run = 0; }
                else if (wantsRetract && cur > minD) { cur--; run = 0; }
            }
            fwd[i] = cur;
        }

        // Passe arriere — meme logique, force le retour a startD
        var bwd = new int[len];
        cur = startD;
        run = minRun;
        bwd[len - 1] = startD;
        for (int i = len - 2; i >= 0; i--)
        {
            run++;
            if (run > minRun)
            {
                bool wantsExpand = ideal[i] > cur + 0.5f;
                bool wantsRetract = ideal[i] < cur - 0.5f;

                if (!wantsExpand && !wantsRetract)
                {
                    if (cur < maxD && Random.value < expChance) wantsExpand = true;
                    else if (cur > minD && Random.value < retChance) wantsRetract = true;
                }

                if (wantsExpand && cur < maxD) { cur++; run = 0; }
                else if (wantsRetract && cur > minD) { cur--; run = 0; }
            }
            bwd[i] = cur;
        }

        // Fusionner : minimum des deux passes
        var depths = new int[len];
        for (int i = 0; i < len; i++)
            depths[i] = Mathf.Min(fwd[i], bwd[i]);

        return depths;
    }

    // =========================================================================
    // BUILD WEST — cote gauche (verticale, marche bas vers haut)
    //
    // Pieces : EdgeW, CornerSW (open/expand), CornerNW (close/retract),
    //          InnerSW (expand start), InnerNW (retract end)
    // =========================================================================

    private void BuildWest()
    {
        for (int i = 0; i < EffectiveLength; i++)
        {
            int y = EffectiveStart + i;
            if (y < 0 || y >= GridHeight) continue;

            int d = Depths[i];
            int prevD = i > 0 ? Depths[i - 1] : 0;
            int waterW = Mathf.Max(d, prevD); // transition rows use wider depth

            // Marquer eau
            for (int x = 0; x < waterW && x < GridWidth; x++)
                IsWater[x, y] = true;

            // Placer pieces
            if (i == 0)
            {
                // Ouverture : Corner ouvre la forme
                SetCell(d - 1, y, CoastCellType.CornerSW);
            }
            else if (i == EffectiveLength - 1)
            {
                // Fermeture : Corner ferme la forme
                SetCell(d - 1, y, CoastCellType.CornerNW);
            }
            else if (d > prevD)
            {
                // EXPAND : Inner commence (ancien bord), Corner finit (nouveau bord)
                SetCell(prevD - 1, y, CoastCellType.InnerSW);
                SetCell(d - 1, y, CoastCellType.CornerSW);
            }
            else if (d < prevD)
            {
                // RETRACT : Corner commence (ancien bord), Inner finit (nouveau bord)
                SetCell(prevD - 1, y, CoastCellType.CornerNW);
                SetCell(d - 1, y, CoastCellType.InnerNW);
            }
            else
            {
                // Meme profondeur : Edge
                SetCell(d - 1, y, CoastCellType.EdgeW);
            }

            // Remplir interieur avec Water
            for (int x = 0; x < waterW && x < GridWidth; x++)
                if (CellTypes[x, y] == CoastCellType.None)
                    CellTypes[x, y] = CoastCellType.Water;
        }
    }

    // =========================================================================
    // BUILD EAST — cote droite (verticale, marche bas vers haut)
    //
    // Pieces : EdgeE, CornerSE (open/expand), CornerNE (close/retract),
    //          InnerSE (expand start), InnerNE (retract end)
    // =========================================================================

    private void BuildEast()
    {
        for (int i = 0; i < EffectiveLength; i++)
        {
            int y = EffectiveStart + i;
            if (y < 0 || y >= GridHeight) continue;

            int d = Depths[i];
            int prevD = i > 0 ? Depths[i - 1] : 0;
            int waterW = Mathf.Max(d, prevD);

            // Marquer eau (depuis le bord droit)
            for (int dx = 0; dx < waterW; dx++)
            {
                int x = GridWidth - 1 - dx;
                if (x >= 0) IsWater[x, y] = true;
            }

            // Colonnes bord
            int edgeX = GridWidth - d;
            int prevEdgeX = prevD > 0 ? GridWidth - prevD : GridWidth;

            if (i == 0)
            {
                SetCell(edgeX, y, CoastCellType.CornerSE);
            }
            else if (i == EffectiveLength - 1)
            {
                SetCell(edgeX, y, CoastCellType.CornerNE);
            }
            else if (d > prevD) // Expand
            {
                SetCell(prevEdgeX, y, CoastCellType.InnerSE);
                SetCell(edgeX, y, CoastCellType.CornerSE);
            }
            else if (d < prevD) // Retract
            {
                SetCell(prevEdgeX, y, CoastCellType.CornerNE);
                SetCell(edgeX, y, CoastCellType.InnerNE);
            }
            else
            {
                SetCell(edgeX, y, CoastCellType.EdgeE);
            }

            // Remplir interieur
            for (int dx = 0; dx < waterW; dx++)
            {
                int x = GridWidth - 1 - dx;
                if (x >= 0 && CellTypes[x, y] == CoastCellType.None)
                    CellTypes[x, y] = CoastCellType.Water;
            }
        }
    }

    // =========================================================================
    // BUILD SOUTH — cote basse (horizontale, marche gauche vers droite)
    //
    // Sprites corriges pour orientation sud (eau en bas, terre en haut) :
    //   - CornerNE = ouverture/expand (ancien CornerSW)
    //   - CornerNW = fermeture/retract (ancien CornerSE)
    //   - InnerNE  = expand start (ancien InnerSW)
    //   - InnerNW  = retract end (ancien InnerSE)
    // =========================================================================

    private void BuildSouth()
    {
        for (int i = 0; i < EffectiveLength; i++)
        {
            int x = EffectiveStart + i;
            if (x < 0 || x >= GridWidth) continue;

            int d = Depths[i];
            int prevD = i > 0 ? Depths[i - 1] : 0;
            int waterH = Mathf.Max(d, prevD);

            // Marquer eau (depuis le bas)
            for (int dy = 0; dy < waterH && dy < GridHeight; dy++)
                IsWater[x, dy] = true;

            if (i == 0)
            {
                SetCell(x, d - 1, CoastCellType.CornerNE);
            }
            else if (i == EffectiveLength - 1)
            {
                SetCell(x, d - 1, CoastCellType.CornerNW);
            }
            else if (d > prevD) // Expand
            {
                SetCell(x, prevD - 1, CoastCellType.InnerNE);
                SetCell(x, d - 1, CoastCellType.CornerNE);
            }
            else if (d < prevD) // Retract
            {
                SetCell(x, prevD - 1, CoastCellType.CornerNW);
                SetCell(x, d - 1, CoastCellType.InnerNW);
            }
            else
            {
                SetCell(x, d - 1, CoastCellType.EdgeS);
            }

            // Remplir interieur
            for (int dy = 0; dy < waterH && dy < GridHeight; dy++)
                if (CellTypes[x, dy] == CoastCellType.None)
                    CellTypes[x, dy] = CoastCellType.Water;
        }
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private void SetCell(int x, int y, CoastCellType type)
    {
        if (x >= 0 && x < GridWidth && y >= 0 && y < GridHeight)
            CellTypes[x, y] = type;
    }

    public static bool IsVertical(CoastSide side) =>
        side == CoastSide.East || side == CoastSide.West;
}

// =========================================================================
// ENUM — Types de pieces cotieres
// =========================================================================

public enum CoastCellType
{
    None = 0,
    Water,

    // Edges (bord droit — 1 orientation terre)
    EdgeS, EdgeN, EdgeE, EdgeW,

    // Corners (convexe — 2 voisins terre adjacents)
    CornerNW, CornerNE, CornerSW, CornerSE,

    // Inners (concave — transition profondeur)
    InnerNW, InnerNE, InnerSW, InnerSE,
}
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// MountainWFCGenerator — Génère la montagne correctement.
///
/// STRUCTURE :
///   BASE : construite HORIZONTALEMENT
///     AngleL → [FaceL + FaceR] × pairCount → AngleR
///     
///   MID : DEUX COLONNES VERTICALES indépendantes (gauche + droite)
///     Colonne gauche : monte depuis le haut de AngleL
///     Colonne droite : monte depuis le haut de AngleR
///     Pièces : BorderSimple, BorderDouble, Turn (relief), Angle (cassure)
///     
///   TOP : fermeture
///     AngleTopG (gauche) → TopEdge × largeur → AngleTopD (droite)
/// </summary>
public class MountainWFCGenerator
{
    public struct Placement
    {
        public PieceType type;
        public int gridX, gridY;
    }

    public List<Placement> Placements { get; } = new();
    public int SommetY { get; private set; }

    private MountainRecipe _recipe;
    private System.Random _rng;
    private int _gridWidth;

    // Anchors de sortie de la base (point de départ des colonnes mid)
    private MountainAnchor _topOfAngleL;
    private MountainAnchor _topOfAngleR;
    private int _baseLeftX, _baseRightX; // positions X des bords

    // =========================================================================
    // GÉNÉRATION
    // =========================================================================

    public void Generate(MountainRecipe recipe, int baseX, int baseY, int gridWidth)
    {
        Placements.Clear();
        _recipe = recipe;
        _rng = new System.Random();
        _gridWidth = gridWidth;

        // ── PHASE 1 : BASE (horizontal) ──────────────────────────────────────
        BuildBase(baseX, baseY);

        // ── PHASE 2 : MID (deux colonnes verticales) ─────────────────────────
        bool extLeft = _recipe.hasExtension && _recipe.extensionOnLeft;
        bool extRight = _recipe.hasExtension && !_recipe.extensionOnLeft;
        var topL = BuildColumnWithExt(_topOfAngleL, PieceType.BorderSimpleL, true, extLeft);
        var topR = BuildColumnWithExt(_topOfAngleR, PieceType.BorderSimpleR, false, extRight);
        _baseLeftX = topL.currentX;
        _baseRightX = topR.currentX;


        // ── PHASE 3 : TOP (fermeture horizontale) ────────────────────────────
        BuildTop(topL.anchor, topL.lastType, topR.anchor, topR.lastType);

        // ── POST-TRAITEMENT : remplacer les compositions par des virages ─────
        PostProcessVirages();
        FillGapsAfterVirages();

        Debug.Log($"[MtnWFC] Montagne : {Placements.Count} pièces, " +
                  $"base=({_baseLeftX},{baseY}), sommet Y={SommetY}");
    }

    // =========================================================================
    // BASE — HORIZONTALE : AngleL → Paires de Faces → AngleR
    // =========================================================================

    /// <summary>
    /// BASE : colonne par colonne, de gauche à droite.
    /// curX avance d'1 case par pièce. Y change pour les cassures.
    ///
    /// Structure : AngleL [FaceL FaceR]×pairCount AngleR
    /// Cassure : angle entre deux paires qui décale Y
    /// </summary>
    private void BuildBase(int centerX, int baseY)
    {
        int pairs = _recipe.pairCount;
        int totalWidth = pairs * 2 + 2;

        // Position de la base selon le côté de référence
        int lx;
        if (_recipe.referenceFromRight)
        {
            // Référence droite : le bord DROIT est à xAtBase du bord droit
            // → le bord gauche = (gridWidth - xAtBase) - largeur totale
            int rightEdge = _gridWidth - 1 - _recipe.xAtBase;
            lx = Mathf.Clamp(rightEdge - totalWidth + 1, 0, _gridWidth - totalWidth);
        }
        else
        {
            lx = Mathf.Clamp(_recipe.xAtBase, 0, 14);
        }

        // Cave : quelle paire et quel côté (gauche OU droite, pas les deux)
        int cavePair = _rng.Next(0, pairs);
        bool caveOnLeft = _rng.Next(2) == 0;

        // Cassure : entre quelle paire ? direction ?
        bool hasCassure = _recipe.allowBreaks && pairs >= 3
                       && _rng.NextDouble() < _recipe.breakChance;
        int cassureAfter = hasCassure ? _rng.Next(0, pairs - 1) : -1;
        int cassureDir = _rng.Next(2) == 0 ? 1 : -1; // +1=monte, -1=descend

        int curX = lx;
        int curY = baseY;

        // ── AngleL de départ ─────────────────────────────────────────────────
        Add(PieceType.AngleL, new MountainAnchor(curX, curY));
        _baseLeftX = curX;
        var startAngleL = new MountainAnchor(curX, curY);
        curX++;

        // ── Paires de faces ──────────────────────────────────────────────────
        for (int i = 0; i < pairs; i++)
        {
            bool isCavePairL = (i == cavePair && caveOnLeft);
            bool isCavePairR = (i == cavePair && !caveOnLeft);
            PieceType fL = isCavePairL ? PieceType.FaceLeftCave : PieceType.FaceLeft;
            PieceType fR = isCavePairR ? PieceType.FaceRightCave : PieceType.FaceRight;

            // FaceLeft
            Add(fL, new MountainAnchor(curX, curY));
            curX++;

            // FaceRight
            Add(fR, new MountainAnchor(curX, curY));
            curX++;

            // Cassure après cette paire ?
            if (i == cassureAfter && i < pairs - 1)
            {
                PieceType cassureType = cassureDir > 0 ? PieceType.AngleR : PieceType.AngleL;
                // AngleL (descend) → placer au niveau BAS (curY-1)
                // AngleR (monte)   → placer au niveau actuel (curY)
                int angleY = cassureDir < 0 ? curY - 1 : curY;
                Add(cassureType, new MountainAnchor(curX, angleY));
                curX++;
                curY += cassureDir;
            }
        }

        // ── AngleR de fermeture ──────────────────────────────────────────────
        Add(PieceType.AngleR, new MountainAnchor(curX, curY));
        _baseRightX = curX;
        var endAngleR = new MountainAnchor(curX, curY);

        // ── Points de départ pour les colonnes mid ───────────────────────────
        // Colonne gauche : part du HAUT de l'AngleL de départ
        int angleHeight = MountainPieceData.GetHeight(PieceType.AngleL); // 5
        _topOfAngleL = new MountainAnchor(_baseLeftX, startAngleL.Y + angleHeight);
        _topOfAngleR = new MountainAnchor(_baseRightX, endAngleR.Y + angleHeight);

        _baseThickness = _baseRightX - _baseLeftX;
        Debug.Log($"[MtnWFC] Base : lx={_baseLeftX} rx={_baseRightX} épaisseur={_baseThickness} " +
                  $"pairs={pairs} cave=pair{cavePair} " +
                  $"cassure={(hasCassure ? $"après{cassureAfter} dir={cassureDir}" : "non")}");
    }

    // =========================================================================
    // COLONNE MID — contour continu, aucun trou
    // =========================================================================

    private struct ColumnResult
    {
        public MountainAnchor anchor;
        public PieceType lastType;
        public int currentX;
    }

    private ColumnResult BuildColumnWithExt(MountainAnchor start, PieceType fromType,
                                              bool isLeft, bool withExtension = false)
    {
        int midHeight = _recipe.midTotalHeight;
        int extInsertAt = -1;
        if (withExtension)
        {
            int tierH = midHeight / 3;
            extInsertAt = (_recipe.extensionTier - 1) * tierH + tierH / 2;
        }
        bool extensionDone = false;

        // ── Placer la PREMIÈRE pièce au point de départ (pas de trou) ─────────
        PieceType firstPiece = isLeft ? PieceType.BorderSimpleL : PieceType.BorderSimpleR;
        Add(firstPiece, start);

        MountainAnchor cur = start;
        PieceType last = firstPiece;
        int built = MountainPieceData.GetHeight(firstPiece);
        int consecBord = 1;
        bool prevWasTurn = false;
        bool prevWasAngle = false;

        while (built < midHeight)
        {
            int tier = _recipe.GetTier(built, 0);
            int remaining = midHeight - built;

            // Calculer le X cible — seulement pour le côté RÉFÉRENCE
            bool isReferenceSide = (isLeft && !_recipe.referenceFromRight)
                                || (!isLeft && _recipe.referenceFromRight);
            if (isReferenceSide)
            {
                int baseH = MountainPieceData.GetHeight(PieceType.AngleL);
                float heightRatio = (float)(baseH + built) / (baseH + midHeight + 2);
                int rawTarget = _recipe.GetTargetX(heightRatio);

                // Convertir distance-du-bord en position X réelle
                if (_recipe.referenceFromRight)
                    _currentTargetX = _gridWidth - 1 - rawTarget;
                else
                    _currentTargetX = rawTarget;
                _currentTargetX = Mathf.Clamp(_currentTargetX, 0, _gridWidth - 1);
                _currentColumnX = cur.X;
            }
            else
            {
                // Côté spectateur : calculer le cible depuis Recipe + épaisseur
                // PAS depuis _refSideLastX (qui est le X final, pas celui à cette hauteur)
                int baseH = MountainPieceData.GetHeight(PieceType.AngleL);
                float hRatio = (float)(baseH + built) / (baseH + midHeight + 2);
                int rawRefTarget = _recipe.GetTargetX(hRatio);

                // Convertir en X réel puis ajouter/soustraire l'épaisseur
                int refXReal;
                if (_recipe.referenceFromRight)
                    refXReal = _gridWidth - 1 - rawRefTarget;
                else
                    refXReal = rawRefTarget;

                // Épaisseur cible : moyenne entre min et max du Recipe
                int targetThickness = (_recipe.thicknessMin + _recipe.thicknessMax) / 2;
                if (targetThickness > 0)
                {
                    if (_recipe.referenceFromRight)
                        _currentTargetX = refXReal - targetThickness;
                    else
                        _currentTargetX = refXReal + targetThickness;
                    _currentTargetX = Mathf.Clamp(_currentTargetX, 0, _gridWidth - 1);
                    _currentColumnX = cur.X;
                }
                else
                {
                    _currentTargetX = -1;
                    _currentColumnX = -1;
                }
            }

            // ── Extension face au bon tier ───────────────────────────────────
            if (withExtension && !extensionDone && _recipe.extensionTier <= 3 && built >= extInsertAt)
            {
                // Avant l'extension : signaler le thicknessMin comme cible
                // pour que le spectateur se rapproche avant l'ouverture
                _baseThickness = _recipe.thicknessMin;

                InsertExtensionInline(ref cur, ref last, ref built, isLeft);
                extensionDone = true;
                consecBord = 0; prevWasTurn = false; prevWasAngle = false;

                // Après l'extension : rétablir l'épaisseur moyenne
                _baseThickness = (_recipe.thicknessMin + _recipe.thicknessMax) / 2;
                continue;
            }

            PieceType next = ChooseMidPiece(
                last, isLeft, tier, remaining,
                consecBord, prevWasTurn, prevWasAngle);

            // Toutes les pièces sont des sprites uniques placés via GetAnchorOut
            MountainAnchor nextAnchor = MountainPieceData.GetAnchorOut(last, cur, next);

            // Combler les trous si gap vertical
            int curTop = cur.Y + MountainPieceData.GetHeight(last);
            int gapCells = nextAnchor.Y - curTop;
            if (gapCells > 0)
            {
                PieceType fillBord = isLeft ? PieceType.BorderSimpleL : PieceType.BorderSimpleR;
                for (int g = 0; g < gapCells; g++)
                {
                    MountainAnchor fillA = new MountainAnchor(cur.X, curTop + g);
                    Add(fillBord, fillA);
                    built += 1;
                    cur = fillA;
                    last = fillBord;
                }
                // Recalculer l'ancrage depuis le dernier bord intercalé
                nextAnchor = MountainPieceData.GetAnchorOut(last, cur, next);
            }

            Add(next, nextAnchor);
            int h = MountainPieceData.GetHeight(next);
            built += h;
            cur = nextAnchor;
            last = next;

            bool isBordS = (last == PieceType.BorderSimpleL || last == PieceType.BorderSimpleR);
            bool isBordD = (last == PieceType.BorderDoubleL || last == PieceType.BorderDoubleR);
            bool isBord = isBordS || isBordD;
            bool isTurn = (last == PieceType.TurnShortL || last == PieceType.TurnShortR ||
                            last == PieceType.TurnLargeL || last == PieceType.TurnLargeR);
            bool isAngle = (last == PieceType.AngleL || last == PieceType.AngleR ||
                            last == PieceType.AngleTopG || last == PieceType.AngleTopD);

            // Tracking hauteur en CELLS (pas en count)
            if (isBordS) consecBord += 1;
            else if (isBordD) consecBord += 2;
            else consecBord = 0;

            prevWasTurn = isTurn;
            prevWasAngle = isAngle;

            // Mettre à jour le X du côté référence
            bool isRefSide = (isLeft && !_recipe.referenceFromRight)
                          || (!isLeft && _recipe.referenceFromRight);
            if (isRefSide) _refSideLastX = cur.X;

            // Tracker la distance depuis le dernier retract
            bool isRetract = (last == PieceType.AngleTopG || last == PieceType.AngleTopD);
            if (isRetract) _heightSinceRetract = 0;
            else if (_heightSinceRetract >= 0)
            {
                _heightSinceRetract += MountainPieceData.GetHeight(last);
                if (_heightSinceRetract > 4) _heightSinceRetract = -1; // reset
            }
        }

        // ── Rattrapage final : si le X cible n'est pas atteint ───────────────
        bool isRefFinal = (isLeft && !_recipe.referenceFromRight)
                       || (!isLeft && _recipe.referenceFromRight);
        if (isRefFinal)
        {
            int baseH = MountainPieceData.GetHeight(PieceType.AngleL);
            float finalRatio = (float)(baseH + built) / (baseH + midHeight + 2);
            int rawFinal = _recipe.GetTargetX(finalRatio);
            int finalTarget = _recipe.referenceFromRight
                ? _gridWidth - 1 - rawFinal : rawFinal;
            finalTarget = Mathf.Clamp(finalTarget, 0, _gridWidth - 1);

            int safety2 = 0;
            while (cur.X != finalTarget && safety2 < 8)
            {
                safety2++;
                PieceType brd = isLeft ? PieceType.BorderSimpleL : PieceType.BorderSimpleR;

                if ((isLeft && cur.X > finalTarget) || (!isLeft && cur.X < finalTarget))
                {
                    // Besoin d'expand — vérifier qu'on ne sort pas de la grille
                    PieceType exp = isLeft ? PieceType.AngleL : PieceType.AngleR;
                    MountainAnchor ea = MountainPieceData.GetAnchorOut(last, cur, exp);
                    if (ea.X < 0 || ea.X >= _gridWidth) break; // stop si hors grille
                    Add(exp, ea);
                    cur = ea; last = exp;
                    // Bord après expand
                    MountainAnchor ba = MountainPieceData.GetAnchorOut(last, cur, brd);
                    Add(brd, ba);
                    cur = ba; last = brd;
                }
                else if ((isLeft && cur.X < finalTarget) || (!isLeft && cur.X > finalTarget))
                {
                    // Besoin de retract
                    PieceType ret = isLeft ? PieceType.AngleTopG : PieceType.AngleTopD;
                    MountainAnchor ra = MountainPieceData.GetAnchorOut(last, cur, ret);
                    Add(ret, ra);
                    cur = ra; last = ret;
                    // Bord après retract
                    MountainAnchor ba = MountainPieceData.GetAnchorOut(last, cur, brd);
                    Add(brd, ba);
                    cur = ba; last = brd;
                }
                else break;
            }
        }

        return new ColumnResult { anchor = cur, lastType = last, currentX = cur.X };
    }

    private int _heightSinceRetract = -1;
    private int _currentTargetX = -1;
    private int _currentColumnX = -1;
    private int _refSideLastX = -1;
    private int _dummyBuilt = 0;   // dernier X du côté référence (pour compenser)
    private int _baseThickness = -1;  // épaisseur de la base (pour maintenir)

    private PieceType ChooseMidPiece(
        PieceType last, bool isLeft, int tier, int remaining,
        int consecBordHeight, bool prevWasTurn, bool prevWasAngle)
    {
        PieceType bS = isLeft ? PieceType.BorderSimpleL : PieceType.BorderSimpleR;
        PieceType bD = isLeft ? PieceType.BorderDoubleL : PieceType.BorderDoubleR;
        PieceType tS = isLeft ? PieceType.TurnShortL : PieceType.TurnShortR;
        PieceType tL = isLeft ? PieceType.TurnLargeL : PieceType.TurnLargeR;
        PieceType tXL = isLeft ? PieceType.TurnExtraLargeL : PieceType.TurnExtraLargeR;
        PieceType aExp = isLeft ? PieceType.AngleL : PieceType.AngleR;
        PieceType aRet = isLeft ? PieceType.AngleTopG : PieceType.AngleTopD;

        var opts = new System.Collections.Generic.List<(PieceType t, int w)>();

        // ── Bloquer expand si déjà au bord de la grille ─────────────────────
        bool atGridEdge = (isLeft && _currentColumnX <= 0) ||
                          (!isLeft && _currentColumnX >= _gridWidth - 1);

        // ── Calcul orientation X (en premier pour être disponible partout) ────
        bool needExpand = false;
        bool needRetract = false;
        int xDiff = 0;
        if (_currentTargetX >= 0 && _currentColumnX >= 0)
        {
            if (isLeft)
            {
                xDiff = _currentColumnX - _currentTargetX;
                needExpand = xDiff > 0;
                needRetract = xDiff < 0;
            }
            else
            {
                xDiff = _currentTargetX - _currentColumnX;
                needExpand = xDiff > 0;
                needRetract = xDiff < 0;
            }
            xDiff = Mathf.Abs(xDiff);
        }

        // ── Après un angle → BorderSimple UNIQUEMENT (BorderDouble chevauche) ─
        if (prevWasAngle)
        {
            return bS;
        }

        // ── Après un virage → bord obligatoire ───────────────────────────────
        if (prevWasTurn)
        {
            if (remaining >= 2) opts.Add((bD, 5));
            opts.Add((bS, 3));
            return Pick(opts);
        }

        // ── Orientation vers X cible (PRIORITAIRE, avant les autres règles) ──
        // Côté référence : suit les valeurs Recipe précisément
        // Côté spectateur : suit doucement pour garder l'épaisseur
        bool isRefSide2 = (_currentTargetX >= 0 && _currentColumnX >= 0);
        bool isSpectator = isRefSide2 && (
            (isLeft && _recipe.referenceFromRight) ||
            (!isLeft && !_recipe.referenceFromRight));

        if (xDiff >= 1 && consecBordHeight >= 1 && !prevWasAngle && !prevWasTurn)
        {
            int expandW, retractW, bordW;
            if (isSpectator)
            {
                if (xDiff < 1) goto skipOrientation;

                // Poids modéré — entre passif et agressif
                expandW = Mathf.Min(xDiff * 2, 8);
                retractW = Mathf.Min(xDiff * 2, 8);
                bordW = 3;
            }
            else
            {
                expandW = Mathf.Min(xDiff * 3, 12);
                retractW = Mathf.Min(xDiff * 3, 12);
                bordW = 2;
            }

            if (needExpand)
            {
                // xDiff >= 3 → ignorer _heightSinceRetract (urgence)
                if (xDiff >= 3)
                    opts.Add((aExp, expandW));
                else
                {
                    int minDist = xDiff >= 2 ? 2 : 4;
                    if (_heightSinceRetract < 0 || _heightSinceRetract >= minDist)
                        opts.Add((aExp, expandW));
                }
            }
            if (needRetract)
            {
                opts.Add((aRet, retractW));
            }
            opts.Add((bS, bordW));
            if (remaining >= 2) opts.Add((bD, Mathf.Max(1, bordW - 1)));
            if (opts.Count > 0) return Pick(opts);
        }
    skipOrientation:

        // ── Si très peu de place, uniquement BorderSimple ───────────────────
        if (remaining <= 3)
        {
            opts.Add((bS, 8));
            if (remaining >= 2) opts.Add((bD, 3));
            return Pick(opts);
        }



        // ── Orientation douce vers le X cible (seulement côté référence) ─────
        // Dès 1 bord posé, favoriser expand/retract si X est loin du cible
        if (xDiff >= 1 && consecBordHeight >= 1 && !prevWasAngle && !prevWasTurn)
        {
            // Expand : seulement si assez de distance depuis le dernier retract
            // PAS de virage (ils ne changent pas X)
            if (needExpand && remaining >= 2)
            {
                int ew = Mathf.Min(xDiff * 3, 10);
                if (_heightSinceRetract == 1 && remaining >= 3)
                    opts.Add((tS, ew));
                else if (_heightSinceRetract == 2 && remaining >= 4)
                    opts.Add((tL, ew));
                else if (_heightSinceRetract == 3 && remaining >= 5)
                    opts.Add((tXL, ew));
                else if ((_heightSinceRetract < 0 || _heightSinceRetract >= 4) && !atGridEdge)
                    opts.Add((aExp, ew));
            }
            if (needRetract && remaining >= 1)
            {
                opts.Add((aRet, xDiff >= 2 ? 10 : 5));
            }
            opts.Add((bS, 2));
            if (remaining >= 2) opts.Add((bD, 1));
            if (opts.Count > 0) return Pick(opts);
        }

        // ── RÈGLE : max 3 hauteur de bords consécutifs ───────────────────────
        if (consecBordHeight >= 3)
        {
            if (remaining >= 4) opts.Add((tS, 3));
            if (remaining >= 5) opts.Add((tL, 2));
            if (remaining >= 3)
            {
                bool canExp = isLeft ? _recipe.CanExpandSecret(tier) : _recipe.CanExpandBiome(tier);
                if (canExp)
                {
                    // LOI VIRAGE : remplacer expand par Turn si trop proche d'un retract
                    if (_heightSinceRetract == 1 && remaining >= 3)
                        opts.Add((tS, 3)); // TurnShort remplace retract+1bord+expand
                    else if (_heightSinceRetract == 2 && remaining >= 4)
                        opts.Add((tL, 3)); // TurnLarge remplace retract+2bords+expand
                    else if (_heightSinceRetract == 3)
                    { } // INTERDIT — pas de sprite pour 3 hauteur
                    else if (_heightSinceRetract < 0 || _heightSinceRetract >= 4)
                        opts.Add((aExp, needExpand ? 4 : 2));
                }
            }
            if (remaining >= 3)
            {
                bool canRet = isLeft ? _recipe.CanRetractSecret(tier) : _recipe.CanRetractBiome(tier);
                if (canRet) opts.Add((aRet, needRetract ? 4 : 2));
            }
            if (opts.Count > 0) return Pick(opts);
            // Fallback si rien de spécial possible : bord simple
            return bS;
        }

        // ── RÈGLE : pas 2 BorderSimple d'affilé → utiliser BorderDouble ──────
        if (consecBordHeight == 1 && last == bS)
        {
            // Après 1 BorderSimple : préférer BorderDouble (= total 3 de haut)
            if (remaining >= 2) opts.Add((bD, 6));
            // Ou éléments spéciaux si 2 cases de bord suffisent
            if (remaining >= 4) opts.Add((tS, 2));
            if (remaining >= 5) opts.Add((tL, 1));
            if (remaining >= 3)
            {
                bool canExp = isLeft ? _recipe.CanExpandSecret(tier) : _recipe.CanExpandBiome(tier);
                if (canExp)
                {
                    if (_heightSinceRetract == 1) opts.Add((tS, 2));
                    else if (_heightSinceRetract == 2) opts.Add((tL, 2));
                    else if (_heightSinceRetract == 3) opts.Add((tXL, 2));
                    else if (!atGridEdge) opts.Add((aExp, 1));
                }
            }
            if (remaining >= 3)
            {
                bool canRet = isLeft ? _recipe.CanRetractSecret(tier) : _recipe.CanRetractBiome(tier);
                if (canRet) opts.Add((aRet, 1));
            }
            if (opts.Count > 0) return Pick(opts);
            return bS;
        }

        // ── RÈGLE : après BorderDouble (2 cases) → spécial ou 1 BorderSimple ─
        if (consecBordHeight == 2)
        {
            opts.Add((bS, 3)); // 1 simple pour arriver à 3 → puis spécial
            if (remaining >= 4) opts.Add((tS, 2));
            if (remaining >= 5) opts.Add((tL, 1));
            if (remaining >= 3)
            {
                bool canExp = isLeft ? _recipe.CanExpandSecret(tier) : _recipe.CanExpandBiome(tier);
                if (canExp)
                {
                    if (_heightSinceRetract == 1) opts.Add((tS, 2));
                    else if (_heightSinceRetract == 2) opts.Add((tL, 2));
                    else if (_heightSinceRetract == 3) opts.Add((tXL, 2));
                    else if (!atGridEdge) opts.Add((aExp, 1));
                }
            }
            if (remaining >= 3)
            {
                bool canRet = isLeft ? _recipe.CanRetractSecret(tier) : _recipe.CanRetractBiome(tier);
                if (canRet) opts.Add((aRet, 1));
            }
            return Pick(opts);
        }

        // ── Début de section (0 bords) → commencer par un bord ───────────────
        if (remaining >= 2) opts.Add((bD, 4));
        opts.Add((bS, 3));
        // Spéciaux possibles si au moins 1 bord avant (consecBordHeight > 0)
        if (consecBordHeight >= 1)
        {
            if (remaining >= 4) opts.Add((tS, 2));
            if (remaining >= 5) opts.Add((tL, 1));
        }

        return Pick(opts);
    }

    private PieceType Pick(System.Collections.Generic.List<(PieceType t, int w)> opts)
    {
        int total = 0;
        foreach (var (_, w) in opts) total += w;
        int roll = _rng.Next(total);
        int acc = 0;
        foreach (var (t, w) in opts)
        {
            acc += w;
            if (roll < acc) return t;
        }
        return opts[0].t;
    }

    private enum MidAction { BorderSimple, BorderDouble, TurnShort, TurnLarge, Expand, Retract }
    private MidAction ChooseMidAction(bool isLeft, int tier, int remaining, int curX) => MidAction.BorderSimple;

    // =========================================================================
    // EXTENSION FACE — insérée DANS la colonne au tier demandé
    // =========================================================================

    /// <summary>
    /// Extension face insérée dans la colonne.
    /// Positionnement explicite : chaque élément à extX, extX += dx.
    /// 
    /// Si un retract précède → virage remplace l'angle d'ouverture.
    /// L'épaisseur est réduite au minimum avant l'extension.
    /// </summary>
    private void InsertExtensionInline(ref MountainAnchor cur, ref PieceType last,
                                        ref int built, bool isLeft)
    {
        bool hasCave = _recipe.extensionHasCave;
        int colX = cur.X;
        int dx = isLeft ? -1 : 1;
        int hAngle = MountainPieceData.GetHeight(PieceType.AngleL); // 5
        int hFace = MountainPieceData.GetHeight(PieceType.FaceLeft); // 4

        PieceType bSimple = isLeft ? PieceType.BorderSimpleL : PieceType.BorderSimpleR;
        PieceType angleFace = isLeft ? PieceType.AngleL : PieceType.AngleR;

        // Y de départ = haut du dernier sprite colonne
        int curY = cur.Y + MountainPieceData.GetHeight(last);
        int extX = colX;

        // ── 1. AngleR/L d'ouverture (PostProcessVirages remplacera si nécessaire) ─
        extX += dx;
        int openY = curY - hAngle + 1;
        Add(angleFace, new MountainAnchor(extX, openY));
        curY += 1;
        built += 1;

        // ── 2. FaceLeft (x+dx) ───────────────────────────────────────────────
        extX += dx;
        PieceType face1 = isLeft ? PieceType.FaceRight : PieceType.FaceLeft;
        int faceY = curY - hFace + 1; // haut aligné avec haut de l'ouverture
        Add(face1, new MountainAnchor(extX, faceY));

        // ── 3. FaceRight/Cave (x+dx) ────────────────────────────────────────
        extX += dx;
        PieceType face2raw = isLeft ? PieceType.FaceLeft : PieceType.FaceRight;
        PieceType face2 = hasCave
            ? (isLeft ? PieceType.FaceLeftCave : PieceType.FaceRightCave)
            : face2raw;
        Add(face2, new MountainAnchor(extX, faceY));
        curY += 1;
        built += 1;

        // ── 4. AngleR/L de fermeture (x+dx) ─────────────────────────────────
        extX += dx;
        int closeY = curY - hAngle + 1;
        Add(angleFace, new MountainAnchor(extX, closeY));
        curY += 1;
        built += 1;

        // ── 5. Bords après fermeture ─────────────────────────────────────────
        Add(bSimple, new MountainAnchor(extX, curY));
        curY += 1; built += 1;
        Add(bSimple, new MountainAnchor(extX, curY));
        curY += 1; built += 1;

        // ── 6. Retour progressif vers la colonne ─────────────────────────────
        int rdx = -dx;
        int maxRetract = (_recipe.extensionTier == 4) ? 1 + _rng.Next(2) : 2 + _rng.Next(2);
        bool lastWasAngleTop = false;
        int retractDone = 0;
        PieceType angleTop = isLeft ? PieceType.AngleTopG : PieceType.AngleTopD;

        while (retractDone < maxRetract)
        {
            if (lastWasAngleTop || _rng.Next(2) == 0)
            {
                // Pattern A : AngleTop + Bord
                if (!lastWasAngleTop)
                {
                    Add(angleTop, new MountainAnchor(extX, curY));
                    curY += 1; built += 1;
                }
                extX += rdx;
                Add(bSimple, new MountainAnchor(extX, curY));
                curY += 1; built += 1;
                retractDone++;
                lastWasAngleTop = false;
            }
            else
            {
                // Pattern B : Bord + AngleTop + TopEdge + AngleTop
                Add(bSimple, new MountainAnchor(extX, curY));
                curY += 1; built += 1;

                Add(angleTop, new MountainAnchor(extX, curY));
                extX += rdx;
                Add(PieceType.TopEdge, new MountainAnchor(extX, curY));
                extX += rdx;
                curY += 1;
                Add(angleTop, new MountainAnchor(extX, curY));
                curY += 1; built += 4;
                retractDone += 2;
                lastWasAngleTop = true;
            }

            if (extX <= colX && !isLeft) break;
            if (extX >= colX && isLeft) break;
        }

        // Finir proprement
        if (lastWasAngleTop)
        {
            extX += rdx;
            Add(bSimple, new MountainAnchor(extX, curY));
            curY += 1; built += 1;
        }

        cur = new MountainAnchor(extX, curY - 1);
        last = bSimple;
    }    // =========================================================================
    // TOP — HORIZONTAL : AngleTopG → TopEdge × n (+ cassures) → AngleTopD
    // =========================================================================

    /// <summary>
    /// TOP horizontal avec cassures.
    ///
    /// Structure : AngleTopG → TopEdge×n → [cassures] → TopEdge×n → AngleTopD
    ///
    /// Cassures possibles :
    ///   AngleTopG = monte (y+1), AngleTopD = descend (y-1)
    ///   3 configs : monte seul, monte+descend, descend seul
    ///
    /// Les coins gauche (AngleTopG) et droite (AngleTopD) connectent aux colonnes.
    /// </summary>
    private void BuildTop(MountainAnchor leftAnchor, PieceType leftLast,
                          MountainAnchor rightAnchor, PieceType rightLast)
    {
        // Aligner les colonnes à la même hauteur
        int targetY = Mathf.Max(
            leftAnchor.Y + MountainPieceData.GetHeight(leftLast),
            rightAnchor.Y + MountainPieceData.GetHeight(rightLast));

        FillColumnToHeight(ref leftAnchor, ref leftLast, targetY, true);
        FillColumnToHeight(ref rightAnchor, ref rightLast, targetY, false);

        // Forcer un BorderSimple si la colonne finit par un AngleTop
        // → transition propre vers le coin top
        if (leftLast == PieceType.AngleTopG || leftLast == PieceType.AngleTopD ||
            leftLast == PieceType.AngleL || leftLast == PieceType.AngleR)
        {
            MountainAnchor bL = MountainPieceData.GetAnchorOut(
                leftLast, leftAnchor, PieceType.BorderSimpleL);
            Add(PieceType.BorderSimpleL, bL);
            leftAnchor = bL;
            leftLast = PieceType.BorderSimpleL;
        }
        if (rightLast == PieceType.AngleTopG || rightLast == PieceType.AngleTopD ||
            rightLast == PieceType.AngleL || rightLast == PieceType.AngleR)
        {
            MountainAnchor bR = MountainPieceData.GetAnchorOut(
                rightLast, rightAnchor, PieceType.BorderSimpleR);
            Add(PieceType.BorderSimpleR, bR);
            rightAnchor = bR;
            rightLast = PieceType.BorderSimpleR;
        }

        MountainAnchor leftCur = leftAnchor;
        PieceType leftTypeCur = leftLast;
        MountainAnchor rightCur = rightAnchor;
        PieceType rightTypeCur = rightLast;

        // ── Extension au TOP (tier 4) ────────────────────────────────────────
        if (_recipe.hasExtension && _recipe.extensionTier == 4)
        {
            bool extLeft = _recipe.extensionOnLeft;
            if (extLeft)
            {
                InsertExtensionInline(ref leftCur, ref leftTypeCur, ref _dummyBuilt, true);
                // Réaligner la droite à la même hauteur
                int newTargetY = leftCur.Y + MountainPieceData.GetHeight(leftTypeCur);
                FillColumnToHeight(ref rightCur, ref rightTypeCur, newTargetY, false);
            }
            else
            {
                int dummyBuilt = 0;
                InsertExtensionInline(ref rightCur, ref rightTypeCur, ref dummyBuilt, false);
                // Réaligner la gauche
                int newTargetY = rightCur.Y + MountainPieceData.GetHeight(rightTypeCur);
                FillColumnToHeight(ref leftCur, ref leftTypeCur, newTargetY, true);
            }

            // Forcer un BorderSimple après l'extension pour transition propre
            if (leftTypeCur == PieceType.AngleTopG || leftTypeCur == PieceType.AngleTopD ||
                leftTypeCur == PieceType.AngleL || leftTypeCur == PieceType.AngleR)
            {
                MountainAnchor bL = MountainPieceData.GetAnchorOut(leftTypeCur, leftCur, PieceType.BorderSimpleL);
                Add(PieceType.BorderSimpleL, bL);
                leftCur = bL; leftTypeCur = PieceType.BorderSimpleL;
            }
            if (rightTypeCur == PieceType.AngleTopG || rightTypeCur == PieceType.AngleTopD ||
                rightTypeCur == PieceType.AngleL || rightTypeCur == PieceType.AngleR)
            {
                MountainAnchor bR = MountainPieceData.GetAnchorOut(rightTypeCur, rightCur, PieceType.BorderSimpleR);
                Add(PieceType.BorderSimpleR, bR);
                rightCur = bR; rightTypeCur = PieceType.BorderSimpleR;
            }
        }

        // ── Si le dernier élément colonne gauche est AngleTop, décaler le coin ─
        // Deux AngleTop consécutifs : le 2ème à x+1, y+1
        MountainAnchor atgAnchor;
        if (leftTypeCur == PieceType.AngleTopG || leftTypeCur == PieceType.AngleTopD)
        {
            int topOfLeft = leftCur.Y + MountainPieceData.GetHeight(leftTypeCur);
            atgAnchor = new MountainAnchor(leftCur.X + 1, topOfLeft);
        }
        else
        {
            atgAnchor = MountainPieceData.GetAnchorOut(
                leftTypeCur, leftCur, PieceType.AngleTopG);
        }

        // ── AngleTopG (coin gauche) ──────────────────────────────────────────
        Add(PieceType.AngleTopG, atgAnchor);
        Debug.Log($"[MtnWFC] Coin gauche: AngleTopG à ({atgAnchor.X},{atgAnchor.Y}) " +
                  $"lastCol={leftTypeCur} à ({leftCur.X},{leftCur.Y}) " +
                  $"consecutive={(leftTypeCur == PieceType.AngleTopG || leftTypeCur == PieceType.AngleTopD)}");

        // AngleTopD sera placé APRÈS la boucle TopEdge (pour tenir compte des cassures)
        int rightEndX = rightCur.X;
        int topY = atgAnchor.Y;

        // ── TopEdge entre les deux coins avec cassures ────────────────────────
        int startX = atgAnchor.X + 1;
        int endX = rightEndX;
        int totalWidth = endX - startX;

        if (totalWidth <= 0)
        {
            SommetY = topY + 2;
            return;
        }

        // Choisir la configuration de cassure
        bool allowCassure = _recipe.allowTopBreak && totalWidth >= 6;
        int config = 0; // 0 = pas de cassure
        if (allowCassure && _rng.NextDouble() < _recipe.topBreakChance)
        {
            config = _rng.Next(1, 4); // 1=monte, 2=monte+descend, 3=descend
        }

        int curX = startX;
        int curY = topY;
        int safety = 0;

        switch (config)
        {
            case 0: // Pas de cassure — TopEdge tout droit
                while (curX < endX && safety++ < 50)
                {
                    if (curX >= endX) break; // pas dépasser le coin droit
                    Add(PieceType.TopEdge, new MountainAnchor(curX, curY));
                    curX++;
                }
                break;

            case 1: // Monte (centrée gauche)
                {
                    int cassureX = startX + Mathf.Max(2, 3 + _rng.Next(Mathf.Max(1, totalWidth / 2 - 3)));
                    // Laisser au moins 2 TopEdge après la cassure
                    cassureX = Mathf.Min(cassureX, endX - 3);
                    while (curX < endX && safety++ < 50)
                    {
                        if (curX == cassureX)
                        {
                            Add(PieceType.AngleTopG, new MountainAnchor(curX, curY));
                            curY += 1;
                            curX++;
                            continue;
                        }
                        Add(PieceType.TopEdge, new MountainAnchor(curX, curY));
                        curX++;
                    }
                    break;
                }

            case 2: // Monte puis descend (centrée)
                {
                    int third = Mathf.Max(2, totalWidth / 3);
                    int cassure1X = startX + third;
                    int cassure2X = startX + third * 2;
                    while (curX < endX && safety++ < 50)
                    {
                        if (curX == cassure1X)
                        {
                            // Monte : AngleTopG à x, y+1
                            Add(PieceType.AngleTopG, new MountainAnchor(curX, curY + 1));
                            curY += 1;
                            curX++;
                            continue;
                        }
                        if (curX == cassure2X)
                        {
                            // Descend : AngleTopD à x, y0 (même Y), prochain TopEdge y-1
                            Add(PieceType.AngleTopD, new MountainAnchor(curX, curY));
                            curX++;
                            curY -= 1;
                            continue;
                        }
                        Add(PieceType.TopEdge, new MountainAnchor(curX, curY));
                        curX++;
                    }
                    break;
                }

            case 3: // Descend (centrée droite)
                {
                    int cassureX = startX + Mathf.Max(2, totalWidth / 2);
                    cassureX = Mathf.Min(cassureX, endX - 3);
                    while (curX < endX && safety++ < 50)
                    {
                        if (curX == cassureX)
                        {
                            // Descend : AngleTopD à x, y0, prochain TopEdge y-1
                            Add(PieceType.AngleTopD, new MountainAnchor(curX, curY));
                            curX++;
                            curY -= 1;
                            continue;
                        }
                        Add(PieceType.TopEdge, new MountainAnchor(curX, curY));
                        curX++;
                    }
                    break;
                }
        }

        // ── Coin droit — AngleTopD au Y de la rangée TopEdge ──────────────
        // Le coin droit s'aligne sur curY (Y final des TopEdge après cassures)
        int topOfRightCol = rightCur.Y + MountainPieceData.GetHeight(rightTypeCur);

        // Si la colonne droite n'atteint pas curY, combler avec des bords
        while (topOfRightCol < curY)
        {
            PieceType bR = PieceType.BorderSimpleR;
            MountainAnchor brA = MountainPieceData.GetAnchorOut(
                rightTypeCur, rightCur, bR);
            Add(bR, brA);
            rightCur = brA;
            rightTypeCur = bR;
            topOfRightCol = rightCur.Y + MountainPieceData.GetHeight(rightTypeCur);
        }

        // Forcer BorderSimple avant AngleTopD si dernier = AngleTop
        if (rightTypeCur == PieceType.AngleTopG || rightTypeCur == PieceType.AngleTopD)
        {
            MountainAnchor bRA = MountainPieceData.GetAnchorOut(
                rightTypeCur, rightCur, PieceType.BorderSimpleR);
            Add(PieceType.BorderSimpleR, bRA);
            rightCur = bRA;
            rightTypeCur = PieceType.BorderSimpleR;
        }

        // AngleTopD au Y de la rangée TopEdge
        MountainAnchor atdAnchor = MountainPieceData.GetAnchorOut(
            rightTypeCur, rightCur, PieceType.AngleTopD);
        Add(PieceType.AngleTopD, atdAnchor);

        // Combler entre les TopEdge (curX) et AngleTopD avec des TopEdge
        int gapStartX = curX;
        while (gapStartX < atdAnchor.X)
        {
            Add(PieceType.TopEdge, new MountainAnchor(gapStartX, curY));
            gapStartX++;
        }

        SommetY = Mathf.Max(topY, Mathf.Max(curY, atdAnchor.Y)) + 2;
    }

    /// <summary>
    /// Remplit une colonne jusqu'à targetY avec variété (mêmes règles que BuildColumn).
    /// </summary>
    private void FillColumnToHeight(ref MountainAnchor anchor, ref PieceType lastType,
                                     int targetY, bool isLeft)
    {
        int consecBord = 0;
        bool prevTurn = (lastType == PieceType.TurnShortL || lastType == PieceType.TurnShortR ||
                         lastType == PieceType.TurnLargeL || lastType == PieceType.TurnLargeR);
        bool prevAngle = (lastType == PieceType.AngleL || lastType == PieceType.AngleR ||
                          lastType == PieceType.AngleTopG || lastType == PieceType.AngleTopD);
        int safety = 0;

        while (anchor.Y + MountainPieceData.GetHeight(lastType) < targetY && safety < 60)
        {
            safety++;
            int remaining = targetY - anchor.Y;
            int tier = _recipe.GetTier(0, 0);

            PieceType next;
            PieceType bSafe = isLeft ? PieceType.BorderSimpleL : PieceType.BorderSimpleR;

            // Bloquer expand si au bord de la grille
            bool fillAtEdge = (isLeft && anchor.X <= 0) ||
                              (!isLeft && anchor.X >= _gridWidth - 1);

            if (prevAngle)
            {
                next = bSafe;
                prevAngle = false;
            }
            else if (prevTurn)
            {
                next = bSafe;
                prevTurn = false;
            }
            else
            {
                _currentColumnX = anchor.X; // pour que atGridEdge fonctionne
                next = ChooseMidPiece(lastType, isLeft, tier, remaining,
                                      consecBord, prevTurn, prevAngle);
            }

            // Sécurité : si la prochaine pièce dépasse targetY, forcer BorderSimple
            MountainAnchor nextA = MountainPieceData.GetAnchorOut(lastType, anchor, next);
            if (nextA.Y + MountainPieceData.GetHeight(next) > targetY)
            {
                PieceType bS = isLeft ? PieceType.BorderSimpleL : PieceType.BorderSimpleR;
                next = bS;
                nextA = MountainPieceData.GetAnchorOut(lastType, anchor, next);
                // Si même le BorderSimple dépasse, arrêter
                if (nextA.Y + 1 > targetY) break;
            }

            // Combler les trous
            int curTop = anchor.Y + MountainPieceData.GetHeight(lastType);
            int gap = nextA.Y - curTop;
            if (gap > 0)
            {
                PieceType fill = isLeft ? PieceType.BorderSimpleL : PieceType.BorderSimpleR;
                for (int g = 0; g < gap; g++)
                {
                    MountainAnchor fillA = new MountainAnchor(anchor.X, curTop + g);
                    Add(fill, fillA);
                    anchor = fillA;
                    lastType = fill;
                }
                // Recalculer l'ancrage depuis le dernier bord intercalé
                nextA = MountainPieceData.GetAnchorOut(lastType, anchor, next);
            }

            Add(next, nextA);
            anchor = nextA;
            lastType = next;

            bool isBordS = (next == (isLeft ? PieceType.BorderSimpleL : PieceType.BorderSimpleR));
            bool isBordD = (next == (isLeft ? PieceType.BorderDoubleL : PieceType.BorderDoubleR));
            bool isTurn = (next == PieceType.TurnShortL || next == PieceType.TurnShortR ||
                            next == PieceType.TurnLargeL || next == PieceType.TurnLargeR);
            bool isAngle = (next == PieceType.AngleL || next == PieceType.AngleR ||
                            next == PieceType.AngleTopG || next == PieceType.AngleTopD);

            if (isBordS) consecBord += 1;
            else if (isBordD) consecBord += 2;
            else consecBord = 0;
            prevTurn = isTurn;
            prevAngle = isAngle;
        }
    }

    // =========================================================================
    // POST-TRAITEMENT : LOI VIRAGE
    // =========================================================================

    /// <summary>
    /// Scanne les placements et remplace les compositions par des virages :
    ///   AngleTopG/D + 1 bord + AngleL/R → TurnShort (3 cells)
    ///   AngleTopG/D + 2 bords + AngleL/R → TurnLarge (4 cells)
    ///   AngleTopG/D + 3 bords + AngleL/R → TurnExtraLarge (5 cells)
    /// </summary>
    private void PostProcessVirages()
    {
        int replaced = 0;
        var result = new System.Collections.Generic.List<Placement>();

        int i = 0;
        while (i < Placements.Count)
        {
            var p = Placements[i];

            // Chercher un AngleTop (début de retract)
            bool isRetractL = p.type == PieceType.AngleTopG;
            bool isRetractR = p.type == PieceType.AngleTopD;

            if (isRetractL || isRetractR)
            {
                // Compter les bords qui suivent
                int bordCount = 0;
                int j = i + 1;
                while (j < Placements.Count && IsBorder(Placements[j].type))
                {
                    bordCount++;
                    j++;
                }

                // Vérifier s'il y a un expand (AngleL/R) après les bords
                if (j < Placements.Count && bordCount >= 1 && bordCount <= 3)
                {
                    var expandP = Placements[j];
                    bool isExpandL = expandP.type == PieceType.AngleL;
                    bool isExpandR = expandP.type == PieceType.AngleR;

                    // Vérifier que le retract et l'expand sont du même côté
                    bool sameLeft = isRetractL && isExpandL;
                    bool sameRight = isRetractR && isExpandR;

                    if (sameLeft || sameRight)
                    {
                        bool left = sameLeft;
                        PieceType turnType;

                        if (bordCount == 1)
                            turnType = left ? PieceType.TurnShortL : PieceType.TurnShortR;
                        else if (bordCount == 2)
                            turnType = left ? PieceType.TurnLargeL : PieceType.TurnLargeR;
                        else // bordCount == 3
                            turnType = left ? PieceType.TurnExtraLargeL : PieceType.TurnExtraLargeR;

                        // Le virage se place à la position de l'AngleTop (retract)
                        int turnHeight = MountainPieceData.GetHeight(turnType);

                        // Virage à la position de l'AngleTop (pas de décalage)
                        // Son haut s'aligne naturellement avec le haut de l'expand remplacé
                        int turnY = p.gridY;
                        int turnTopY = turnY + turnHeight - 1;

                        // L'ancien expand (AngleR/L) avait son haut à :
                        var oldExpand = Placements[j];
                        int oldExpandTopY = oldExpand.gridY + MountainPieceData.GetHeight(oldExpand.type) - 1;

                        // Décalage Y pour les faces suivantes
                        int yShift = turnTopY - oldExpandTopY;

                        result.Add(new Placement
                        {
                            type = turnType,
                            gridX = p.gridX,
                            gridY = turnY
                        });

                        // Ajuster les faces qui suivent (si extension)
                        int nextIdx = j + 1;
                        while (nextIdx < Placements.Count)
                        {
                            var nextP = Placements[nextIdx];
                            bool isFace = nextP.type == PieceType.FaceLeft ||
                                          nextP.type == PieceType.FaceRight ||
                                          nextP.type == PieceType.FaceLeftCave ||
                                          nextP.type == PieceType.FaceRightCave;
                            if (!isFace) break;

                            // Ajuster Y des faces
                            result.Add(new Placement
                            {
                                type = nextP.type,
                                gridX = nextP.gridX,
                                gridY = nextP.gridY + yShift
                            });
                            nextIdx++;
                        }

                        replaced++;
                        i = nextIdx; // sauter retract + bords + expand + faces ajustées
                        continue;
                    }
                }
            }

            result.Add(p);
            i++;
        }

        if (replaced > 0)
        {
            Placements.Clear();
            Placements.AddRange(result);
            Debug.Log($"[MtnWFC] LOI VIRAGE : {replaced} composition(s) remplacée(s) par des virages");
        }
    }

    /// <summary>
    /// Après remplacement par virages, comble les trous éventuels
    /// en insérant des BorderSimple là où il y a un gap vertical
    /// entre deux pièces consécutives du même côté.
    /// </summary>
    private void FillGapsAfterVirages()
    {
        var result = new System.Collections.Generic.List<Placement>();
        int filled = 0;

        for (int i = 0; i < Placements.Count; i++)
        {
            result.Add(Placements[i]);

            if (i + 1 < Placements.Count)
            {
                var current = Placements[i];
                var next = Placements[i + 1];

                // Vérifier si les deux pièces sont sur le même côté (même X ou X±1)
                // et s'il y a un gap vertical
                int curTop = current.gridY + MountainPieceData.GetHeight(current.type);
                int gap = next.gridY - curTop;

                // Gap positif = espace vide entre les deux pièces
                if (gap > 0 && gap <= 3 && Mathf.Abs(next.gridX - current.gridX) <= 1)
                {
                    // Déterminer le côté pour le bord de remplissage
                    bool isLeftSide = IsLeftPiece(current.type) || IsLeftPiece(next.type);
                    PieceType fillType = isLeftSide
                        ? PieceType.BorderSimpleL : PieceType.BorderSimpleR;

                    for (int g = 0; g < gap; g++)
                    {
                        result.Add(new Placement
                        {
                            type = fillType,
                            gridX = current.gridX,
                            gridY = curTop + g
                        });
                        filled++;
                    }
                }
            }
        }

        if (filled > 0)
        {
            Placements.Clear();
            Placements.AddRange(result);
            Debug.Log($"[MtnWFC] Gaps comblés : {filled} bord(s) ajouté(s)");
        }
    }

    private bool IsLeftPiece(PieceType t)
    {
        return t == PieceType.AngleL || t == PieceType.AngleTopG ||
               t == PieceType.BorderSimpleL || t == PieceType.BorderDoubleL ||
               t == PieceType.FaceLeft || t == PieceType.FaceLeftCave ||
               t == PieceType.TurnShortL || t == PieceType.TurnLargeL ||
               t == PieceType.TurnExtraLargeL;
    }

    private bool IsBorder(PieceType t)
    {
        return t == PieceType.BorderSimpleL || t == PieceType.BorderSimpleR ||
               t == PieceType.BorderDoubleL || t == PieceType.BorderDoubleR;
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private void Add(PieceType type, MountainAnchor anchor)
    {
        Placements.Add(new Placement
        {
            type = type,
            gridX = Mathf.Clamp(anchor.X, 0, _gridWidth - 1),
            gridY = anchor.Y
        });
    }
}
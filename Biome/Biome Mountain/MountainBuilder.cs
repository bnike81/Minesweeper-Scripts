using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// MountainBuilder — Constructeur centralisé.
/// Place les sprites via ancres et retourne l'ancre suivante.
///
/// USAGE :
///   var anchor = new MountainAnchor(x, y);
///   anchor = builder.Place(PieceType.BorderSimpleL, anchor, PieceType.AngleL);
///   anchor = builder.Place(PieceType.AngleL,        anchor, PieceType.BorderSimpleL);
/// </summary>
public class MountainBuilder
{
    public MountainSprites Sprites => _sprites;

    // Cache sprites slicés — évite Sprite.Create répété → élimine allocations GC
    private static readonly Dictionary<(Texture2D, int, int), Sprite> _sliceCache
        = new Dictionary<(Texture2D, int, int), Sprite>();

    public static void ClearSliceCache() => _sliceCache.Clear();

    private readonly MountainSprites _sprites;
    private readonly float _cs;
    private readonly GridManager _gm;
    private readonly Transform _parent;
    private readonly string _sortingLayer;
    private readonly int _sortingOrder;
    private readonly List<GameObject> _spawned;

    public MountainBuilder(MountainSprites sprites, float cs, GridManager gm,
        Transform parent, string sortingLayer, int sortingOrder,
        List<GameObject> spawned)
    {
        _sprites = sprites;
        _cs = cs;
        _gm = gm;
        _parent = parent;
        _sortingLayer = sortingLayer;
        _sortingOrder = sortingOrder;
        _spawned = spawned;
    }

    // =========================================================================
    // PLACE — pose une pièce et retourne l'ancre pour la pièce suivante
    // =========================================================================

    /// <summary>
    /// Place une pièce à anchorIn et retourne l'ancre calculée pour nextType.
    /// </summary>
    public MountainAnchor Place(PieceType type, MountainAnchor anchorIn,
        PieceType nextType)
    {
        Sprite sprite = _sprites.Get(type);
        int h = MountainPieceData.GetHeight(type);

        if (sprite != null)
        {
            int idxBefore = _spawned.Count;
            PlaceSprite("M", sprite, anchorIn.X, anchorIn.Y, h);

            // ── Cave entrance : détecter les faces cave et attacher le trigger ──
            // FaceLeftCave et FaceRightCave sont les seules pièces qui donnent
            // accès à une grotte. Le trigger est posé sur la tranche dy=0
            // (la plus basse du sprite = la case visible à l'entrée).
            bool isCaveFace = type == PieceType.FaceLeftCave
                           || type == PieceType.FaceRightCave;
            if (isCaveFace && _spawned.Count > idxBefore)
                AttachCaveEntrance(_spawned[idxBefore],
                    anchorIn.X, anchorIn.Y, isBottom: true);
        }

        Reserve(anchorIn.X, anchorIn.Y, h);

        var anchorOut = MountainPieceData.GetAnchorOut(type, anchorIn, nextType);
        return anchorOut;
    }

    /// <summary>
    /// Place une pièce sans connaître la suivante — ancre = coin haut gauche.
    /// </summary>
    public MountainAnchor Place(PieceType type, MountainAnchor anchorIn)
    {
        return Place(type, anchorIn, type); // nextType=type → ancre générique
    }

    /// <summary>
    /// Place directement aux coordonnées X,Y sans ancre.
    /// </summary>
    public void PlaceAt(PieceType type, int gx, int gy)
    {
        Sprite sprite = _sprites.Get(type);
        int h = MountainPieceData.GetHeight(type);

        if (sprite != null)
        {
            int idxBefore = _spawned.Count;
            PlaceSprite("M", sprite, gx, gy, h);

            // Même détection que Place() : faces cave → trigger d'entrée
            bool isCaveFace = type == PieceType.FaceLeftCave
                           || type == PieceType.FaceRightCave;
            if (isCaveFace && _spawned.Count > idxBefore)
                AttachCaveEntrance(_spawned[idxBefore], gx, gy, isBottom: true);
        }

        Reserve(gx, gy, h);
    }

    /// <summary>
    /// Place une paire FaceG+FaceD côte à côte et retourne l'ancre après FaceD.
    /// </summary>
    public MountainAnchor PlaceFacePair(MountainAnchor anchorFaceG,
        bool withCaveG, bool withCaveD, PieceType nextAfterFaceD,
        bool isBottomEntrance = true)
    {
        PieceType typeG = withCaveG ? PieceType.FaceLeftCave : PieceType.FaceLeft;
        PieceType typeD = withCaveD ? PieceType.FaceRightCave : PieceType.FaceRight;

        Sprite fG = _sprites.Get(typeG);
        Sprite fD = _sprites.Get(typeD);

        int gx = anchorFaceG.X;
        int gy = anchorFaceG.Y;

        // ── FaceG ─────────────────────────────────────────────────────────────
        // On mémorise l'index dans _spawned AVANT PlaceSprite.
        // PlaceSprite (h=4) ajoute 4 GOs : index [before] = dy=0 (tranche basse).
        int idxBeforeG = _spawned.Count;
        if (fG != null) PlaceSprite("FaceG_" + gx + "_" + gy, fG, gx, gy, 4);
        if (withCaveG && _spawned.Count > idxBeforeG)
            AttachCaveEntrance(_spawned[idxBeforeG], gx, gy, isBottomEntrance);

        // ── FaceD ─────────────────────────────────────────────────────────────
        int idxBeforeD = _spawned.Count;
        if (fD != null) PlaceSprite("FaceD_" + gx + "_" + gy, fD, gx + 1, gy, 4);
        if (withCaveD && _spawned.Count > idxBeforeD)
            AttachCaveEntrance(_spawned[idxBeforeD], gx + 1, gy, isBottomEntrance);

        Reserve(gx, gy, 4);
        Reserve(gx + 1, gy, 4);

        var anchorFaceD = MountainPieceData.GetAnchorOut(typeG, anchorFaceG, typeD);
        return MountainPieceData.GetAnchorOut(typeD, anchorFaceD, nextAfterFaceD);
    }

    /// <summary>
    /// Attache BoxCollider2D + CaveEntranceTrigger sur la tranche basse d'une face cave.
    /// Enregistre aussi la position dans CaveManager.
    /// </summary>
    private void AttachCaveEntrance(GameObject go, int gridX, int gridY, bool isBottom)
    {
        if (go == null) return;

        if (go.GetComponent<BoxCollider2D>() == null)
        {
            var col = go.AddComponent<BoxCollider2D>();
            col.size = Vector2.one * Mathf.Max(0.1f, _cs - _gm.CellSpacing);
        }

        if (go.GetComponent<CaveEntranceTrigger>() == null)
        {
            var trig = go.AddComponent<CaveEntranceTrigger>();
            trig.Initialize(gridX, gridY, isBottom);
        }

        CaveManager.Instance?.RegisterEntrance(gridX, gridY, isBottom);
        Debug.Log($"[CaveEntrance] Trigger posé sur {go.name} " +
                  $"grid=({gridX},{gridY}) bas={isBottom}");
    }

    /// <summary>
    /// Place directement avec coordonnées X,Y (pour cas spéciaux).
    /// </summary>
    public void PlaceSpriteAt(string id, Sprite sprite, int gx, int gy, int h)
    {
        if (sprite != null) PlaceSprite(id + "_" + gx + "_" + gy, sprite, gx, gy, h);
    }

    // =========================================================================
    // RESERVE
    // =========================================================================

    public void Reserve(int x, int y, int h)
    {
        for (int dy = 0; dy < h; dy++)
        {
            int cy = y + dy;
            if (cy < 0 || cy >= _gm.Height) continue;
            var cell = _gm.GetCell(x, cy);
            if (cell == null) continue;
            _gm.ReserveCircle(x, cy, 0.4f);
            cell.Content = CellContent.Empty;
            cell.IsMountainReserved = true;
            var cv = _gm.GetCellView(x, cy) as MonoBehaviour;
            if (cv != null) cv.gameObject.SetActive(false);
        }
    }

    // =========================================================================
    // PLACE SPRITE — découpe en tranches de 1 case (16×16 px)
    // Chaque tranche est positionnée EXACTEMENT au centre de sa case.
    // Respecte l'espacement vide (_cellSpacing) entre les cases.
    // =========================================================================

    private void PlaceSprite(string id, Sprite sprite, int gx, int gy, int h)
    {
        if (sprite == null) return;

        float cellSize = _cs - _gm.CellSpacing;    // taille visuelle d'une case
        int stepPx = Mathf.RoundToInt(_cs * 16f); // pas en 1/16 d'unité

        float wx = (gx * stepPx) / 16f;          // X centré sur la colonne gx
        float scaleXf = (sprite.bounds.size.x > 0f)
                        ? cellSize / sprite.bounds.size.x
                        : 1f;

        if (h == 1)
        {
            // ── Case unique : placement direct, pas de découpe ──────────────
            var go = new GameObject(id);
            go.transform.SetParent(_parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerName = _sortingLayer;
            sr.sortingOrder = _sortingOrder;

            float wy = (gy * stepPx) / 16f;
            float scaleY = (sprite.bounds.size.y > 0f)
                           ? cellSize / sprite.bounds.size.y
                           : 1f;
            go.transform.localScale = new Vector3(scaleXf, scaleY, 1f);
            go.transform.position = new Vector3(wx, wy, 0f);
            _spawned.Add(go);
        }
        else
        {
            // ── Multi-cases : découpe en h tranches de 1 case ───────────────
            // Chaque tranche est positionnée EXACTEMENT sur son centre de case.
            // Cela respecte l'espacement vide (_cellSpacing) entre les cases.
            int pixH = Mathf.Max(1, Mathf.RoundToInt(sprite.pixelsPerUnit));

            for (int dy = 0; dy < h; dy++)
            {
                var key = (sprite.texture,
                           (int)sprite.rect.y + dy * pixH,
                           pixH);

                if (!_sliceCache.TryGetValue(key, out var slice))
                {
                    var rect = new Rect(
                        sprite.rect.x,
                        sprite.rect.y + dy * pixH,
                        sprite.rect.width,
                        pixH);
                    slice = Sprite.Create(sprite.texture, rect,
                        new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit);
                    _sliceCache[key] = slice;
                }

                var go2 = new GameObject(id + "_" + dy);
                go2.transform.SetParent(_parent, false);
                var sr2 = go2.AddComponent<SpriteRenderer>();
                sr2.sprite = slice;
                sr2.sortingLayerName = _sortingLayer;
                sr2.sortingOrder = _sortingOrder;

                float wy2 = ((gy + dy) * stepPx) / 16f;
                float scaleY2 = (slice.bounds.size.y > 0f)
                                ? cellSize / slice.bounds.size.y
                                : 1f;

                go2.transform.localScale = new Vector3(scaleXf, scaleY2, 1f);
                go2.transform.position = new Vector3(wx, wy2, 0f);
                _spawned.Add(go2);
            }
        }
    }

    /// <summary>
    /// Marque les cases intérieures du plateau montagneux comme réservées.
    /// Scanne de baseY à sommetY. Pour chaque Y, trouve le X gauche et droit
    /// les plus extrêmes puis réserve tout l'intérieur.
    /// Gère correctement les extensions qui élargissent temporairement.
    /// </summary>
    public void ReservePlateau(System.Collections.Generic.List<MountainWFCGenerator.Placement> placements)
    {
        if (placements == null || placements.Count == 0) return;

        // Copier la liste pour éviter les modifications pendant l'itération
        var placementsCopy = new System.Collections.Generic.List<MountainWFCGenerator.Placement>(placements);
        _lastPlacements = placementsCopy;

        // Trouver le leftX et rightX pour chaque Y
        var leftAtY = new System.Collections.Generic.Dictionary<int, int>();
        var rightAtY = new System.Collections.Generic.Dictionary<int, int>();
        int minY = int.MaxValue, maxY = int.MinValue;

        foreach (var p in placementsCopy)
        {
            int h = MountainPieceData.GetHeight(p.type);
            for (int dy = 0; dy < h; dy++)
            {
                int cy = p.gridY + dy;
                if (cy < 0 || cy >= _gm.Height) continue;

                if (!leftAtY.ContainsKey(cy) || p.gridX < leftAtY[cy])
                    leftAtY[cy] = p.gridX;
                if (!rightAtY.ContainsKey(cy) || p.gridX > rightAtY[cy])
                    rightAtY[cy] = p.gridX;

                if (cy < minY) minY = cy;
                if (cy > maxY) maxY = cy;
            }
        }

        // Combler les Y manquants en interpolant entre les Y connus
        // Cela gère les cas où l'extension laisse des trous
        int lastLeft = -1, lastRight = -1;
        for (int cy = minY; cy <= maxY; cy++)
        {
            if (leftAtY.ContainsKey(cy))
                lastLeft = leftAtY[cy];
            else if (lastLeft >= 0)
                leftAtY[cy] = lastLeft;

            if (rightAtY.ContainsKey(cy))
                lastRight = rightAtY[cy];
            else if (lastRight >= 0)
                rightAtY[cy] = lastRight;
        }

        // Marquer les cases intérieures
        int plateauCount = 0;
        foreach (var kvp in leftAtY)
        {
            int cy = kvp.Key;
            int lx = kvp.Value;
            if (!rightAtY.ContainsKey(cy)) continue;
            int rx = rightAtY[cy];

            for (int cx = lx + 1; cx < rx; cx++)
            {
                if (cy < 0 || cy >= _gm.Height) continue;
                if (cx < 0 || cx >= _gm.Width) continue;
                var cell = _gm.GetCell(cx, cy);
                if (cell == null) continue;
                if (!cell.IsMountainReserved)
                {
                    cell.IsMountainReserved = true;
                    cell.IsMountainPlateau = true; // intérieur = HighGrid fog
                    cell.Content = CellContent.Empty;
                    plateauCount++;
                }
            }
        }

        Debug.Log($"[MountainBuilder] Plateau réservé : {plateauCount} cases (Y {minY}-{maxY})");

        // Stocker les limites pour HighGrid
        _plateauLeftAtY = leftAtY;
        _plateauRightAtY = rightAtY;
        _plateauMinY = minY;
        _plateauMaxY = maxY;
    }

    private System.Collections.Generic.Dictionary<int, int> _plateauLeftAtY;
    private System.Collections.Generic.Dictionary<int, int> _plateauRightAtY;
    private int _plateauMinY, _plateauMaxY;

    /// <summary>
    /// Vide les visuels MainGrid sur TOUTES les cases IsMountainReserved.
    /// Scan complet de la grille — aucun trou possible.
    /// </summary>
    public void ClearMainGridPlateau()
    {
        int cleared = 0;
        int viewsHidden = 0;

        for (int x = 0; x < _gm.Width; x++)
            for (int y = 0; y < _gm.Height; y++)
            {
                var cell = _gm.GetCell(x, y);
                if (cell == null || !cell.IsMountainReserved) continue;

                // Vider le contenu (pas d'ennemis, items, etc.)
                cell.Content = CellContent.Empty;
                cleared++;

                if (cell.IsMountainPlateau)
                {
                    // Plateau intérieur : désactiver CellView (HighGrid prend le relais)
                    // PAS de ForceReveal → MainGrid fog reste 100% sur ces cases
                    var view = _gm.GetCellView(x, y);
                    if (view != null && view is MonoBehaviour mb && mb.gameObject != null)
                    {
                        mb.gameObject.SetActive(false);
                        viewsHidden++;
                    }
                }
                // Silhouette : PAS de ForceReveal → FogOfWar couvre normalement
            }

        Debug.Log($"[MountainBuilder] MainGrid plateau vidé : {cleared} cases, {viewsHidden} views cachées");
    }

    private System.Collections.Generic.List<MountainWFCGenerator.Placement> _lastPlacements;

    /// <summary>Enregistre le plateau auprès du HighGrid commun.</summary>
    public void RegisterHighGrid()
    {
        if (_plateauLeftAtY == null || _plateauRightAtY == null) return;

        var hg = HighGrid.Instance;
        if (hg == null)
        {
            Debug.LogWarning("[MountainBuilder] HighGrid.Instance introuvable — " +
                "créer un GO 'HighGrid' dans MainGrid avec le composant HighGrid");
            return;
        }

        hg.RegisterPlateau(_plateauLeftAtY, _plateauRightAtY, _plateauMinY, _plateauMaxY);
        Debug.Log("[MountainBuilder] Plateau enregistré sur HighGrid commun");
    }
}
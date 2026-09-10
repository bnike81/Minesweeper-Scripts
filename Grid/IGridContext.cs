using UnityEngine;

/// <summary>
/// IGridContext — Interface commune à Grid 0 (surface) et Grid -1 (sous-sol).
///
/// HeroController, HeroPathFinder et tous les systèmes de navigation
/// l'utilisent pour fonctionner indépendamment de la couche active.
/// Permet de basculer sans friction entre surface et souterrain.
/// </summary>
public interface IGridContext
{
    // ── Dimensions ────────────────────────────────────────────────────────────
    int Width { get; }
    int Height { get; }
    float CellStep { get; }
    float CellSpacing { get; }
    float CellSize { get; }

    // ── Données ───────────────────────────────────────────────────────────────
    Cell GetCell(int x, int y);
    bool IsInBounds(int x, int y);

    /// <summary>
    /// True si la case est un obstacle infranchissable (mur, ennemi révélé...).
    /// Utilisé par HeroPathFinder pour bloquer la navigation.
    /// Chaque grille l'implémente selon sa logique propre.
    /// </summary>
    bool IsBlocked(int x, int y);

    // ── Révélation ────────────────────────────────────────────────────────────
    RevealResult RevealCell(int x, int y);

    // ── Coordonnées monde ─────────────────────────────────────────────────────
    /// <summary>Coordonnée monde du centre de la case (gx, gy).</summary>
    Vector3 GridToWorld(int gx, int gy, float z = 0f);

    /// <summary>
    /// Convertit une position monde en coordonnées de grille.
    /// Chaque implémentation utilise sa propre méthode (Tilemap.WorldToCell, division, etc.).
    /// Utilisé par HeroController.Update() pour le clic.
    /// </summary>
    Vector2Int WorldToGrid(Vector3 worldPos);
}

/// <summary>
/// GridLayer — Couche de jeu active.
/// </summary>
public enum GridLayer
{
    Surface = 0,   // Grid 0 — forêt/montagne/plage
    Indoor = 1,   // Intérieurs bâtiments
    Underground = -1,   // Grid -1 — grottes, mines
    Highland = 2,   // Grid +1 — plateaux [futur]
}

/// <summary>
/// ActiveGrid — Service locator : retourne la grille actuellement active.
/// Priorité : Indoor → Underground → Surface (Grid 0).
/// HeroController, HeroPathFinder et HeroJump l'utilisent pour naviguer
/// sans savoir sur quelle couche ils se trouvent.
/// </summary>
public static class ActiveGrid
{
    public static IGridContext Current
    {
        get
        {
            // 1. Intérieur bâtiment
            var im = IndoorManager.Instance;
            if (im != null && im.IsIndoor)
            {
                // IsIndoor=true garantit que la grille est prête — pas besoin de IsGenerated
                var ig = im.ActiveIndoor;
                if (ig != null) return ig;
            }
            // 2. Sous-sol (grotte)
            var cm = CaveManager.Instance;
            if (cm != null && cm.IsInCave)
            {
                var ug = UndergroundGrid.Instance;
                if (ug != null && ug.IsGenerated) return ug;
            }
            // 3. Surface (Grid 0)
            return GridManager.Instance;
        }
    }

    public static GridLayer Layer
    {
        get
        {
            if (IndoorManager.Instance?.IsIndoor == true) return GridLayer.Indoor;
            if (CaveManager.Instance?.IsInCave == true) return GridLayer.Underground;
            return GridLayer.Surface;
        }
    }
}
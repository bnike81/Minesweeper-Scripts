// ============================================================================
// TerrainType + TerrainRules — chantier Stage Arena, etape 1
//
// Le terrain decrit ce qu EST la case, independamment de son contenu
// (ennemi, piege) et de son etat (cachee / revelee).
//
// V1 : quatre terrains. Glace, Brule... viendront avec le chantier elements.
// ============================================================================

public enum TerrainType
{
    Sol = 0,         // marchable, revelable — plaine, sable, sol de grotte
    Eau = 1,         // obstacle — mer, cote
    Montagne = 2,    // obstacle — cases reservees par un segment de montagne
    MurGrotte = 3    // obstacle — mur ou pilier de grotte
}

public static class TerrainRules
{
    /// <summary>True = on ne peut ni marcher dessus, ni reveler la case.</summary>
    public static bool IsObstacle(TerrainType terrain) => terrain != TerrainType.Sol;

    /// <summary>
    /// Pose le terrain sur une case ET renseigne les anciens booleens de Cell.
    ///
    /// Pourquoi les anciens booleens : HeroPathfinder, MinesweeperLogic.FloodFill,
    /// HeroController et les vues de biome testent IsCoastWater / IsMountainReserved.
    /// En les renseignant ici, tout ce code reagit au terrain sans modification.
    ///
    /// MurGrotte utilise IsMountainReserved : meme sens pour le code existant
    /// (case reservee, infranchissable, jamais revelee par le flood fill).
    /// </summary>
    public static void Apply(Cell cell, TerrainType terrain)
    {
        if (cell == null) return;
        cell.Terrain = terrain;
        cell.IsCoastWater = terrain == TerrainType.Eau;
        cell.IsMountainReserved = terrain == TerrainType.Montagne
                               || terrain == TerrainType.MurGrotte;
    }
}
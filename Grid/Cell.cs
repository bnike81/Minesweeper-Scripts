using UnityEngine;

/// <summary>
/// Cell — Données pures d'une cellule de la grille.
/// Modèle de données (pas de MonoBehaviour).
/// Contient tout ce qui définit une case : position, contenu, état, biome.
/// </summary>
[System.Serializable]
public class Cell
{
    // ─── Position ─────────────────────────────────────────────────────────────
    public int X { get; private set; }
    public int Y { get; private set; }

    // ─── Contenu & État ───────────────────────────────────────────────────────
    public CellContent Content { get; set; }
    public CellState State { get; private set; }
    public BiomeType Biome { get; set; }

    // ─── Chiffre d'adjacence (1-8, 0 = aucun) ────────────────────────────────
    public int AdjacentDangerCount { get; set; }

    // ─── Flags ────────────────────────────────────────────────────────────────
    public bool IsEnemy => (int)Content >= 10 && (int)Content < 20;
    public bool IsTreasure => (int)Content >= 20 && (int)Content < 30;
    public bool IsTrap => Content == CellContent.Trap;
    public bool IsBoss => Content == CellContent.Enemy_Boss;
    public bool IsDangerous => IsEnemy || IsTrap;
    public bool IsEmpty => Content == CellContent.Empty;
    public bool IsNumber => Content == CellContent.Number;
    public bool IsHidden => State == CellState.Hidden;
    public bool IsRevealed => State == CellState.Revealed;
    public bool IsMountainReserved { get; set; } = false;
    /// <summary>True = intérieur plateau (HighGrid fog). False = silhouette (MainGrid fog).</summary>
    public bool IsMountainPlateau { get; set; } = false;
    public bool IsFlagged => State == CellState.Flagged;

    // ─── Danger (pour génération et logique) ──────────────────────────────────
    public bool CountsAsAdjacentDanger => IsDangerous || IsBoss;

    // ─── XP accordée à la révélation ──────────────────────────────────────────
    public int XPValue
    {
        get
        {
            if (IsEmpty) return 1;
            if (IsNumber) return 0; // XP accordée seulement pour les cases vides/ennemis
            return Content switch
            {
                CellContent.Enemy_Wolf => 5,
                CellContent.Enemy_Mercenary => 7,
                CellContent.Enemy_BanditSword => 7,
                CellContent.Enemy_BanditArcher => 8,
                CellContent.Enemy_Bear => 12,
                CellContent.Enemy_CoralReef => 8,
                CellContent.Enemy_Boss => 100,
                CellContent.Treasure_Campfire => 10,
                CellContent.Treasure_Flower => 10,
                CellContent.Treasure_Chest => 15,
                CellContent.Treasure_Fountain => 20,
                CellContent.Treasure_Scroll => 12,
                CellContent.Trap => 3,
                _ => 0
            };
        }
    }

    // ─── Dégâts infligés si révélé ────────────────────────────────────────────
    public int DamageOnReveal
    {
        get
        {
            return Content switch
            {
                CellContent.Enemy_Wolf => 1,
                CellContent.Enemy_Mercenary => 1,
                CellContent.Enemy_BanditSword => 1,
                CellContent.Enemy_BanditArcher => 1,
                CellContent.Enemy_CoralReef => 1,
                CellContent.Enemy_Bear => 2,
                CellContent.Enemy_Boss => 3,
                CellContent.Trap => 1,
                _ => 0
            };
        }
    }

    // ─── Constructeur ─────────────────────────────────────────────────────────
    public Cell(int x, int y, BiomeType biome = BiomeType.Forest)
    {
        X = x;
        Y = y;
        Biome = biome;
        Content = CellContent.Empty;
        State = CellState.Hidden;
        AdjacentDangerCount = 0;
    }

    // ─── Mutations d'état ─────────────────────────────────────────────────────
    public void Reveal()
    {
        if (State == CellState.Flagged) return; // Ne peut pas révéler une case flaggée
        State = CellState.Revealed;
    }

    public void ForceReveal()
    {
        State = CellState.Revealed; // Révélation forcée (capacité spéciale, fin de partie)
    }

    public void ToggleFlag()
    {
        if (State == CellState.Hidden) State = CellState.Flagged;
        else if (State == CellState.Flagged) State = CellState.Hidden;
    }

    public void Reset()
    {
        Content = CellContent.Empty;
        State = CellState.Hidden;
        AdjacentDangerCount = 0;
    }

    // ─── Debug ────────────────────────────────────────────────────────────────
    public override string ToString()
        => $"Cell({X},{Y}) [{Biome}] {Content} | {State} | adj:{AdjacentDangerCount}";
}
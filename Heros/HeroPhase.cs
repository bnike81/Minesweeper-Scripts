/// <summary>
/// HeroPhase — Enumeration des etats possibles du heros.
///
/// Le heros est TOUJOURS dans EXACTEMENT UN de ces etats a l instant T.
/// Les combinaisons impossibles sont donc impossibles par construction :
///   Moving + Attacking ne peut pas exister.
///   Jumping + Flinching ne peut pas exister.
///
/// Chaque transition est validee par HeroStateMachine.TryTransitionTo.
///
/// REMPLACE a terme les flags croises de HeroState :
///   IsMoving, IsCalculating, IsUILocked, IsJumping, CollectOnArrival, etc.
/// </summary>
public enum HeroPhase
{
    /// <summary>Pas encore apparu dans le monde (premier clic pas encore eu lieu).</summary>
    NotAppeared,

    /// <summary>Libre — peut cliquer, bouger, attaquer. Etat par defaut.</summary>
    Idle,

    /// <summary>Pathfinder tourne (RunAStar en cours, cherche un chemin).</summary>
    Calculating,

    /// <summary>Deplacement fluide sur la grille (MoveAlongSegments).</summary>
    Moving,

    /// <summary>Saut au-dessus d un obstacle (HeroJump).</summary>
    Jumping,

    /// <summary>ApproachAndReveal — marche vers un launchpad puis reveal.</summary>
    Revealing,

    /// <summary>Attaque en cours (Draw → Attack → Sheath).</summary>
    Attacking,

    /// <summary>Encaisse un coup d ennemi (PlayHitAnimation).</summary>
    Flinching,

    /// <summary>UI modale ouverte (pickup, dialogue, boutique, feu de camp).</summary>
    UILocked,

    /// <summary>Piege actif — se debattre.</summary>
    Trapped,

    /// <summary>HP = 0, game over.</summary>
    Dead,
}
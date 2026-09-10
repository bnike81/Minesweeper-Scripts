// GameEnums - Centralise tous les enums du projet.

// Etat global du jeu
public enum GameState
{
    MainMenu, ChapterSelect, Playing, Paused,
    LevelTransition, BossFight, GameOver, Victory, MetaShop
}

// Type de biome
public enum BiomeType
{
    None = 0, Forest = 1, Cave = 2, Beach = 3, Desert = 4, BossArena = 99
}

// Contenu d'une cellule
public enum CellContent
{
    Empty = 0,
    Number = 1,
    Enemy_Wolf = 10,
    Enemy_Bear = 11,
    Enemy_Mercenary = 12,
    Enemy_BanditSword = 13,
    Enemy_BanditArcher = 14,
    Enemy_CoralReef = 15,
    Enemy_Spider = 16,
    Enemy_Bat = 17,
    Enemy_GoblinLance = 18,
    Enemy_GoblinMasse = 19,
    Enemy_Boss = 99,
    Treasure_Chest = 20,
    Treasure_Campfire = 21,
    Treasure_Flower = 22,
    Treasure_Fountain = 23,
    Treasure_Scroll = 24,
    Trap = 30
}

// Etat visuel d'une cellule
public enum CellState
{
    Hidden, Revealed, Flagged, Questioned
}

// Type d'ennemi
public enum EnemyType
{
    Wolf, Bear, Mercenary, BanditSword, BanditArcher, CoralReef,
    Spider, Bat, GoblinLance, GoblinMasse,
    Boss
}

// Type de tresor
public enum TreasureType
{
    Chest, Campfire, Flower, Fountain, Scroll
}

// Capacites joueur
public enum AbilityType
{
    BasicFlag, ChordClick, ProbabilityDisplay, ZoneScan,
    EnemyRadar, MultiFlag, Compass, StoneAmulet,
    HunterScope, InvisibilityCloak, Pickaxe
}

// Direction
public enum Direction { Up, Down, Left, Right, UpLeft, UpRight, DownLeft, DownRight }

// Niveau de rarete
public enum Rarity { Common, Uncommon, Rare, Epic, Legendary }

// Resultat d'une revelation
public enum RevealResult
{
    Empty, Number, EnemyHit, TreasureFound,
    TrapTriggered, BossFound, AlreadyRevealed, Flagged
}

// Type d'objet inventaire
public enum ItemType
{
    Currency, Consumable, Material, QuestItem
}

// Identifiant unique de chaque objet
public enum ItemID
{
    None = 0,
    PieceOr = 1,
    Pomme = 2,
    Branche = 3,
    SteackCru = 4,
    SteackCuit = 5,
    PeauAnimal = 6,
    Fromage = 7,
    Fleur = 8,
    Parchemin = 9,
}

// Slot d'equipement
public enum EquipSlot
{
    Weapon, Armor, Helmet, Ring
}

// Stats bonus equipement
public enum StatBonus
{
    AttackDamage, Defense, MaxHP, XPBonus, LootChance
}

// Type de notification HUD
public enum NotificationType
{
    Info, Item, Damage, LevelUp, Warning, XP
}
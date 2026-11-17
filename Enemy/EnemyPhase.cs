public enum EnemyPhase
{
    NotSpawned,
    Idle,
    Alert,          // Indicateur "!" au dessus de la tete
    Moving,
    Attacking,
    Flinching,
    LostSight,      // Indicateur "?" (hero hors perception)
    Dead
}
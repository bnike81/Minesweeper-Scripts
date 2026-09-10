using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// EnemySpawnTable - ScriptableObject definissant les probabilites
/// de spawn de chaque type d ennemi et de trap par niveau de grille.
/// 
/// SETUP :
///   Project -> Create -> MinesweeperRPG -> Enemy Spawn Table
///   Assigner sur GridManager dans le champ Spawn Table
/// 
/// EXTENSIBLE :
///   Ajouter une entree LevelSpawnConfig pour chaque nouveau niveau.
///   Les biomes futurs peuvent avoir leur propre SpawnTable.
/// </summary>
[CreateAssetMenu(fileName = "EnemySpawnTable",
    menuName = "MinesweeperRPG/Enemy Spawn Table")]
public class EnemySpawnTable : ScriptableObject
{
    public static EnemySpawnTable Instance { get; private set; }

    [SerializeField] private LevelSpawnConfig[] _levels;

    private void OnEnable() => Instance = this;

    /// <summary>Retourne la config du niveau demande (ou le dernier si depasse)</summary>
    public LevelSpawnConfig GetConfig(int level)
    {
        if (_levels == null || _levels.Length == 0) return GetDefault();
        int idx = Mathf.Clamp(level - 1, 0, _levels.Length - 1);
        return _levels[idx];
    }

    private LevelSpawnConfig GetDefault()
    {
        return new LevelSpawnConfig
        {
            levelName = "Default",
            entries = new SpawnEntry[]
            {
                new SpawnEntry { content = CellContent.Trap,            weight = 30 },
                new SpawnEntry { content = CellContent.Enemy_Wolf,      weight = 40 },
                new SpawnEntry { content = CellContent.Enemy_BanditSword,    weight = 20 },
                new SpawnEntry { content = CellContent.Enemy_Bear,      weight = 10 },
            }
        };
    }
}

// ─── Structures ──────────────────────────────────────────────────────────────

[System.Serializable]
public class LevelSpawnConfig
{
    [Tooltip("Nom du niveau (ex: Foret, Montagne...)")]
    public string levelName = "Niveau";

    [Tooltip("Entrees de spawn - les poids sont relatifs (pas besoin que = 100)")]
    public SpawnEntry[] entries;

    /// <summary>Tire un CellContent aleatoire selon les poids</summary>
    public CellContent Roll()
    {
        if (entries == null || entries.Length == 0)
            return CellContent.Enemy_Wolf;

        float total = 0f;
        foreach (var e in entries) total += Mathf.Max(0, e.weight);
        if (total <= 0) return CellContent.Enemy_Wolf;

        float roll = Random.Range(0f, total);
        float cumul = 0f;
        foreach (var e in entries)
        {
            cumul += Mathf.Max(0, e.weight);
            if (roll <= cumul) return e.content;
        }
        return entries[entries.Length - 1].content;
    }

    /// <summary>Retourne true si cette config contient des traps</summary>
    public bool HasTraps()
    {
        if (entries == null) return false;
        foreach (var e in entries)
            if (e.content == CellContent.Trap && e.weight > 0) return true;
        return false;
    }

    /// <summary>Poids total des traps (pour calculer le ratio)</summary>
    public float TrapWeight()
    {
        float w = 0f;
        if (entries == null) return w;
        foreach (var e in entries)
            if (e.content == CellContent.Trap) w += e.weight;
        return w;
    }

    /// <summary>Poids total de tout sauf les traps</summary>
    public float EnemyWeight()
    {
        float w = 0f;
        if (entries == null) return w;
        foreach (var e in entries)
            if (e.content != CellContent.Trap) w += e.weight;
        return w;
    }
}

[System.Serializable]
public class SpawnEntry
{
    [Tooltip("Type de contenu a spawner")]
    public CellContent content = CellContent.Enemy_Wolf;

    [Tooltip("Poids relatif (ex: 40 = 40%)")]
    [Range(0, 100)]
    public float weight = 25f;
}
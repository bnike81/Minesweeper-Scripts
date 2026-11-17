using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// StagePalette — liste ordonnee de stages, associee aux touches F1 a F9.
///
/// Creer : Project → clic droit → Create → Arena → Stage Palette.
/// Glisser l asset dans CombatArena.stagePalette.
/// Le stage n°1 de la liste se charge avec F1, le n°2 avec F2, etc.
/// </summary>
[CreateAssetMenu(menuName = "Arena/Stage Palette", fileName = "StagePalette")]
public class StagePalette : ScriptableObject
{
    [Tooltip("Stages accessibles par F1 a F9 pendant Play (les 9 premiers).")]
    public List<StageConfig> stages = new List<StageConfig>();

    public StageConfig Get(int index)
    {
        if (stages == null || index < 0 || index >= stages.Count) return null;
        return stages[index];
    }
}
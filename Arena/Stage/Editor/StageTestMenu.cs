#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// StageTestMenu — crée cinq stages de test et leur palette (étape 2).
///
/// Menu Unity : MinesweeperRPG → Stages → Creer les stages de test
/// Résultat : dossier Assets/Stages avec 5 StageConfig et 1 StagePalette.
/// Relancer le menu remet les stages de test dans leur état d origine.
///
/// CE FICHIER DOIT ÊTRE DANS UN DOSSIER NOMMÉ « Editor ».
///
/// Les stages n utilisent que le loup (w) et le bandit épée (s), présents dans
/// l EnemyDatabase. La légende ASCII est en tête de StageConfig.cs.
/// </summary>
public static class StageTestMenu
{
    private const string Folder = "Assets/Stages";

    private struct TestStage
    {
        public string file;
        public string displayName;
        public string notes;
        public BiomeType biome;
        public string ascii;
    }

    private static readonly TestStage[] Stages =
    {
        new TestStage
        {
            file = "Stage_Test1_Clairiere",
            displayName = "Forêt — clairière 10x10",
            notes = "Clairière ouverte, un bandit visible, deux loups cachés sous les arbres.",
            biome = BiomeType.Forest,
            ascii =
                "TTTTTTTTTT\n" +
                "TTTWTTTTTT\n" +
                "TTTTTTTTTT\n" +
                "TT......TT\n" +
                "T........T\n" +
                "T....s...T\n" +
                "T.......WT\n" +
                "TT......TT\n" +
                "TTT.@..TTT\n" +
                "TTTTTTTTTT"
        },
        new TestStage
        {
            file = "Stage_Test2_ForetDense",
            displayName = "Forêt dense 10x15",
            notes = "Presque tout est à révéler. Quatre ennemis cachés.",
            biome = BiomeType.Forest,
            ascii =
                "TTTTTTTTTT\n" +
                "TTTTTSTTTT\n" +
                "TTTTTTTTTT\n" +
                "TTWTTTTTTT\n" +
                "TTTTTTTTTT\n" +
                "TTTTTTTWTT\n" +
                "TTTTTTTTTT\n" +
                "TTTTTTTTTT\n" +
                "TTTTSTTTTT\n" +
                "TTTTTTTTTT\n" +
                "TTTTTTTTTT\n" +
                "TTT....TTT\n" +
                "TT......TT\n" +
                "TT..@...TT\n" +
                "TTT....TTT"
        },
        new TestStage
        {
            file = "Stage_Test3_Plage",
            displayName = "Plage — bout de côte 15x10",
            notes = "Mer à droite, palmiers à gauche. Un loup visible, un bandit caché.",
            biome = BiomeType.Beach,
            ascii =
                "TTTTTTTT...~~~~\n" +
                "TTTSTT.....~~~~\n" +
                "TTTTT.......~~~\n" +
                "TTT.........~~~\n" +
                "TT....w......~~\n" +
                "TT...........~~\n" +
                "TTT.........~~~\n" +
                "TTTT...@...~~~~\n" +
                "TTTTT.....~~~~~\n" +
                "TTTTTTT..~~~~~~"
        },
        new TestStage
        {
            file = "Stage_Test4_Grotte",
            displayName = "Grotte — couloirs 15x15",
            notes = "Salle du bas découverte avec piliers. Deux salles du haut cachées, " +
                    "reliées par deux portes. Un bandit et un loup cachés.",
            biome = BiomeType.Cave,
            ascii =
                "###############\n" +
                "#??????#??????#\n" +
                "#?S????#???W??#\n" +
                "#??????#??????#\n" +
                "#???#??#??#???#\n" +
                "#??????...????#\n" +
                "###.#######.###\n" +
                "#.............#\n" +
                "#..#.......#..#\n" +
                "#.............#\n" +
                "#.....###.....#\n" +
                "#.............#\n" +
                "#..#...@...#..#\n" +
                "#.............#\n" +
                "###############"
        },
        new TestStage
        {
            file = "Stage_Test5_Col",
            displayName = "Col de montagne 15x15",
            notes = "Couloir entre deux montagnes (provisoires, en cases pleines). " +
                    "Un bandit visible dans le col, un loup caché au fond.",
            biome = BiomeType.Forest,
            ascii =
                "^^^^^TTTTT^^^^^\n" +
                "^^^^^TTWTT^^^^^\n" +
                "^^^^^TTTTT^^^^^\n" +
                "^^^^^.....^^^^^\n" +
                "^^^^^.....^^^^^\n" +
                "^^^^..s....^^^^\n" +
                "^^^^.......^^^^\n" +
                "^^^.........^^^\n" +
                "^^^.........^^^\n" +
                "^^...........^^\n" +
                "^^...........^^\n" +
                "T.............T\n" +
                "TT.....@.....TT\n" +
                "TTT.........TTT\n" +
                "TTTTT.....TTTTT"
        }
    };

    [MenuItem("MinesweeperRPG/Stages/Creer les stages de test")]
    public static void CreateTestStages()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets", "Stages");

        var created = new List<StageConfig>();
        foreach (var def in Stages)
        {
            string path = Folder + "/" + def.file + ".asset";
            var stage = AssetDatabase.LoadAssetAtPath<StageConfig>(path);
            if (stage == null)
            {
                stage = ScriptableObject.CreateInstance<StageConfig>();
                AssetDatabase.CreateAsset(stage, path);
            }

            string error;
            if (!stage.LoadFromAscii(def.ascii, def.biome, out error))
            {
                Debug.LogError("[StageTestMenu] " + def.file + " : " + error);
                continue;
            }

            stage.displayName = def.displayName;
            stage.notes = def.notes;
            stage.asciiSource = def.ascii;
            EditorUtility.SetDirty(stage);
            created.Add(stage);
        }

        string palettePath = Folder + "/StagePalette_Test.asset";
        var palette = AssetDatabase.LoadAssetAtPath<StagePalette>(palettePath);
        if (palette == null)
        {
            palette = ScriptableObject.CreateInstance<StagePalette>();
            AssetDatabase.CreateAsset(palette, palettePath);
        }
        palette.stages = created;
        EditorUtility.SetDirty(palette);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = palette;
        EditorGUIUtility.PingObject(palette);

        Debug.Log("<color=#66DDFF>[StageTestMenu]</color> " + created.Count + " stages de test créés dans "
                  + Folder + ". Glisser 'StagePalette_Test' dans CombatArena → Stage Palette, "
                  + "puis F1 à F" + created.Count + " en Play.");
    }
}
#endif
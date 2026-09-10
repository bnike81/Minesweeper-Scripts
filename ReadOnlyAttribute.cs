using UnityEngine;

/// <summary>
/// ReadOnlyAttribute — Affiche un champ en lecture seule dans l'Inspector.
/// Défini ici pour être accessible dans TOUS les contextes (Editor ET Build).
/// </summary>
public class ReadOnlyAttribute : PropertyAttribute { }

#if UNITY_EDITOR
[UnityEditor.CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
public class ReadOnlyDrawer : UnityEditor.PropertyDrawer
{
    public override void OnGUI(
        UnityEngine.Rect position,
        UnityEditor.SerializedProperty property,
        UnityEngine.GUIContent label)
    {
        bool prev = UnityEngine.GUI.enabled;
        UnityEngine.GUI.enabled = false;
        UnityEditor.EditorGUI.PropertyField(position, property, label);
        UnityEngine.GUI.enabled = prev;
    }
}
#endif
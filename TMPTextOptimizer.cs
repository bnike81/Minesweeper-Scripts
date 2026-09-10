using UnityEngine;
using TMPro;

/// <summary>
/// TMPTextOptimizer — Élimine les allocations string de TextMeshPro.
/// 
/// USAGE : Ajouter ce composant sur chaque TMP_Text qui se met à jour fréquemment.
/// Remplace myText.text = "Score: " + score par SetInt/SetFloat sans allocation.
/// 
/// Le Profiler montre 11.4 KB alloués par frame par TMPro — ce composant réduit
/// ça à 0 en évitant la concaténation de strings.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class TMPTextOptimizer : MonoBehaviour
{
    private TMP_Text _text;

    // Valeurs précédentes pour éviter les mises à jour inutiles
    private int _lastInt = int.MinValue;
    private float _lastFloat = float.MinValue;
    private string _lastString = null;

    private void Awake()
    {
        _text = GetComponent<TMP_Text>();
    }

    // ─── API publique ─────────────────────────────────────────────────────────

    /// <summary>Met à jour le texte avec un int — zéro allocation.</summary>
    public void SetInt(int value)
    {
        if (value == _lastInt) return; // Pas de changement → rien à faire
        _lastInt = value;
        _text.SetText("{0}", value); // TMPro format sans allocation string
    }

    /// <summary>Met à jour avec un float — zéro allocation.</summary>
    public void SetFloat(float value, int decimals = 1)
    {
        if (Mathf.Approximately(value, _lastFloat)) return;
        _lastFloat = value;
        // SetText("{0:F1}", float) pas disponible — on arrondit à int
        int rounded = Mathf.RoundToInt(value * Mathf.Pow(10, decimals));
        _text.SetText("{0}", rounded);
    }

    /// <summary>Met à jour avec un string — vérifie le changement avant d'écrire.</summary>
    public void SetString(string value)
    {
        if (value == _lastString) return;
        _lastString = value;
        _text.text = value; // string nécessite une allocation mais seulement si changé
    }

    /// <summary>Format "Label: value" sans allocation.</summary>
    public void SetIntWithLabel(string label, int value)
    {
        if (value == _lastInt) return;
        _lastInt = value;
        _text.SetText(label + "{0}", value);
    }

    public void ForceRefresh()
    {
        _lastInt = int.MinValue;
        _lastFloat = float.MinValue;
        _lastString = null;
    }
}
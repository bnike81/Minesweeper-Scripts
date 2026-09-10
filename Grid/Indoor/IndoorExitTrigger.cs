using UnityEngine;

/// <summary>
/// IndoorExitTrigger — Zone de sortie placée devant la porte de la cabane.
///
/// SETUP dans Indoor scene :
///   Créer un GO vide "DoorExit" positionné sur la tuile porte du tilemap.
///   Ajouter BoxCollider2D (isTrigger = true) dimensionné à 1×1 tuile.
///   Ajouter ce script.
///
/// Quand le héros marche sur cette zone → SceneLoader.ExitIndoor().
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class IndoorExitTrigger : MonoBehaviour
{
    [SerializeField] private string _buildingName = "Cabane du Berger";

    private void Awake()
    {
        var col = GetComponent<BoxCollider2D>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Hero") && other.GetComponent<HeroController>() == null)
            return;

        if (IndoorManager.Instance?.IsIndoor == true)
        {
            Debug.Log($"[IndoorExitTrigger] Héros sort de {_buildingName}");
            IndoorManager.Instance.ExitBuilding(0, 0);
        }
    }

    private void OnMouseEnter() => TooltipUI.Show($"Sortir de {_buildingName}");
    private void OnMouseExit() => TooltipUI.Hide();
}
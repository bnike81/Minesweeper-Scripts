/// <summary>
/// Interface pour les cellules qui ont un layer d'arbres.
/// Permet à GridManager de setter le type sans connaître la classe concrète.
/// </summary>
public interface ITreeLayer
{
    void SetTreeLayer(ForestCellView.TreeLayerType type);
}
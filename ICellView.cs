/// <summary>
/// Interface commune à CellView et CellViewBase.
/// Permet à GridManager de gérer les deux sans connaître la classe.
/// </summary>
public interface ICellView
{
    int X { get; }
    int Y { get; }
    void Refresh();
    void Initialize(Cell cell);
}
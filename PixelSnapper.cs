using UnityEngine;

/// <summary>
/// Snappe un GameObject au pixel grid à chaque frame.
/// Attacher sur les objets qui dérivent (fog, grille, etc.)
/// </summary>
public class PixelSnapper : MonoBehaviour
{
    [SerializeField] private float _ppu = 16f;

    private Vector3 _originalPos;

    private void LateUpdate()
    {
        _originalPos = transform.position;
        var p = _originalPos;
        p.x = Mathf.Round(p.x * _ppu) / _ppu;
        p.y = Mathf.Round(p.y * _ppu) / _ppu;
        transform.position = p;
    }
}
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// CellViewCuller — Désactive les SpriteRenderers hors caméra.
/// Cache les références SpriteRenderer — pas de GetComponent par frame.
/// Réduit les Draw Calls de ~815 à ~200 (seulement la zone visible).
/// </summary>
public class CellViewCuller : MonoBehaviour
{
    public static CellViewCuller Instance { get; private set; }

    [SerializeField] private int _margin = 2;
    [SerializeField] private int _updateEveryNFrames = 4;

    private GridManager _gm;
    private Camera _cam;
    private int _frameCount;

    // Cache SpriteRenderer[x,y] — rempli une fois par SpawnOneCellView
    private SpriteRenderer[,] _renderers;

    // Dernière zone visible
    private int _lastMinX = -1, _lastMaxX = -1, _lastMinY = -1, _lastMaxY = -1;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _gm = GridManager.Instance;
        _cam = Camera.main;
    }

    /// <summary>
    /// Appelé par GridManager.SpawnOneCellView pour enregistrer le SpriteRenderer.
    /// Évite GetComponent() par frame.
    /// </summary>
    public void RegisterCell(int x, int y, SpriteRenderer sr)
    {
        if (_renderers == null && _gm != null)
        {
            int maxH = _gm.Width > 0
                ? Mathf.Max(_gm.Height, 200)
                : 200;
            _renderers = new SpriteRenderer[_gm.Width, maxH];
        }
        if (_renderers != null &&
            x >= 0 && x < _renderers.GetLength(0) &&
            y >= 0 && y < _renderers.GetLength(1))
            _renderers[x, y] = sr;
    }

    public void UnregisterCell(int x, int y)
    {
        if (_renderers != null &&
            x >= 0 && x < _renderers.GetLength(0) &&
            y >= 0 && y < _renderers.GetLength(1))
            _renderers[x, y] = null;
    }

    public void ResetCache()
    {
        _renderers = null;
        _lastMinX = _lastMaxX = _lastMinY = _lastMaxY = -1;
    }

    private void LateUpdate()
    {
        _frameCount++;
        if (_frameCount % _updateEveryNFrames != 0) return;
        if (_gm == null || _cam == null || _renderers == null) return;

        float step = _gm.CellStep;
        Vector3 botLeft = _cam.ViewportToWorldPoint(new Vector3(0, 0, 0));
        Vector3 topRight = _cam.ViewportToWorldPoint(new Vector3(1, 1, 0));

        int minX = Mathf.Max(0, Mathf.FloorToInt(botLeft.x / step) - _margin);
        int maxX = Mathf.Min(_gm.Width - 1, Mathf.CeilToInt(topRight.x / step) + _margin);
        int minY = Mathf.Max(0, Mathf.FloorToInt(botLeft.y / step) - _margin);
        int maxY = Mathf.Min(_gm.Height - 1, Mathf.CeilToInt(topRight.y / step) + _margin);

        if (minX == _lastMinX && maxX == _lastMaxX &&
            minY == _lastMinY && maxY == _lastMaxY) return;

        // Désactiver ce qui sort de la vue
        ApplyRange(_lastMinX, _lastMaxX, _lastMinY, _lastMaxY,
                   minX, maxX, minY, maxY, false);
        // Activer ce qui entre dans la vue
        ApplyRange(minX, maxX, minY, maxY,
                   _lastMinX, _lastMaxX, _lastMinY, _lastMaxY, true);

        _lastMinX = minX; _lastMaxX = maxX;
        _lastMinY = minY; _lastMaxY = maxY;
    }

    private void ApplyRange(
        int minX, int maxX, int minY, int maxY,
        int exMinX, int exMaxX, int exMinY, int exMaxY,
        bool visible)
    {
        int w = _renderers.GetLength(0);
        int h = _renderers.GetLength(1);
        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                if (x >= exMinX && x <= exMaxX && y >= exMinY && y <= exMaxY) continue;
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                var sr = _renderers[x, y];
                if (sr != null) sr.enabled = visible;
            }
    }
}
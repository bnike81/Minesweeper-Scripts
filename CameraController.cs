using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// CameraController - Scrolling molette haut/bas sur la grille.
/// Compatible nouveau Input System Unity.
/// Attach sur la Main Camera.
/// </summary>
public class CameraController : MonoBehaviour
{
    public static CameraController Instance { get; private set; }

    [Header("=== Scrolling Molette ===")]
    [Tooltip("Vitesse de scroll a la molette")]
    [SerializeField] private float _scrollSpeed = 0.005f;

    [Tooltip("Lissage du mouvement (0 = instantane, 0.9 = tres lisse)")]
    [SerializeField, Range(0f, 0.95f)] private float _smoothing = 0.85f;

    [Header("=== Limites de la Grille ===")]
    [Tooltip("Marge en unites Unity au-dela de la grille")]
    [SerializeField] private float _borderMargin = 1.5f;

    [Header("=== Etat (lecture seule) ===")]
    [SerializeField, ReadOnly] private float _minY;
    [SerializeField, ReadOnly] private float _maxY;
    [SerializeField, ReadOnly] private float _currentTargetY;

    private Vector3 _targetPosition;
    private Camera _cam;
    private float _halfHeight;
    private float _halfWidth;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _cam = GetComponent<Camera>();
        _targetPosition = transform.position;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnGridGenerated>(OnGridGenerated);
        EventBus.Subscribe<OnGridExtended>(OnGridExtended);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnGridGenerated>(OnGridGenerated);
        EventBus.Unsubscribe<OnGridExtended>(OnGridExtended);
    }

    private void Update()
    {
        HandleScrollInput();
        ApplySmoothing();
    }

    // -------------------------------------------------------------------------
    // Mise a jour des limites quand la grille change
    // -------------------------------------------------------------------------

    private void OnGridGenerated(OnGridGenerated evt)
    {
        UpdateCameraHalfSize();
        ComputeLimits(evt.Width, evt.Height);

        // Demarre la camera en bas de la grille (niveau 1 visible)
        float step = GetCellStep();
        float startX = evt.Width * step * 0.5f - step * 0.5f;
        // _minY est deja calcule = halfHeight - margin, soit la vue du bas
        float startY = _minY + _borderMargin; // exactement le bas de grille

        _targetPosition = new Vector3(startX, startY, transform.position.z);
        transform.position = _targetPosition;

        Debug.Log("<color=#00CCFF>[Camera]</color> Position initiale X="
            + startX.ToString("F2") + " Y=" + startY.ToString("F2"));
    }

    private void OnGridExtended(OnGridExtended evt)
    {
        UpdateCameraHalfSize();
        var gm = GridManager.Instance;
        if (gm != null) ComputeLimits(gm.Width, gm.Height);
    }

    private void ComputeLimits(int gridWidth, int gridHeight)
    {
        float step = GetCellStep();
        float gridWorldHeight = gridHeight * step;
        float gridWorldWidth = gridWidth * step;

        if (_halfHeight * 2f >= gridWorldHeight)
        {
            // Cas : la camera est plus grande que la grille
            // On centre sur la grille et on bloque le scroll
            _minY = _maxY = gridWorldHeight * 0.5f;
            Debug.LogWarning("<color=#FFAA00>[Camera]</color> Camera plus grande que la grille !"
                + " CamHalf=" + _halfHeight.ToString("F1")
                + " GridH=" + gridWorldHeight.ToString("F1")
                + " -> Reduis orthographicSize ou augmente Reference Resolution.");
        }
        else
        {
            // Cas normal : la camera voit une portion de la grille
            _minY = _halfHeight - _borderMargin;
            _maxY = gridWorldHeight - _halfHeight + _borderMargin;
        }

        _currentTargetY = _targetPosition.y;

        Debug.Log("<color=#00CCFF>[Camera]</color> Limites Y=["
            + _minY.ToString("F1") + " , " + _maxY.ToString("F1") + "]"
            + " | GridH=" + gridWorldHeight.ToString("F1")
            + " | CamHalf=" + _halfHeight.ToString("F1")
            + ((_halfHeight * 2f >= gridWorldHeight)
                ? " [CAMERA TROP GRANDE - scroll desactive]"
                : " [OK]"));
    }

    // -------------------------------------------------------------------------
    // Input molette - nouveau Input System
    // -------------------------------------------------------------------------

    private void HandleScrollInput()
    {
        // Ne pas scroller la grille si la souris est sur le panel dialogue
        if (DialogueScrollHandler.MouseOverDialogue) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0f)) return;

        _targetPosition.y += scroll * _scrollSpeed;
        _targetPosition.y = Mathf.Clamp(_targetPosition.y, _minY, _maxY);
        _currentTargetY = _targetPosition.y;
    }

    // -------------------------------------------------------------------------
    // Lissage
    // -------------------------------------------------------------------------

    private void ApplySmoothing()
    {
        if (_smoothing <= 0f)
        {
            transform.position = _targetPosition;
            return;
        }

        transform.position = Vector3.Lerp(
            transform.position,
            _targetPosition,
            1f - _smoothing);
    }

    // -------------------------------------------------------------------------
    // API publique
    // -------------------------------------------------------------------------

    public void ScrollTo(float worldY, bool instant = false)
    {
        _targetPosition.y = Mathf.Clamp(worldY, _minY, _maxY);
        _currentTargetY = _targetPosition.y;
        if (instant) transform.position = _targetPosition;
    }

    public void ScrollToNewZone(float newGridHeight)
    {
        float step = GetCellStep();
        ScrollTo(newGridHeight * step - _halfHeight);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------



    private void UpdateCameraHalfSize()
    {
        if (_cam == null) return;
        _halfHeight = _cam.orthographicSize;
        _halfWidth = _halfHeight * _cam.aspect;
    }

    private float GetCellStep()
    {
        var gm = GridManager.Instance;
        return gm != null ? gm.CellStep : 1.05f;
    }
}
using UnityEngine;

/// <summary>
/// CoastAnimator -- Cycle 6 frames d'animation pour une piece cotiere.
/// Timer global synchronise pour que l'eau pulse ensemble.
/// </summary>
public class CoastAnimator : MonoBehaviour
{
    private static float _globalTimer;
    private static int _globalFrame;
    private static float _frameDuration = 0.8f;
    private static int _lastUpdateFrame = -1; // garde : un seul Update par frame Unity

    public const int FrameCount = 6;

    public static void SetFrameDuration(float duration)
    {
        _frameDuration = Mathf.Max(0.01f, duration);
    }

    public static int CurrentFrame => _globalFrame;

    private Sprite[] _frames;
    private SpriteRenderer _sr;
    private int _lastFrame = -1;

    public void Setup(Sprite[] frames, SpriteRenderer sr)
    {
        _frames = frames;
        _sr = sr;
        _lastFrame = -1;

        if (_frames != null && _frames.Length > 0 && _sr != null)
            _sr.sprite = _frames[0];
    }

    private void Update()
    {
        // Un seul CoastAnimator avance le timer par frame Unity
        int unityFrame = Time.frameCount;
        if (_lastUpdateFrame != unityFrame)
        {
            _lastUpdateFrame = unityFrame;
            _globalTimer += Time.deltaTime;
            if (_globalTimer >= _frameDuration)
            {
                _globalTimer -= _frameDuration;
                _globalFrame = (_globalFrame + 1) % FrameCount;
            }
        }

        if (_globalFrame != _lastFrame && _frames != null && _sr != null)
        {
            _lastFrame = _globalFrame;
            int idx = _globalFrame % _frames.Length;
            if (_frames[idx] != null)
                _sr.sprite = _frames[idx];
        }
    }

    public static void ResetGlobalTimer()
    {
        _globalTimer = 0f;
        _globalFrame = 0;
        _lastUpdateFrame = -1;
    }
}
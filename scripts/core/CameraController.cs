using Godot;

namespace NWO.Core;

// Owns camera pan, zoom, smooth-center, and the post-animation center delay.
// Lives outside the Camera2D node so the panning state and tween logic are
// testable / inspectable without going through Godot scene plumbing.
public class CameraController
{
    private const float CameraLerpSpeed = 8f;
    private const float PanSpeed        = 600f;
    public  const float PostAnimCenterDelay = 0.5f;

    private static readonly Vector2 ZoomMin = Vector2.One * 0.2f;
    private static readonly Vector2 ZoomMax = Vector2.One * 5.0f;

    private readonly Camera2D _camera;
    private Vector2? _target;
    private float    _postAnimDelay;
    private Vector2? _deferredCenter;

    public bool IsPanning { get; set; }

    public CameraController(Camera2D camera) => _camera = camera;

    public Vector2 Position
    {
        get => _camera.Position;
        set => _camera.Position = value;
    }

    public void CenterOn(Vector2 worldPos) => _target = worldPos;

    // If a post-animation delay is active, queue the center; otherwise center now.
    // Used after a unit finishes moving — we want the player to see the destination
    // briefly before the next end-turn-queue item yanks the camera away.
    public void DeferOrCenter(Vector2 worldPos)
    {
        if (_postAnimDelay > 0f) _deferredCenter = worldPos;
        else                     CenterOn(worldPos);
    }

    public void StartPostAnimDelay() => _postAnimDelay = PostAnimCenterDelay;

    public void CancelPostAnimDelay()
    {
        _postAnimDelay  = 0f;
        _deferredCenter = null;
    }

    public void ApplyKeyboardPan(Vector2 dir, float delta)
    {
        if (dir == Vector2.Zero) return;
        _target           = null;
        _camera.Position += dir.Normalized() * PanSpeed * delta / _camera.Zoom.X;
    }

    public void ApplyMousePan(Vector2 relative)
    {
        _target           = null;
        _camera.Position -= relative / _camera.Zoom.X;
    }

    public void Zoom(float factor)
    {
        _camera.Zoom = (_camera.Zoom * factor).Clamp(ZoomMin, ZoomMax);
    }

    // Per-frame tween toward target + post-anim delay tick.
    public void Tick(float delta)
    {
        if (_postAnimDelay > 0f)
        {
            _postAnimDelay -= delta;
            if (_postAnimDelay <= 0f)
            {
                _postAnimDelay = 0f;
                if (_deferredCenter.HasValue)
                {
                    CenterOn(_deferredCenter.Value);
                    _deferredCenter = null;
                }
            }
        }

        if (_target.HasValue)
        {
            float t = 1f - Mathf.Exp(-CameraLerpSpeed * delta);
            _camera.Position = _camera.Position.Lerp(_target.Value, t);
            if (_camera.Position.DistanceTo(_target.Value) < 0.5f)
            {
                _camera.Position = _target.Value;
                _target          = null;
            }
        }
    }
}

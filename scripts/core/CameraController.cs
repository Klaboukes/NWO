using Godot;

namespace NWO.Core;

// Owns camera pan, zoom, smooth-center, and the post-animation center delay for
// the fixed-tilt 3D view (Phase 7 V7.1). A pivot Node3D sits on the ground plane
// at the look-at point; the Camera3D is its child, locked at a fixed downward
// tilt and a variable dolly distance (zoom). Pan slides the pivot across the
// ground; zoom changes the camera distance. The tween / defer logic is unchanged
// from the old Camera2D controller — it just lerps a Vector3 pivot position.
public class CameraController
{
    private const float CameraLerpSpeed   = 8f;
    private const float KeyPanPxPerSec     = 900f; // screen px/sec at the focal plane
    public  const float PostAnimCenterDelay = 0.5f;

    private const float TiltDegFromGround = 55f;   // 90 = straight down, 0 = horizon
    private const float MinDistance       = 150f;
    private const float MaxDistance       = 1600f;

    private readonly Node3D   _pivot;
    private readonly Camera3D _camera;
    private readonly float    _tiltRad;
    private readonly Vector3  _dir;   // unit offset from pivot to camera

    private float    _distance = 520f;
    private Vector3? _target;
    private float    _postAnimDelay;
    private Vector3? _deferredCenter;

    public bool IsPanning { get; set; }

    public CameraController(Node3D pivot, Camera3D camera)
    {
        _pivot   = pivot;
        _camera  = camera;
        _tiltRad = Mathf.DegToRad(TiltDegFromGround);
        _dir     = new Vector3(0f, Mathf.Sin(_tiltRad), Mathf.Cos(_tiltRad)).Normalized();
        ApplyDistance();
    }

    // The pivot's ground position (X/Z; Y stays 0). WorldMap centres on tiles by
    // setting this to HexProjection.AxialToWorld(...).
    public Vector3 Position
    {
        get => _pivot.Position;
        set => _pivot.Position = new Vector3(value.X, 0f, value.Z);
    }

    public void CenterOn(Vector3 worldPos) => _target = Ground(worldPos);

    public void DeferOrCenter(Vector3 worldPos)
    {
        if (_postAnimDelay > 0f) _deferredCenter = Ground(worldPos);
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
        _target = null;
        float step = KeyPanPxPerSec * delta * WorldPerPixel();
        _pivot.Position += GroundDelta(dir.Normalized() * step);
    }

    public void ApplyMousePan(Vector2 relative)
    {
        _target = null;
        // Drag the world under the cursor: move the pivot opposite the drag.
        _pivot.Position -= GroundDelta(relative * WorldPerPixel());
    }

    public void Zoom(float factor) => ZoomToward(factor, null);

    // Zoom while keeping the world point under `mouseScreen` stationary.
    // If the ray misses the ground (e.g. pointing above the horizon) it falls
    // back to plain centre-zoom.
    public void ZoomToward(float factor, Vector2? mouseScreen)
    {
        float distBefore = _distance;
        _distance = Mathf.Clamp(_distance / factor, MinDistance, MaxDistance);
        ApplyDistance();

        if (mouseScreen is not { } screen) return;

        var hit = GroundUnderScreen(screen);
        if (hit is not { } cursor) return;

        // Shift the pivot so `cursor` stays fixed on screen.
        // The world-per-pixel scale is proportional to _distance, so the pivot
        // offset from cursor must scale by the same ratio.
        float ratio = _distance / distBefore;
        var   p     = _pivot.Position;
        _pivot.Position = new Vector3(
            cursor.X - (cursor.X - p.X) * ratio,
            0f,
            cursor.Z - (cursor.Z - p.Z) * ratio);
        _target = null; // cancel any in-progress smooth-center tween
    }

    private Vector3? GroundUnderScreen(Vector2 screen)
    {
        var from = _camera.ProjectRayOrigin(screen);
        var dir  = _camera.ProjectRayNormal(screen);
        return new Plane(Vector3.Up, 0f).IntersectsRay(from, dir);
    }

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
            _pivot.Position = _pivot.Position.Lerp(_target.Value, t);
            if (_pivot.Position.DistanceTo(_target.Value) < 0.5f)
            {
                _pivot.Position = _target.Value;
                _target         = null;
            }
        }
    }

    // Place the camera at the fixed tilt, `_distance` from the pivot, looking back
    // at the pivot centre. Pure translation of the pivot afterwards preserves the
    // look-at relationship, so rotation only needs setting here.
    private void ApplyDistance()
    {
        var localPos = _dir * _distance;
        _camera.Transform = new Transform3D(Basis.Identity, localPos).LookingAt(Vector3.Zero, Vector3.Up);
    }

    private static Vector3 Ground(Vector3 v) => new(v.X, 0f, v.Z);

    // Map a screen-space delta (X right, Y down) onto the ground plane. The Y/Z
    // axis is divided by sin(tilt) because the tilted view foreshortens the ground
    // along the look direction, so a vertical drag should cover more ground.
    private Vector3 GroundDelta(Vector2 screen)
        => new(screen.X, 0f, screen.Y / Mathf.Sin(_tiltRad));

    private float WorldPerPixel()
    {
        var vp = _camera.GetViewport();
        float vh = vp != null ? vp.GetVisibleRect().Size.Y : 720f;
        if (vh <= 0f) vh = 720f;
        float worldHeight = 2f * _distance * Mathf.Tan(Mathf.DegToRad(_camera.Fov * 0.5f));
        return worldHeight / vh;
    }
}

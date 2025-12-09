using OpenTK.Mathematics;

namespace AstrildCCSandbox.Character;

/// <summary>
/// Very small kinematic character motor prototype.
/// Uses an infinite ground at y=0 for ground detection.
/// </summary>
public class CharacterMotor
{
    private readonly CharacterControllerData _data;
    // Optional ground query callback. Given an XZ position returns (height, normal) or null if no surface.
    public Func<Vector2, (float height, Vector3 normal)?>? GroundQuery { get; set; }

    public CharacterMotor(CharacterControllerData data)
    {
        _data = data;
    }

    public Vector3 Position => _data.Position;
    public Vector3 Velocity => _data.Velocity;
    public bool IsGrounded => _data.IsGrounded;

    public void Update(float dt, Vector2 input, bool jumpPressed)
    {
        // Update timers
        _data.TimeSinceLastGrounded += dt;
        _data.TimeSinceJumpPressed += dt;
        if (jumpPressed)
            _data.TimeSinceJumpPressed = 0f;

        // Query scene for ground surface first (if provided), otherwise fall back to Y=0 plane
        float groundY = 0f;
        Vector3 groundNormal = Vector3.UnitY;
        var query = GroundQuery?.Invoke(new Vector2(_data.Position.X, _data.Position.Z));
        if (query != null)
        {
            groundY = query.Value.height;
            groundNormal = query.Value.normal;
        }
        float capsuleBottom = _data.Position.Y - (_data.Height * 0.5f);
        float capsuleTop = _data.Position.Y + (_data.Height * 0.5f);
        // Only snap to ground if we're not currently moving upward and the surface
        // is not above the character (prevents snapping to ceilings while passing underneath).
        // This prevents a freshly-applied jump from being immediately cancelled by the snap when
        // GroundSnapDistance is larger than a single-frame jump displacement.
        if (capsuleBottom <= groundY + _data.GroundSnapDistance && _data.Velocity.Y <= 0.01f && groundY <= capsuleTop - 0.05f)
        {
            // Snap to ground
            var p = _data.Position;
            p.Y = groundY + (_data.Height * 0.5f) + _data.SkinWidth;
            _data.Position = p;

            _data.IsGrounded = true;
            _data.GroundNormal = groundNormal;
            _data.TimeSinceLastGrounded = 0f;

            if (_data.Velocity.Y < 0f)
            {
                var v0 = _data.Velocity;
                v0.Y = 0f;
                _data.Velocity = v0;
            }
        }
        else
        {
            _data.IsGrounded = false;
        }

        // Gravity
        if (!_data.IsGrounded)
        {
            var gv = _data.Velocity;
            gv.Y -= _data.Gravity * dt;
            if (gv.Y < -_data.MaxFallSpeed) gv.Y = -_data.MaxFallSpeed;
            _data.Velocity = gv;
        }

        // Horizontal movement with acceleration
        Vector2 currentH = new Vector2(_data.Velocity.X, _data.Velocity.Z);
        // If standing on a moving platform, blend platform horizontal velocity into movement target
        Vector2 platformVelH = new Vector2(_data.PlatformVelocity.X, _data.PlatformVelocity.Z);
        Vector2 targetH = input * _data.MaxWalkSpeed;
        if (_data.IsGrounded)
            targetH += platformVelH;
        float accel = _data.IsGrounded ? _data.Acceleration : _data.AirAcceleration;
        Vector2 newH = MoveTowards(currentH, targetH, accel * dt);
        var vh2 = _data.Velocity;
        vh2.X = newH.X;
        vh2.Z = newH.Y;
        _data.Velocity = vh2;

        // Rotation: face movement direction when there's input
        if (input.LengthSquared > 0.0001f)
        {
            // input.X -> world X (right), input.Y -> world Z (forward)
            float desiredRad = MathF.Atan2(input.X, input.Y); // atan2(x, z)
            float desiredDeg = MathHelper.RadiansToDegrees(desiredRad);
            _data.Yaw = MoveTowardsAngle(_data.Yaw, desiredDeg, _data.RotationSpeed * dt);
        }

        // Jump buffering + coyote time
        if (_data.TimeSinceJumpPressed <= _data.JumpBufferTime && _data.TimeSinceLastGrounded <= _data.CoyoteTime)
        {
            var vj = _data.Velocity;
            vj.Y = _data.JumpSpeed;
            _data.Velocity = vj;
            _data.IsGrounded = false;
            _data.TimeSinceJumpPressed = 999f; // consume
        }

        // Integrate
        _data.Position += _data.Velocity * dt;

        // If standing on a moving platform, apply platform transport velocity to position
        if (_data.IsGrounded)
        {
            _data.Position += _data.PlatformVelocity * dt;
        }

        // Re-query scene at new XZ to detect ceilings/overhangs and resolve vertical penetration.
        // We only treat a surface as a ceiling if it lies within the vertical span of the capsule
        // (i.e. above the capsule bottom) and the capsule top penetrates the surface.
        var postQuery = GroundQuery?.Invoke(new Vector2(_data.Position.X, _data.Position.Z));
        if (postQuery != null)
        {
            float surfaceH = postQuery.Value.height;
            // capsule vertical extents
            float half = _data.Height * 0.5f;
            float capBottom = _data.Position.Y - half;
            float capTop = _data.Position.Y + half;
            // only consider this surface a potential ceiling if it is above the capsule bottom
            if (surfaceH > capBottom + 1e-4f)
            {
                float allowedTop = surfaceH - _data.SkinWidth;
                if (capTop > allowedTop)
                {
                    float penetration = capTop - allowedTop;
                    var p = _data.Position;
                    p.Y -= penetration;
                    _data.Position = p;
                    // cancel upward velocity so we don't immediately re-penetrate
                    if (_data.Velocity.Y > 0f)
                    {
                        var v = _data.Velocity;
                        v.Y = 0f;
                        _data.Velocity = v;
                    }
                    // also mark not grounded (we are colliding above)
                    _data.IsGrounded = false;
                }
            }
        }
    }

    private static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDelta)
    {
        Vector2 delta = target - current;
        float dist = delta.Length;
        if (dist <= maxDelta || dist == 0f)
            return target;
        return current + delta / dist * maxDelta;
    }

    private static float MoveTowardsAngle(float current, float target, float maxDelta)
    {
        float delta = DeltaAngle(current, target);
        if (MathF.Abs(delta) <= maxDelta)
            return target;
        return current + MathF.Sign(delta) * maxDelta;
    }

    private static float DeltaAngle(float a, float b)
    {
        float diff = (b - a) % 360f;
        if (diff < -180f) diff += 360f;
        if (diff > 180f) diff -= 360f;
        return diff;
    }
}

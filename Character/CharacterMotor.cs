using OpenTK.Mathematics;

namespace AstrildCCSandbox.Character;

/// <summary>
/// Very small kinematic character motor prototype.
/// Uses an infinite ground at y=0 for ground detection.
/// </summary>
public class CharacterMotor
{
    private readonly CharacterControllerData _data;
    // Optional ground query callback. Given an XZ position and a max search height (exclusive),
    // returns the highest surface at or below maxSearchHeight, or null if none.
    public Func<Vector2, float, (float height, Vector3 normal)?>? GroundQuery { get; set; }

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
        // ask the ground query to find surfaces at or below the capsule top (so overhead objects don't win)
        float capsuleTopForQuery = _data.Position.Y + (_data.Height * 0.5f);
        var query = GroundQuery?.Invoke(new Vector2(_data.Position.X, _data.Position.Z), capsuleTopForQuery - 0.05f);
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

        // Slope handling: if grounded and the ground normal exceeds MaxSlopeAngle,
        // apply sliding along the downhill direction while still allowing player input
        if (_data.IsGrounded && _data.GroundNormal != Vector3.Zero)
        {
            float dot = Vector3.Dot(_data.GroundNormal, Vector3.UnitY);
            if (dot < -1f) dot = -1f;
            if (dot > 1f) dot = 1f;
            float slopeAngleDeg = MathHelper.RadiansToDegrees(MathF.Acos(dot));
            if (slopeAngleDeg > _data.MaxSlopeAngle)
            {
                // downhill direction (project world down onto the ground plane)
                var downhill = new Vector3(-_data.GroundNormal.X, 0f, -_data.GroundNormal.Z);
                if (downhill.LengthSquared > 1e-6f)
                    downhill = downhill.Normalized();
                else
                    downhill = Vector3.Zero;

                // slide acceleration magnitude proportional to gravity and slope steepness
                float slideAccel = _data.Gravity * MathF.Sin(MathHelper.DegreesToRadians(slopeAngleDeg));

                // apply slide to horizontal velocity, but allow player input to counter (we already computed newH earlier)
                var v = _data.Velocity;
                v.X += downhill.X * slideAccel * dt;
                v.Z += downhill.Z * slideAccel * dt;
                // small downward bias so character remains on the slope
                v.Y -= slideAccel * 0.05f * dt;
                _data.Velocity = v;
            }
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

        // NOTE: ceiling/overhang resolution is handled by the scene collision resolver
        // (Game.HandleSceneCollisionsAndTransport) after integration so that we don't
        // oscillate with per-surface pushes. GroundQuery/ceiling logic will be applied
        // centrally in the game collision pass.
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

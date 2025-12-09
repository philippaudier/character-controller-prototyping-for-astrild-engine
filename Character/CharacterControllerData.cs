using OpenTK.Mathematics;

namespace AstrildCCSandbox.Character;

/// <summary>
/// Holds character geometry parameters, movement parameters and runtime state.
/// This is a small container used by the CharacterMotor prototype.
/// </summary>
public class CharacterControllerData
{
    // Geometry
    public float Radius { get; set; }
    public float Height { get; set; }

    // Movement params
    public float MaxWalkSpeed { get; set; }
    public float Gravity { get; set; }
    public float JumpSpeed { get; set; }

    // Extended movement params
    public float MaxRunSpeed { get; set; }
    public float Acceleration { get; set; }
    public float AirAcceleration { get; set; }
    public float MaxFallSpeed { get; set; }

    // Ground & geometry helpers
    public float SkinWidth { get; set; }
    public float GroundSnapDistance { get; set; }
    public float MaxSlopeAngle { get; set; }
    public float StepOffset { get; set; }
    public Vector3 GroundNormal { get; set; }

    // Jump helpers
    public float CoyoteTime { get; set; }
    public float JumpBufferTime { get; set; }

    // Runtime
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
    public bool IsGrounded { get; set; }
    // Velocity of the surface the character is standing on (transport)
    public Vector3 PlatformVelocity { get; set; }

    // Timers (runtime)
    public float TimeSinceLastGrounded { get; set; }
    public float TimeSinceJumpPressed { get; set; }
    
    // Orientation (degrees)
    public float Yaw { get; set; }
    public float RotationSpeed { get; set; } // degrees per second

    public CharacterControllerData()
    {
        Radius = 0.4f;
        Height = 1.8f;

        // Movement
        MaxWalkSpeed = 6f;
        MaxRunSpeed = 9f;
        Acceleration = 20f;
        AirAcceleration = 5f;
        MaxFallSpeed = 50f;

        Gravity = 20f;
        JumpSpeed = 7f;

        // Geometry / ground
        SkinWidth = 0.02f;
        GroundSnapDistance = 0.2f;
        MaxSlopeAngle = 45f;
        StepOffset = 0.4f;
        GroundNormal = Vector3.UnitY;

        // Jump timings
        CoyoteTime = 0.1f;
        JumpBufferTime = 0.1f;

        // Runtime
        Position = new Vector3(0, 1.0f, 0);
        Velocity = Vector3.Zero;
        IsGrounded = false;
        PlatformVelocity = Vector3.Zero;
        TimeSinceLastGrounded = 999f;
        TimeSinceJumpPressed = 999f;
        Yaw = 0f;
        RotationSpeed = 720f;
    }
}

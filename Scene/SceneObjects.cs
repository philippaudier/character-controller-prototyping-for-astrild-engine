using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace AstrildCCSandbox.Scene
{
    // Base surface that can answer height queries at an XZ position
    public abstract class SceneSurface
    {
        // Return true if the surface covers the XZ position and provide top height and normal
        public abstract bool TryGetHeight(Vector2 xz, out float height, out Vector3 normal);

        // Optional per-frame update for moving surfaces
        public virtual void Update(float dt) { }
    }

    // Simple axis-aligned rectangular platform (top is flat)
    public class FlatPlatform : SceneSurface
    {
        public Vector3 Center; // world center pos
        public float SizeX;
        public float SizeZ;
        public float TopY; // top surface y
        public float RotationYDeg; // yaw rotation around Y in degrees

        public FlatPlatform(Vector3 center, float sizeX, float sizeZ, float topY, float rotationYDeg = 0f)
        {
            Center = center;
            SizeX = sizeX;
            SizeZ = sizeZ;
            TopY = topY;
            RotationYDeg = rotationYDeg;
        }

        public override bool TryGetHeight(Vector2 xz, out float height, out Vector3 normal)
        {
            // transform xz into local platform space (inverse yaw)
            float yaw = MathHelper.DegreesToRadians(-RotationYDeg);
            float cos = (float)Math.Cos(yaw);
            float sin = (float)Math.Sin(yaw);
            var localX = cos * (xz.X - Center.X) - sin * (xz.Y - Center.Z);
            var localZ = sin * (xz.X - Center.X) + cos * (xz.Y - Center.Z);

            if (Math.Abs(localX) <= SizeX * 0.5f + 1e-5f && Math.Abs(localZ) <= SizeZ * 0.5f + 1e-5f)
            {
                height = TopY;
                normal = Vector3.UnitY;
                return true;
            }
            height = 0f;
            normal = Vector3.UnitY;
            return false;
        }
    }

    // Ramp: rectangular top that linearly interpolates height between one edge and the other
    public class Ramp : SceneSurface
    {
        public Vector3 Center;
        public float SizeX;
        public float SizeZ;
        public float StartY; // at local -Z/2
        public float EndY;   // at local +Z/2
        public float RotationYDeg;

        public Ramp(Vector3 center, float sizeX, float sizeZ, float startY, float endY, float rotationYDeg = 0f)
        {
            Center = center;
            SizeX = sizeX;
            SizeZ = sizeZ;
            StartY = startY;
            EndY = endY;
            RotationYDeg = rotationYDeg;
        }

        public override bool TryGetHeight(Vector2 xz, out float height, out Vector3 normal)
        {
            float yaw = MathHelper.DegreesToRadians(-RotationYDeg);
            float cos = (float)Math.Cos(yaw);
            float sin = (float)Math.Sin(yaw);
            var localX = cos * (xz.X - Center.X) - sin * (xz.Y - Center.Z);
            var localZ = sin * (xz.X - Center.X) + cos * (xz.Y - Center.Z);

            if (Math.Abs(localX) <= SizeX * 0.5f + 1e-5f && Math.Abs(localZ) <= SizeZ * 0.5f + 1e-5f)
            {
                float t = (localZ + SizeZ * 0.5f) / SizeZ;
                height = MathHelper.Lerp(StartY, EndY, t);

                // compute plane normal via three points on the ramp (local space)
                var p1 = new Vector3(-SizeX * 0.5f, MathHelper.Lerp(StartY, EndY, 0f), -SizeZ * 0.5f);
                var p2 = new Vector3(SizeX * 0.5f, MathHelper.Lerp(StartY, EndY, 0f), -SizeZ * 0.5f);
                var p3 = new Vector3(-SizeX * 0.5f, MathHelper.Lerp(StartY, EndY, 1f), SizeZ * 0.5f);
                var v1 = p2 - p1;
                var v2 = p3 - p1;
                var nLocal = Vector3.Cross(v1, v2).Normalized();

                // rotate normal back to world space
                var rot = Quaternion.FromAxisAngle(Vector3.UnitY, MathHelper.DegreesToRadians(RotationYDeg));
                normal = (rot * nLocal).Normalized();
                return true;
            }
            height = 0f;
            normal = Vector3.UnitY;
            return false;
        }
    }

    // Staircase composed of multiple flat steps
    public class Staircase : SceneSurface
    {
        public List<FlatPlatform> Steps = new List<FlatPlatform>();

        public Staircase(Vector3 startCenter, float width, int stepCount, float stepDepth, float stepHeight, float rotationYDeg = 0f)
        {
            // build steps extending along local Z positive
            for (int i = 0; i < stepCount; i++)
            {
                float localZ = -((stepCount * stepDepth) / 2f) + (i * stepDepth) + stepDepth * 0.5f;
                // rotate local to world
                float yaw = MathHelper.DegreesToRadians(rotationYDeg);
                float cos = (float)Math.Cos(yaw);
                float sin = (float)Math.Sin(yaw);
                float worldX = cos * 0 + -sin * localZ;
                float worldZ = sin * 0 + cos * localZ;
                var center = new Vector3(startCenter.X + worldX, startCenter.Y + (i + 1) * stepHeight, startCenter.Z + worldZ);
                Steps.Add(new FlatPlatform(center, width, stepDepth, center.Y, rotationYDeg));
            }
        }

        public override bool TryGetHeight(Vector2 xz, out float height, out Vector3 normal)
        {
            // return the highest step that covers the xz
            float bestH = float.MinValue;
            Vector3 bestN = Vector3.UnitY;
            bool found = false;
            foreach (var s in Steps)
            {
                if (s.TryGetHeight(xz, out float h, out Vector3 n))
                {
                    if (h > bestH)
                    {
                        bestH = h;
                        bestN = n;
                        found = true;
                    }
                }
            }
            height = bestH;
            normal = bestN;
            return found;
        }
    }

    // Moving platform moves its center between two points with a period
    public class MovingPlatform : FlatPlatform
    {
        public Vector3 A;
        public Vector3 B;
        public float Period = 2f;
        private float _time = 0f;
        private Vector3 _prevCenter;
        public Vector3 Velocity { get; private set; }

        public MovingPlatform(Vector3 a, Vector3 b, float sizeX, float sizeZ, float topY, float rotationYDeg = 0f, float period = 2f)
            : base(a, sizeX, sizeZ, topY, rotationYDeg)
        {
            A = a;
            B = b;
            Period = Math.Max(0.001f, period);
            Center = a;
            _prevCenter = a;
            Velocity = Vector3.Zero;
        }

        public override void Update(float dt)
        {
            _time += dt;
            float t = 0.5f * (1f + (float)Math.Sin(2.0 * Math.PI * (_time / Period)));
            var newCenter = Vector3.Lerp(A, B, t);
            Center = newCenter;
            TopY = Center.Y;
            // compute velocity from center movement
            if (dt > 0f)
            {
                Velocity = (Center - _prevCenter) / dt;
            }
            _prevCenter = Center;
        }
    }

    // A rounded bump (hemisphere) sitting at a center position.
    public class Bump : SceneSurface
    {
        public Vector3 Center;
        public float Radius;

        public Bump(Vector3 center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        public override bool TryGetHeight(Vector2 xz, out float height, out Vector3 normal)
        {
            float dx = xz.X - Center.X;
            float dz = xz.Y - Center.Z;
            float d2 = dx * dx + dz * dz;
            if (d2 > Radius * Radius)
            {
                height = 0f;
                normal = Vector3.UnitY;
                return false;
            }
            float dy = MathF.Sqrt(MathF.Max(0f, Radius * Radius - d2));
            height = Center.Y + dy;
            // normal points from sphere center to surface point
            var n = new Vector3(dx, dy, dz);
            normal = n.Normalized();
            return true;
        }
    }

    // Full sphere obstacle (we report the top hemisphere height for queries)
    public class SphereObstacle : SceneSurface
    {
        public Vector3 Center;
        public float Radius;

        public SphereObstacle(Vector3 center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        public override bool TryGetHeight(Vector2 xz, out float height, out Vector3 normal)
        {
            float dx = xz.X - Center.X;
            float dz = xz.Y - Center.Z;
            float d2 = dx * dx + dz * dz;
            if (d2 > Radius * Radius)
            {
                height = 0f;
                normal = Vector3.UnitY;
                return false;
            }
            float dy = MathF.Sqrt(MathF.Max(0f, Radius * Radius - d2));
            height = Center.Y + dy;
            var n = new Vector3(dx, dy, dz);
            normal = n.Normalized();
            return true;
        }
    }
}

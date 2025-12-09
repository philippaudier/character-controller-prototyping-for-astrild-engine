using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace AstrildCCSandbox.Rendering;

    /// <summary>
    /// Provides simple VAO/VBO/EBO creators for basic primitives.
    /// Vertex layout: vec3 position (location = 0) + vec3 normal (location = 1)
    /// </summary>
public static class MeshPrimitives
{
    public static (int vao, int vbo, int ebo, int indexCount) CreateGroundQuad()
    {
        // A large quad on XZ plane centered at origin
        float size = 50f;
        float hs = size * 0.5f;
        // position (x,y,z) + normal (nx,ny,nz)
        float[] verts = new float[]
        {
            -hs, 0f, -hs,  0f, 1f, 0f,
             hs, 0f, -hs,  0f, 1f, 0f,
             hs, 0f,  hs,  0f, 1f, 0f,
            -hs, 0f,  hs,  0f, 1f, 0f,
        };
        uint[] idx = new uint[] { 0,1,2, 2,3,0 };

        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        int ebo = GL.GenBuffer();

        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Length * sizeof(uint), idx, BufferUsageHint.StaticDraw);

        GL.EnableVertexAttribArray(0);
        GL.EnableVertexAttribArray(1);
        int stride = 6 * sizeof(float);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));

        GL.BindVertexArray(0);
        return (vao, vbo, ebo, idx.Length);
    }

    public static (int vao, int vbo, int ebo, int indexCount) CreateUnitCube()
    {
        // Unit cube centered at origin (size 1)
        // 24 vertices (4 per face) with face normals
        float[] verts = new float[]
        {
            // Front (+Z)
            -0.5f, -0.5f,  0.5f,   0f, 0f, 1f,
             0.5f, -0.5f,  0.5f,   0f, 0f, 1f,
             0.5f,  0.5f,  0.5f,   0f, 0f, 1f,
            -0.5f,  0.5f,  0.5f,   0f, 0f, 1f,
            // Back (-Z)
            -0.5f, -0.5f, -0.5f,   0f, 0f, -1f,
             0.5f, -0.5f, -0.5f,   0f, 0f, -1f,
             0.5f,  0.5f, -0.5f,   0f, 0f, -1f,
            -0.5f,  0.5f, -0.5f,   0f, 0f, -1f,
            // Right (+X)
             0.5f, -0.5f, -0.5f,   1f, 0f, 0f,
             0.5f, -0.5f,  0.5f,   1f, 0f, 0f,
             0.5f,  0.5f,  0.5f,   1f, 0f, 0f,
             0.5f,  0.5f, -0.5f,   1f, 0f, 0f,
            // Left (-X)
            -0.5f, -0.5f,  0.5f,  -1f, 0f, 0f,
            -0.5f, -0.5f, -0.5f,  -1f, 0f, 0f,
            -0.5f,  0.5f, -0.5f,  -1f, 0f, 0f,
            -0.5f,  0.5f,  0.5f,  -1f, 0f, 0f,
            // Top (+Y)
            -0.5f,  0.5f,  0.5f,   0f, 1f, 0f,
             0.5f,  0.5f,  0.5f,   0f, 1f, 0f,
             0.5f,  0.5f, -0.5f,   0f, 1f, 0f,
            -0.5f,  0.5f, -0.5f,   0f, 1f, 0f,
            // Bottom (-Y)
            -0.5f, -0.5f, -0.5f,   0f, -1f, 0f,
             0.5f, -0.5f, -0.5f,   0f, -1f, 0f,
             0.5f, -0.5f,  0.5f,   0f, -1f, 0f,
            -0.5f, -0.5f,  0.5f,   0f, -1f, 0f,
        };

        uint[] idx = new uint[]
        {
            // Front
            0,1,2, 2,3,0,
            // Back
            4,5,6, 6,7,4,
            // Right
            8,9,10, 10,11,8,
            // Left
            12,13,14, 14,15,12,
            // Top
            16,17,18, 18,19,16,
            // Bottom
            20,21,22, 22,23,20
        };

        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        int ebo = GL.GenBuffer();

        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Length * sizeof(uint), idx, BufferUsageHint.StaticDraw);

        GL.EnableVertexAttribArray(0);
        GL.EnableVertexAttribArray(1);
        int stride = 6 * sizeof(float);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));

        GL.BindVertexArray(0);
        return (vao, vbo, ebo, idx.Length);
    }

    public static (int vao, int vbo, int ebo, int indexCount) CreateCapsule(float radius, float height, int radialSegments = 16, int hemiSegments = 6)
    {
        // Low-poly capsule along Y axis: cylinder with hemisphere caps.
        float cylHeight = Math.Max(0f, height - 2f * radius);

        var verts = new System.Collections.Generic.List<float>();
        var idx = new System.Collections.Generic.List<uint>();

        // helper to push a vertex and return its index
        System.Func<float, float, float, uint> push = (x, y, z) =>
        {
            uint id = (uint)(verts.Count / 3);
            verts.Add(x); verts.Add(y); verts.Add(z);
            return id;
        };

        double twoPi = Math.PI * 2.0;

        // Top pole
        uint topPole = push(0f, cylHeight * 0.5f + radius, 0f);

        // Top hemisphere rings
        var topRings = new System.Collections.Generic.List<uint[]>();
        for (int s = 1; s <= hemiSegments; s++)
        {
            double phi = (s / (double)hemiSegments) * (Math.PI / 2.0);
            float y = (float)(Math.Sin(phi) * radius) + cylHeight * 0.5f;
            float r = (float)(Math.Cos(phi) * radius);
            var ring = new uint[radialSegments];
            for (int j = 0; j < radialSegments; j++)
            {
                double theta = j / (double)radialSegments * twoPi;
                float x = (float)(Math.Cos(theta) * r);
                float z = (float)(Math.Sin(theta) * r);
                ring[j] = push(x, y, z);
            }
            topRings.Add(ring);
        }

        // Cylinder rings (top and bottom) - if cylinder height is zero, rings overlap with hemisphere rings
        var cylTopRing = new uint[radialSegments];
        var cylBottomRing = new uint[radialSegments];
        for (int j = 0; j < radialSegments; j++)
        {
            double theta = j / (double)radialSegments * twoPi;
            float x = (float)(Math.Cos(theta) * radius);
            float z = (float)(Math.Sin(theta) * radius);
            cylTopRing[j] = push(x, cylHeight * 0.5f, z);
            cylBottomRing[j] = push(x, -cylHeight * 0.5f, z);
        }

        // Bottom hemisphere rings (mirror of top)
        var botRings = new System.Collections.Generic.List<uint[]>();
        for (int s = hemiSegments; s >= 1; s--)
        {
            double phi = (s / (double)hemiSegments) * (Math.PI / 2.0);
            float y = (float)(-Math.Sin(phi) * radius) - cylHeight * 0.5f;
            float r = (float)(Math.Cos(phi) * radius);
            var ring = new uint[radialSegments];
            for (int j = 0; j < radialSegments; j++)
            {
                double theta = j / (double)radialSegments * twoPi;
                float x = (float)(Math.Cos(theta) * r);
                float z = (float)(Math.Sin(theta) * r);
                ring[j] = push(x, y, z);
            }
            botRings.Add(ring);
        }

        uint bottomPole = push(0f, -cylHeight * 0.5f - radius, 0f);

        // Build indices: top pole -> first top ring
        if (topRings.Count > 0)
        {
            var first = topRings[0];
            for (int j = 0; j < radialSegments; j++)
            {
                int j1 = (j + 1) % radialSegments;
                idx.Add(topPole);
                idx.Add(first[(uint)j1]);
                idx.Add(first[(uint)j]);
            }
        }

        // connect top hemisphere rings
        for (int r = 0; r < topRings.Count - 1; r++)
        {
            var a = topRings[r];
            var b = topRings[r + 1];
            for (int j = 0; j < radialSegments; j++)
            {
                int j1 = (j + 1) % radialSegments;
                idx.Add(a[(uint)j]); idx.Add(b[(uint)j]); idx.Add(b[(uint)j1]);
                idx.Add(a[(uint)j]); idx.Add(b[(uint)j1]); idx.Add(a[(uint)j1]);
            }
        }

        // connect last top ring to cylinder top ring
        var lastTop = topRings.Count > 0 ? topRings[topRings.Count - 1] : null;
        if (lastTop != null)
        {
            for (int j = 0; j < radialSegments; j++)
            {
                int j1 = (j + 1) % radialSegments;
                idx.Add(lastTop[(uint)j]); idx.Add(cylTopRing[(uint)j]); idx.Add(cylTopRing[(uint)j1]);
                idx.Add(lastTop[(uint)j]); idx.Add(cylTopRing[(uint)j1]); idx.Add(lastTop[(uint)j1]);
            }
        }

        // cylinder between top and bottom rings
        for (int j = 0; j < radialSegments; j++)
        {
            int j1 = (j + 1) % radialSegments;
            idx.Add(cylTopRing[(uint)j]); idx.Add(cylBottomRing[(uint)j]); idx.Add(cylBottomRing[(uint)j1]);
            idx.Add(cylTopRing[(uint)j]); idx.Add(cylBottomRing[(uint)j1]); idx.Add(cylTopRing[(uint)j1]);
        }

        // connect cylinder bottom ring to first bottom ring
        var firstBot = botRings.Count > 0 ? botRings[0] : null;
        if (firstBot != null)
        {
            for (int j = 0; j < radialSegments; j++)
            {
                int j1 = (j + 1) % radialSegments;
                idx.Add(cylBottomRing[(uint)j]); idx.Add(firstBot[(uint)j]); idx.Add(firstBot[(uint)j1]);
                idx.Add(cylBottomRing[(uint)j]); idx.Add(firstBot[(uint)j1]); idx.Add(cylBottomRing[(uint)j1]);
            }
        }

        // connect bottom hemisphere rings
        for (int r = 0; r < botRings.Count - 1; r++)
        {
            var a = botRings[r];
            var b = botRings[r + 1];
            for (int j = 0; j < radialSegments; j++)
            {
                int j1 = (j + 1) % radialSegments;
                idx.Add(a[(uint)j]); idx.Add(b[(uint)j]); idx.Add(b[(uint)j1]);
                idx.Add(a[(uint)j]); idx.Add(b[(uint)j1]); idx.Add(a[(uint)j1]);
            }
        }

        // bottom ring -> bottom pole
        if (botRings.Count > 0)
        {
            var last = botRings[botRings.Count - 1];
            for (int j = 0; j < radialSegments; j++)
            {
                int j1 = (j + 1) % radialSegments;
                idx.Add(last[(uint)j1]); idx.Add(last[(uint)j]); idx.Add(bottomPole);
            }
        }

        // Create GL buffers
        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        int ebo = GL.GenBuffer();

        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Count * sizeof(float), verts.ToArray(), BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Count * sizeof(uint), idx.ToArray(), BufferUsageHint.StaticDraw);

        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);

        GL.BindVertexArray(0);
        return (vao, vbo, ebo, idx.Count);
    }

    public static (int vao, int vbo, int ebo, int indexCount) CreateUVSphere(float radius = 1f, int stacks = 12, int slices = 16)
    {
        var verts = new System.Collections.Generic.List<float>();
        var idx = new System.Collections.Generic.List<uint>();

        for (int i = 0; i <= stacks; i++)
        {
            float phi = MathF.PI * i / stacks; // 0..PI
            float y = MathF.Cos(phi);
            float r = MathF.Sin(phi);
            for (int j = 0; j <= slices; j++)
            {
                float theta = 2f * MathF.PI * j / slices;
                float x = r * MathF.Cos(theta);
                float z = r * MathF.Sin(theta);
                // position
                verts.Add(x * radius);
                verts.Add(y * radius);
                verts.Add(z * radius);
                // normal (same as position normalized for unit sphere)
                verts.Add(x);
                verts.Add(y);
                verts.Add(z);
            }
        }

        for (int i = 0; i < stacks; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                uint a = (uint)(i * (slices + 1) + j);
                uint b = (uint)((i + 1) * (slices + 1) + j);
                uint c = (uint)((i + 1) * (slices + 1) + (j + 1));
                uint d = (uint)(i * (slices + 1) + (j + 1));
                idx.Add(a); idx.Add(b); idx.Add(c);
                idx.Add(a); idx.Add(c); idx.Add(d);
            }
        }

        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        int ebo = GL.GenBuffer();

        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Count * sizeof(float), verts.ToArray(), BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Count * sizeof(uint), idx.ToArray(), BufferUsageHint.StaticDraw);

        GL.EnableVertexAttribArray(0);
        GL.EnableVertexAttribArray(1);
        int stride = 6 * sizeof(float);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));

        GL.BindVertexArray(0);
        return (vao, vbo, ebo, idx.Count);
    }
}

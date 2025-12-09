using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace AstrildCCSandbox.Rendering;

/// <summary>
/// Minimal shader helper to compile/link and set simple uniforms.
/// Throws on compilation/link errors with the GL log.
/// </summary>
public sealed class SimpleShader : IDisposable
{
    public int Handle { get; }

    public SimpleShader(string vertexSource, string fragmentSource)
    {
        int vs = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vs, vertexSource);
        GL.CompileShader(vs);
        GL.GetShader(vs, ShaderParameter.CompileStatus, out int vOK);
        if (vOK == 0)
        {
            string log = GL.GetShaderInfoLog(vs);
            throw new Exception("Vertex shader compilation failed: " + log);
        }

        int fs = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fs, fragmentSource);
        GL.CompileShader(fs);
        GL.GetShader(fs, ShaderParameter.CompileStatus, out int fOK);
        if (fOK == 0)
        {
            string log = GL.GetShaderInfoLog(fs);
            throw new Exception("Fragment shader compilation failed: " + log);
        }

        Handle = GL.CreateProgram();
        GL.AttachShader(Handle, vs);
        GL.AttachShader(Handle, fs);
        GL.LinkProgram(Handle);
        GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int lOK);
        if (lOK == 0)
        {
            string log = GL.GetProgramInfoLog(Handle);
            throw new Exception("Shader program link failed: " + log);
        }

        GL.DetachShader(Handle, vs);
        GL.DetachShader(Handle, fs);
        GL.DeleteShader(vs);
        GL.DeleteShader(fs);
    }

    public void Use() => GL.UseProgram(Handle);

    public void SetMatrix4(string name, Matrix4 value)
    {
        int loc = GL.GetUniformLocation(Handle, name);
        GL.UniformMatrix4(loc, false, ref value);
    }

    public void SetVector3(string name, Vector3 value)
    {
        int loc = GL.GetUniformLocation(Handle, name);
        GL.Uniform3(loc, value);
    }

    public void Dispose()
    {
        GL.DeleteProgram(Handle);
    }
}

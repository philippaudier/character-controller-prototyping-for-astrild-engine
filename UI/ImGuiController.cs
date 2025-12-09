using System;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace AstrildCCSandbox.UI;

/// <summary>
/// ImGui controller with an OpenGL renderer for ImGui.NET and basic input mapping from OpenTK.
/// - Builds a font atlas texture
/// - Creates a small GL shader to draw ImGui draw lists
/// - Uploads VBO/IBO each frame and issues draw calls with scissor
/// - Provides UpdateIO to map mouse/keyboard state into ImGui IO
/// This is a functional, small renderer suitable for prototyping.
/// </summary>
public class ImGuiController : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ImDrawVert
    {
        public System.Numerics.Vector2 pos; // 8 bytes
        public System.Numerics.Vector2 uv;  // 8 bytes
        public uint col;    // 4 bytes
    }

    private readonly int _width;
    private readonly int _height;

    private int _fontTexture;
    private int _vbo;
    private int _ibo;
    private int _vao;
    private int _shader;
    private int _attribLocationTex;
    private int _attribLocationProjMtx;

    public ImGuiController(int width, int height)
    {
        _width = width;
        _height = height;

        ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
        io.Fonts.AddFontDefault();

        // Build font atlas and upload as GL texture
        {
            IntPtr pixelsPtr;
            int texW, texH, bytesPerPixel;
            io.Fonts.GetTexDataAsRGBA32(out pixelsPtr, out texW, out texH, out bytesPerPixel);
            _fontTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _fontTexture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, texW, texH, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixelsPtr);
            io.Fonts.SetTexID((IntPtr)_fontTexture);
            io.Fonts.ClearTexData();
        }

        // Create buffers
        _vbo = GL.GenBuffer();
        _ibo = GL.GenBuffer();
        _vao = GL.GenVertexArray();

        // Create simple shader for ImGui
        const string vertexSrc = "#version 330 core\nlayout(location=0) in vec2 Position; layout(location=1) in vec2 UV; layout(location=2) in vec4 Color; uniform mat4 u_ProjMtx; out vec2 Frag_UV; out vec4 Frag_Color; void main() { Frag_UV = UV; Frag_Color = Color; gl_Position = u_ProjMtx * vec4(Position, 0, 1); }";
        const string fragmentSrc = "#version 330 core\nprecision mediump float; in vec2 Frag_UV; in vec4 Frag_Color; uniform sampler2D Texture; out vec4 Out_Color; void main() { Out_Color = Frag_Color * texture(Texture, Frag_UV); }";

        int vs = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vs, vertexSrc);
        GL.CompileShader(vs);
        GL.GetShader(vs, ShaderParameter.CompileStatus, out int ok);
        if (ok == 0) throw new Exception("ImGui VS compile: " + GL.GetShaderInfoLog(vs));

        int fs = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fs, fragmentSrc);
        GL.CompileShader(fs);
        GL.GetShader(fs, ShaderParameter.CompileStatus, out ok);
        if (ok == 0) throw new Exception("ImGui FS compile: " + GL.GetShaderInfoLog(fs));

        _shader = GL.CreateProgram();
        GL.AttachShader(_shader, vs);
        GL.AttachShader(_shader, fs);
        GL.LinkProgram(_shader);
        GL.GetProgram(_shader, GetProgramParameterName.LinkStatus, out ok);
        if (ok == 0) throw new Exception("ImGui program link: " + GL.GetProgramInfoLog(_shader));
        GL.DetachShader(_shader, vs); GL.DetachShader(_shader, fs); GL.DeleteShader(vs); GL.DeleteShader(fs);

        _attribLocationTex = GL.GetUniformLocation(_shader, "Texture");
        _attribLocationProjMtx = GL.GetUniformLocation(_shader, "u_ProjMtx");

        // Setup VAO (attributes fixed during render)
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.EnableVertexAttribArray(0);
        GL.EnableVertexAttribArray(1);
        GL.EnableVertexAttribArray(2);
        // Position (2 floats) offset 0
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, Marshal.SizeOf<ImDrawVert>(), 0);
        // UV (2 floats) offset 8
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, Marshal.SizeOf<ImDrawVert>(), 8);
        // Color (4 unsigned byte) offset 16
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, Marshal.SizeOf<ImDrawVert>(), 16);
        // Bind element array buffer to the VAO so that EBO is part of VAO state
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ibo);
        GL.BindVertexArray(0);
    }

    public void WindowResized(int width, int height)
    {
        ImGui.GetIO().DisplaySize = new System.Numerics.Vector2(width, height);
    }

    /// <summary>
    /// Map OpenTK window input into ImGui IO. Call once per frame before BeginFrame.
    /// </summary>
    public void UpdateIO(GameWindow wnd)
    {
        var io = ImGui.GetIO();
        var kb = wnd.KeyboardState;
        var mouse = wnd.MouseState;

        io.DisplaySize = new System.Numerics.Vector2(wnd.Size.X, wnd.Size.Y);
        io.DeltaTime = 1f / 60f;

        io.MousePos = new System.Numerics.Vector2(mouse.Position.X, mouse.Position.Y);
        io.MouseDown[0] = mouse.IsButtonDown(MouseButton.Left);
        io.MouseDown[1] = mouse.IsButtonDown(MouseButton.Right);
        io.MouseDown[2] = mouse.IsButtonDown(MouseButton.Middle);
        io.MouseWheel = mouse.Scroll.Y;

        io.KeyCtrl = kb.IsKeyDown(Keys.LeftControl) || kb.IsKeyDown(Keys.RightControl);
        io.KeyShift = kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift);
        io.KeyAlt = kb.IsKeyDown(Keys.LeftAlt) || kb.IsKeyDown(Keys.RightAlt);
        io.KeySuper = kb.IsKeyDown(Keys.LeftSuper) || kb.IsKeyDown(Keys.RightSuper);

        // Note: We avoid using KeyMap/KeysDown APIs (ImGui.NET versions differ).
        // Basic modifier and mouse state is enough for mouse-driven ImGui widgets
        // (sliders, checkboxes, buttons). Text input and full keyboard navigation
        // can be added by forwarding TextInput events and mapping keys explicitly.
    }

    /// <summary>
    /// Start a new ImGui frame. Call after UpdateIO.
    /// </summary>
    public void BeginFrame(float deltaSeconds, int width, int height)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new System.Numerics.Vector2(width, height);
        io.DeltaTime = deltaSeconds > 0 ? deltaSeconds : (1f / 60f);
        ImGui.NewFrame();
    }

    /// <summary>
    /// Render ImGui draw lists using OpenGL.
    /// </summary>
    public void Render()
    {
        ImGui.Render();
        var drawData = ImGui.GetDrawData();
        if (drawData.CmdListsCount == 0)
            return;

        GL.Enable(EnableCap.Blend);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.ScissorTest);

        // Setup viewport
        GL.Viewport(0, 0, (int)drawData.DisplaySize.X, (int)drawData.DisplaySize.Y);

        GL.UseProgram(_shader);
        GL.Uniform1(_attribLocationTex, 0);

        // Setup orthographic projection matrix into uniform
        float L = 0.0f;
        float R = drawData.DisplaySize.X;
        float T = 0.0f;
        float B = drawData.DisplaySize.Y;
        var proj = Matrix4.CreateOrthographicOffCenter(L, R, B, T, -1.0f, 1.0f);
        GL.UniformMatrix4(_attribLocationProjMtx, false, ref proj);

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];
            int vtxSize = cmdList.VtxBuffer.Size * Marshal.SizeOf<ImDrawVert>();
            // Detect index size used by ImGui (16-bit or 32-bit)
            IntPtr idxPtr = cmdList.IdxBuffer.Data;
            int idxCount = cmdList.IdxBuffer.Size;

            // Heuristic: if number of indices exceeds 65535 use 32-bit indices, otherwise 16-bit
            int indexByteSize = (idxCount > 0 && cmdList.VtxBuffer.Size > 65535) ? 4 : 2;
            DrawElementsType indexType = indexByteSize == 4 ? DrawElementsType.UnsignedInt : DrawElementsType.UnsignedShort;
            int idxSize = idxCount * indexByteSize;

            // Bind VAO first (EBO is already attached to VAO), then upload vertex/index data
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vtxSize, cmdList.VtxBuffer.Data, BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ibo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, idxSize, cmdList.IdxBuffer.Data, BufferUsageHint.DynamicDraw);

            int idxOffset = 0;
            for (int cmdi = 0; cmdi < cmdList.CmdBuffer.Size; cmdi++)
            {
                var pcmd = cmdList.CmdBuffer[cmdi];
                // per-command draw
                GL.BindTexture(TextureTarget.Texture2D, (int)pcmd.TextureId);
                int x = (int)pcmd.ClipRect.X;
                int y = (int)(drawData.DisplaySize.Y - pcmd.ClipRect.W);
                int w = (int)(pcmd.ClipRect.Z - pcmd.ClipRect.X);
                int h = (int)(pcmd.ClipRect.W - pcmd.ClipRect.Y);
                if (w > 0 && h > 0)
                {
                    GL.Scissor(x, y, w, h);
                    GL.DrawElements(PrimitiveType.Triangles, (int)pcmd.ElemCount, indexType, (IntPtr)(idxOffset * indexByteSize));
                }
                idxOffset += (int)pcmd.ElemCount;
            }
        }

        GL.Disable(EnableCap.ScissorTest);
        GL.Disable(EnableCap.Blend);
        GL.Enable(EnableCap.DepthTest);
    }

    /// <summary>
    /// Add a unicode character to ImGui's input queue (useful for text input events).
    /// </summary>
    public void AddInputCharacter(char c)
    {
        ImGui.GetIO().AddInputCharacter(c);
    }

    public void Dispose()
    {
        if (_vbo != 0) GL.DeleteBuffer(_vbo);
        if (_ibo != 0) GL.DeleteBuffer(_ibo);
        if (_vao != 0) GL.DeleteVertexArray(_vao);
        if (_fontTexture != 0) GL.DeleteTexture(_fontTexture);
        if (_shader != 0) GL.DeleteProgram(_shader);
        ImGui.DestroyContext();
    }
}

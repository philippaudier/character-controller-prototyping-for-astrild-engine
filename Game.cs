using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using AstrildCCSandbox.Rendering;
using AstrildCCSandbox.Character;
using AstrildCCSandbox.UI;
using AstrildCCSandbox.Scene;

namespace AstrildCCSandbox;

/// <summary>
/// Main game window. Hosts a simple OpenGL scene and an ImGui overlay.
/// This is a prototype to exercise a kinematic character controller.
/// Not intended to be production-quality physics.
/// </summary>
public class Game : GameWindow
{
    private SimpleShader _shader = null!;
    private int _groundVao, _groundVbo, _groundEbo, _groundIndexCount;
    private int _cubeVao, _cubeVbo, _cubeEbo, _cubeIndexCount;
    private int _sphereVao, _sphereVbo, _sphereEbo, _sphereIndexCount;

    private CharacterControllerData _ccData = null!;
    private CharacterMotor _motor = null!;

    // Lighting and visual parameters (tweakable via ImGui)
    private Vector3 _lightDirection = new Vector3(0.3f, -1.0f, 0.2f);
    private Vector3 _ambientColor = new Vector3(0.2f, 0.2f, 0.25f);
    private Vector3 _groundColor = new Vector3(0.2f, 0.6f, 0.2f);
    private Vector3 _playerColor = new Vector3(0.8f, 0.2f, 0.2f);

    private Vector3 _cameraPos = new(0, 2, 6);
    private Vector3 _cameraTarget = Vector3.Zero;
    // Orbit camera state
    private float _orbitYaw = 0f;
    private float _orbitPitch = 15f;
    private float _orbitDistance = 6f;
    private Vector2 _lastMousePos;
    private bool _firstMouse = true;

    private ImGuiController _imgui = null!;
    private List<SceneSurface> _scene = new List<SceneSurface>();
    private float _sceneScale = 1.0f;

    public Game()
        : base(GameWindowSettings.Default, new NativeWindowSettings
        {
            ClientSize = new Vector2i(1280, 720),
            Title = "Astrild CC Sandbox"
        })
    {
        VSync = VSyncMode.On;
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        GL.ClearColor(0.1f, 0.1f, 0.15f, 1f);
        GL.Enable(EnableCap.DepthTest);

        // Lambert-style shader (positions + normals)
        const string vs = @"#version 330 core
    layout(location = 0) in vec3 aPosition;
    layout(location = 1) in vec3 aNormal;

    uniform mat4 u_Model;
    uniform mat4 u_View;
    uniform mat4 u_Proj;

    out vec3 vNormal;
    out vec3 vWorldPos;

    void main()
    {
        vec4 worldPos = u_Model * vec4(aPosition, 1.0);
        vWorldPos = worldPos.xyz;
        vNormal = mat3(u_Model) * aNormal;
        gl_Position = u_Proj * u_View * worldPos;
    } ";

        const string fs = @"#version 330 core
    in vec3 vNormal;
    in vec3 vWorldPos;

    uniform vec3 u_Color;
    uniform vec3 u_LightDir;
    uniform vec3 u_AmbientColor;

    out vec4 fColor;

    void main()
    {
        vec3 N = normalize(vNormal);
        vec3 L = normalize(-u_LightDir);
        float NdotL = max(dot(N, L), 0.0);

        vec3 baseColor = u_Color;
        vec3 ambient = u_AmbientColor * baseColor;
        vec3 diffuse = NdotL * baseColor;

        vec3 finalColor = ambient + diffuse;
        fColor = vec4(finalColor, 1.0);
    }
    ";

        _shader = new SimpleShader(vs, fs);

        // Create geometry
        (_groundVao, _groundVbo, _groundEbo, _groundIndexCount) = MeshPrimitives.CreateGroundQuad();
        (_cubeVao, _cubeVbo, _cubeEbo, _cubeIndexCount) = MeshPrimitives.CreateUnitCube();
        (_sphereVao, _sphereVbo, _sphereEbo, _sphereIndexCount) = MeshPrimitives.CreateUVSphere(1f, 12, 20);

        _ccData = new CharacterControllerData();
        _motor = new CharacterMotor(_ccData);
        // provide a scene-ground query to the motor so it can detect platforms/ramps
        _motor.GroundQuery = QuerySceneHeight;

        // ImGui controller
        _imgui = new ImGuiController(Size.X, Size.Y);

        // Build an obstacle course scaled to the controller height
        // Increase base scene scale slightly so obstacles feel larger for testing
        _sceneScale = (_ccData.Height / 1.8f) * 1.25f; // base scene was designed for 1.8m
        BuildScene(_sceneScale);
    }

    private void BuildScene(float scale)
    {
        _scene.Clear();
        _scene.Add(new FlatPlatform(new Vector3(3f, 0.5f, 0f) * scale, 3f * scale, 3f * scale, 0.5f * scale));
        _scene.Add(new FlatPlatform(new Vector3(-4f, 0.25f, 1.5f) * scale, 2f * scale, 2f * scale, 0.25f * scale));
        _scene.Add(new Ramp(new Vector3(0f, 0f, -4f) * scale, 3f * scale, 4f * scale, 0f * scale, 1.5f * scale, 0f));
        _scene.Add(new Staircase(new Vector3(-6f, 0f, -3f) * scale, 1.2f * scale, 6, 0.35f * scale, 0.25f * scale, 45f));
        _scene.Add(new MovingPlatform(new Vector3(-2f, 0.5f, 2f) * scale, new Vector3(2f, 0.5f, 2f) * scale, 1.6f * scale, 1.6f * scale, 0.2f * scale, 0f, 3f));
        // narrow beam
        _scene.Add(new FlatPlatform(new Vector3(0f, 0.8f, 4f) * scale, 0.25f * scale, 6f * scale, 0.8f * scale));
        // a couple of scattered small obstacles
        _scene.Add(new FlatPlatform(new Vector3(5f, 0.2f, -3f) * scale, 0.6f * scale, 0.6f * scale, 0.2f * scale));
        _scene.Add(new FlatPlatform(new Vector3(1.5f, 1.2f, -6f) * scale, 2f * scale, 2f * scale, 1.2f * scale));
        // rounded test obstacles
        _scene.Add(new Bump(new Vector3(2.5f, 0.25f * scale, -1.5f) * scale, 0.6f * scale));
        _scene.Add(new Bump(new Vector3(-1.5f, 0.25f * scale, -2.5f) * scale, 0.4f * scale));
        _scene.Add(new SphereObstacle(new Vector3(4f, 0.6f * scale, 2.5f) * scale, 0.6f * scale));
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, Size.X, Size.Y);
        _imgui?.WindowResized(Size.X, Size.Y);
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        float dt = (float)args.Time;

        // Update ImGui IO (mouse/keyboard state) before processing input
        _imgui.UpdateIO(this);

        if (!IsFocused)
            return;
        var kb = KeyboardState;
        if (kb.IsKeyDown(Keys.Escape))
            Close();

        // Build 2D input (X, Z)
        Vector2 input = Vector2.Zero;
        if (kb.IsKeyDown(Keys.W)) input.Y += 1f;
        if (kb.IsKeyDown(Keys.S)) input.Y -= 1f;
        if (kb.IsKeyDown(Keys.A)) input.X -= 1f;
        if (kb.IsKeyDown(Keys.D)) input.X += 1f;
        if (input.LengthSquared > 0.0001f)
            input = input.Normalized();

        bool jumpPressed = kb.IsKeyPressed(Keys.Space);

        // Convert input into world XZ movement relative to orbital camera yaw
        float yawRad = MathHelper.DegreesToRadians(_orbitYaw);
        var forward = new Vector3((float)Math.Sin(yawRad), 0f, (float)Math.Cos(yawRad));
        // right should be perpendicular to forward; compute via cross(forward, up)
        var right = new Vector3(-forward.Z, 0f, forward.X);
        float worldX = right.X * input.X + forward.X * input.Y;
        float worldZ = right.Z * input.X + forward.Z * input.Y;
        Vector2 worldInput = new Vector2(worldX, worldZ);
        if (worldInput.LengthSquared > 0.0001f)
            worldInput = worldInput.Normalized();

        // update moving surfaces first so motor queries see current positions
        foreach (var s in _scene)
            s.Update(dt);

        _motor.Update(dt, worldInput, jumpPressed);

        // after motor update, resolve lateral collisions and transport by moving platforms
        HandleSceneCollisionsAndTransport(dt);

        // Orbit camera: update from mouse if right button is held
        var ms = MouseState;
        var pos = ms.Position;
        if (_firstMouse)
        {
            _lastMousePos = pos;
            _firstMouse = false;
        }
        if (ms.IsButtonDown(OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Right) && !ImGuiNET.ImGui.GetIO().WantCaptureMouse)
        {
            var delta = pos - _lastMousePos;
            float sensitivity = 0.15f;
            // invert horizontal rotation so dragging mouse right rotates camera right
            _orbitYaw -= delta.X * sensitivity;
            _orbitPitch -= delta.Y * sensitivity;
            _orbitPitch = MathHelper.Clamp(_orbitPitch, -89f, 89f);
        }
        _lastMousePos = pos;

        // ImGui frame will be started at the beginning of the render pass.
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);

        float dt = (float)args.Time;

        // Start ImGui frame
        _imgui.BeginFrame(dt, Size.X, Size.Y);

        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        float aspect = Size.X / (float)Size.Y;
        Matrix4 proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60f), aspect, 0.1f, 100f);

        // compute camera position from orbital parameters
        float yawRad = MathHelper.DegreesToRadians(_orbitYaw);
        float pitchRad = MathHelper.DegreesToRadians(_orbitPitch);
        var dir = new Vector3(
            (float)(Math.Cos(pitchRad) * Math.Sin(yawRad)),
            (float)Math.Sin(pitchRad),
            (float)(Math.Cos(pitchRad) * Math.Cos(yawRad))
        );
        _cameraTarget = _motor.Position + new Vector3(0f, 0.9f, 0f);
        _cameraPos = _motor.Position - dir * _orbitDistance + new Vector3(0f, 0.5f, 0f);
        Matrix4 view = Matrix4.LookAt(_cameraPos, _cameraTarget, Vector3.UnitY);

        // Render ground with lighting
        _shader.Use();
        // set common matrices
        _shader.SetMatrix4("u_View", view);
        _shader.SetMatrix4("u_Proj", proj);
        _shader.SetVector3("u_LightDir", _lightDirection);
        _shader.SetVector3("u_AmbientColor", _ambientColor);

        // ground
        Matrix4 model = Matrix4.Identity;
        _shader.SetMatrix4("u_Model", model);
        _shader.SetVector3("u_Color", _groundColor);
        GL.BindVertexArray(_groundVao);
        GL.DrawElements(PrimitiveType.Triangles, _groundIndexCount, DrawElementsType.UnsignedInt, 0);

        // render scene objects (platforms, ramps, stairs)
        GL.BindVertexArray(_cubeVao);
        foreach (var s in _scene)
        {
            if (s is FlatPlatform fp)
            {
                float thickness = 0.2f;
                var scaleMat = Matrix4.CreateScale(fp.SizeX, thickness, fp.SizeZ);
                var rotYMat = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(fp.RotationYDeg));
                var transMat = Matrix4.CreateTranslation(fp.Center.X, fp.TopY + thickness * 0.5f, fp.Center.Z);
                model = scaleMat * rotYMat * transMat;
                _shader.SetMatrix4("u_Model", model);
                _shader.SetVector3("u_Color", new Vector3(0.6f, 0.6f, 0.6f));
                GL.DrawElements(PrimitiveType.Triangles, _cubeIndexCount, DrawElementsType.UnsignedInt, 0);
            }
            else if (s is Ramp r)
            {
                float thickness = 0.2f;
                // slope angle along local Z
                float slope = r.EndY - r.StartY;
                float angle = MathF.Atan2(slope, r.SizeZ);
                var rotXMat = Matrix4.CreateRotationX(-angle);
                var rotYMat = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(r.RotationYDeg));
                // center Y approx average
                float centerY = (r.StartY + r.EndY) * 0.5f;
                var scaleMat = Matrix4.CreateScale(r.SizeX, thickness, r.SizeZ);
                var transMat = Matrix4.CreateTranslation(r.Center.X, centerY + thickness * 0.5f, r.Center.Z);
                model = scaleMat * rotXMat * rotYMat * transMat;
                _shader.SetMatrix4("u_Model", model);
                _shader.SetVector3("u_Color", new Vector3(0.45f, 0.35f, 0.25f));
                GL.DrawElements(PrimitiveType.Triangles, _cubeIndexCount, DrawElementsType.UnsignedInt, 0);
            }
            else if (s is Staircase st)
            {
                float thickness = 0.18f;
                foreach (var step in st.Steps)
                {
                    var scaleMat = Matrix4.CreateScale(step.SizeX, thickness, step.SizeZ);
                    var rotYMat = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(step.RotationYDeg));
                    var transMat = Matrix4.CreateTranslation(step.Center.X, step.TopY + thickness * 0.5f, step.Center.Z);
                    model = scaleMat * rotYMat * transMat;
                    _shader.SetMatrix4("u_Model", model);
                    _shader.SetVector3("u_Color", new Vector3(0.5f, 0.4f, 0.35f));
                    GL.DrawElements(PrimitiveType.Triangles, _cubeIndexCount, DrawElementsType.UnsignedInt, 0);
                }
            }
            else if (s is Scene.Bump bp)
            {
                // render hemisphere as sphere scaled to radius, positioned at center
                var scaleMat = Matrix4.CreateScale(bp.Radius);
                var transMat = Matrix4.CreateTranslation(bp.Center);
                model = scaleMat * transMat;
                _shader.SetMatrix4("u_Model", model);
                _shader.SetVector3("u_Color", new Vector3(0.7f, 0.3f, 0.3f));
                GL.BindVertexArray(_sphereVao);
                GL.DrawElements(PrimitiveType.Triangles, _sphereIndexCount, DrawElementsType.UnsignedInt, 0);
            }
            else if (s is Scene.SphereObstacle so)
            {
                var scaleMat = Matrix4.CreateScale(so.Radius);
                var transMat = Matrix4.CreateTranslation(so.Center);
                model = scaleMat * transMat;
                _shader.SetMatrix4("u_Model", model);
                _shader.SetVector3("u_Color", new Vector3(0.3f, 0.6f, 0.8f));
                GL.BindVertexArray(_sphereVao);
                GL.DrawElements(PrimitiveType.Triangles, _sphereIndexCount, DrawElementsType.UnsignedInt, 0);
            }
            // Also perform capsule-vs-OBB resolution for each flat platform
            if (s is FlatPlatform obb)
            {
                // OBB parameters: use a thin box representing the platform top area
                float platThickness = 0.2f;
                Vector3 obbCenter = new Vector3(obb.Center.X, obb.TopY - platThickness * 0.5f, obb.Center.Z);
                Vector3 halfExtents = new Vector3(obb.SizeX * 0.5f, platThickness * 0.5f, obb.SizeZ * 0.5f);
                float rotY = obb.RotationYDeg;

                // capsule segment (in world space)
                float cylHalf = MathF.Max(0f, (_ccData.Height * 0.5f) - _ccData.Radius);
                var segA = _ccData.Position + new Vector3(0f, cylHalf, 0f);
                var segB = _ccData.Position - new Vector3(0f, cylHalf, 0f);
                // Quick vertical overlap check: skip OBB test if capsule is fully below platform bottom
                float capTop = _ccData.Position.Y + cylHalf;
                float platformBottom = obb.TopY - platThickness;
                if (capTop <= platformBottom - 0.01f)
                {
                    // capsule is entirely below platform bottom: allow passing under
                }
                else if (CapsuleIntersectsOBB(segA, segB, _ccData.Radius, obbCenter, halfExtents, rotY, out Vector3 mtv))
                {
                    // If the capsule is mostly below the platform bottom, avoid pushing it upward
                    // which would place the player on top of the platform when they actually
                    // came from underneath. Prefer a downward or lateral-only resolution.
                    float capBottomLocal = _ccData.Position.Y - (_ccData.Height * 0.5f);
                    if (capBottomLocal < platformBottom - 0.02f && mtv.Y > 0f)
                    {
                        mtv.Y = Math.Min(0f, mtv.Y);
                        if (Math.Abs(mtv.X) < 1e-6f && Math.Abs(mtv.Z) < 1e-6f)
                        {
                            // if mtv is purely vertical upward, convert to a downward push
                            float capTopLocal = _ccData.Position.Y + cylHalf;
                            float down = capTopLocal - platformBottom + 0.001f;
                            mtv.Y = -down;
                        }
                    }
                    _ccData.Position += mtv;
                }
            }
        }

        // Final ceiling/overhang resolution: ensure the character does not end up with its
        // capsule top penetrating a nearby surface (e.g. low ceiling or underside of a ramp).
        // We perform this after all lateral/ramp/sphere resolutions so the final position
        // respects both ground and ceiling constraints.
        {
            float half2 = _ccData.Height * 0.5f;
            float finalCapBottom = _ccData.Position.Y - half2;
            float finalCapTop = _ccData.Position.Y + half2;
            var ceiling = QuerySceneHeight(new Vector2(_ccData.Position.X, _ccData.Position.Z), finalCapTop);
            if (ceiling != null)
            {
                float surfaceH = ceiling.Value.height;
                if (surfaceH > finalCapBottom + 1e-4f)
                {
                    float allowedTop = surfaceH - _ccData.SkinWidth;
                    if (finalCapTop > allowedTop)
                    {
                        float downPen = finalCapTop - allowedTop;
                        var p = _ccData.Position;
                        p.Y -= downPen;
                        _ccData.Position = p;
                        if (_ccData.Velocity.Y > 0f)
                        {
                            var v = _ccData.Velocity;
                            v.Y = 0f;
                            _ccData.Velocity = v;
                        }
                        _ccData.IsGrounded = false;
                    }
                }
            }
        }

        // player cube (stretched to approximate capsule)
        Matrix4 scale = Matrix4.CreateScale(_ccData.Radius * 2f, _ccData.Height, _ccData.Radius * 2f);
        Matrix4 rotation = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(_ccData.Yaw));
        model = scale * rotation * Matrix4.CreateTranslation(_motor.Position);
        _shader.SetMatrix4("u_Model", model);
        _shader.SetVector3("u_Color", _playerColor);
        GL.BindVertexArray(_cubeVao);
        GL.DrawElements(PrimitiveType.Triangles, _cubeIndexCount, DrawElementsType.UnsignedInt, 0);
        // ImGui overlay: Character Controller inspector + lighting
        // ImGui overlay: Character Controller inspector + lighting
        ImGuiNET.ImGui.SetNextWindowSize(new System.Numerics.Vector2(360, 480), ImGuiNET.ImGuiCond.FirstUseEver);
        ImGuiNET.ImGui.Begin("Character Controller");
        if (ImGuiNET.ImGui.CollapsingHeader("Parameters", ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
        {
            float tmpRadius = _ccData.Radius;
            if (ImGuiNET.ImGui.SliderFloat("Radius", ref tmpRadius, 0.1f, 1.0f)) _ccData.Radius = tmpRadius;
            float tmpHeight = _ccData.Height;
            if (ImGuiNET.ImGui.SliderFloat("Height", ref tmpHeight, 0.5f, 3.0f)) _ccData.Height = tmpHeight;
            ImGuiNET.ImGui.Separator();

            float tmpMaxWalk = _ccData.MaxWalkSpeed;
            if (ImGuiNET.ImGui.SliderFloat("Max Walk Speed", ref tmpMaxWalk, 0.5f, 20.0f)) _ccData.MaxWalkSpeed = tmpMaxWalk;
            float tmpMaxRun = _ccData.MaxRunSpeed;
            if (ImGuiNET.ImGui.SliderFloat("Max Run Speed", ref tmpMaxRun, 1.0f, 30.0f)) _ccData.MaxRunSpeed = tmpMaxRun;
            float tmpAccel = _ccData.Acceleration;
            if (ImGuiNET.ImGui.SliderFloat("Acceleration", ref tmpAccel, 1.0f, 80.0f)) _ccData.Acceleration = tmpAccel;
            float tmpAirAccel = _ccData.AirAcceleration;
            if (ImGuiNET.ImGui.SliderFloat("Air Acceleration", ref tmpAirAccel, 0.0f, 40.0f)) _ccData.AirAcceleration = tmpAirAccel;
            float tmpGravity = _ccData.Gravity;
            if (ImGuiNET.ImGui.SliderFloat("Gravity", ref tmpGravity, 1.0f, 50.0f)) _ccData.Gravity = tmpGravity;
            float tmpMaxFall = _ccData.MaxFallSpeed;
            if (ImGuiNET.ImGui.SliderFloat("Max Fall Speed", ref tmpMaxFall, 1.0f, 100.0f)) _ccData.MaxFallSpeed = tmpMaxFall;
            float tmpSnap = _ccData.GroundSnapDistance;
            if (ImGuiNET.ImGui.SliderFloat("Ground Snap Distance", ref tmpSnap, 0.0f, 1.0f)) _ccData.GroundSnapDistance = tmpSnap;
            float tmpSlope = _ccData.MaxSlopeAngle;
            if (ImGuiNET.ImGui.SliderFloat("Max Slope Angle", ref tmpSlope, 0.0f, 80.0f)) _ccData.MaxSlopeAngle = tmpSlope;
            float tmpStep = _ccData.StepOffset;
            if (ImGuiNET.ImGui.SliderFloat("Step Offset", ref tmpStep, 0.0f, 1.0f)) _ccData.StepOffset = tmpStep;
            float tmpSkin = _ccData.SkinWidth;
            if (ImGuiNET.ImGui.SliderFloat("Skin Width", ref tmpSkin, 0.0f, 0.2f)) _ccData.SkinWidth = tmpSkin;
            ImGuiNET.ImGui.Separator();
            float tmpJump = _ccData.JumpSpeed;
            if (ImGuiNET.ImGui.SliderFloat("Jump Speed", ref tmpJump, 0.1f, 20.0f)) _ccData.JumpSpeed = tmpJump;
            float tmpCoyote = _ccData.CoyoteTime;
            if (ImGuiNET.ImGui.SliderFloat("Coyote Time", ref tmpCoyote, 0.0f, 0.5f)) _ccData.CoyoteTime = tmpCoyote;
            float tmpBuffer = _ccData.JumpBufferTime;
            if (ImGuiNET.ImGui.SliderFloat("Jump Buffer Time", ref tmpBuffer, 0.0f, 0.5f)) _ccData.JumpBufferTime = tmpBuffer;
        }

        if (ImGuiNET.ImGui.CollapsingHeader("Runtime", ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGuiNET.ImGui.Text($"IsGrounded: {_ccData.IsGrounded}");
            ImGuiNET.ImGui.Text($"Position: {_ccData.Position}");
            ImGuiNET.ImGui.Text($"Velocity: {_ccData.Velocity}");
            ImGuiNET.ImGui.Text($"GroundNormal: {_ccData.GroundNormal}");
            ImGuiNET.ImGui.Text($"TimeSinceLastGrounded: {_ccData.TimeSinceLastGrounded:0.000}");
            ImGuiNET.ImGui.Text($"TimeSinceJumpPressed: {_ccData.TimeSinceJumpPressed:0.000}");
        }

        if (ImGuiNET.ImGui.CollapsingHeader("Lighting", ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
        {
            var lightDir = _lightDirection;
            System.Numerics.Vector3 tmp = new System.Numerics.Vector3(lightDir.X, lightDir.Y, lightDir.Z);
            if (ImGuiNET.ImGui.DragFloat3("Light Direction", ref tmp, 0.01f))
            {
                _lightDirection = new Vector3(tmp.X, tmp.Y, tmp.Z);
            }

            var amb = _ambientColor;
            System.Numerics.Vector3 ambTmp = new System.Numerics.Vector3(amb.X, amb.Y, amb.Z);
            if (ImGuiNET.ImGui.ColorEdit3("Ambient Color", ref ambTmp))
            {
                _ambientColor = new Vector3(ambTmp.X, ambTmp.Y, ambTmp.Z);
            }

            var groundCol = _groundColor;
            System.Numerics.Vector3 gTmp = new System.Numerics.Vector3(groundCol.X, groundCol.Y, groundCol.Z);
            if (ImGuiNET.ImGui.ColorEdit3("Ground Color", ref gTmp))
            {
                _groundColor = new Vector3(gTmp.X, gTmp.Y, gTmp.Z);
            }

            var playerCol = _playerColor;
            System.Numerics.Vector3 pTmp = new System.Numerics.Vector3(playerCol.X, playerCol.Y, playerCol.Z);
            if (ImGuiNET.ImGui.ColorEdit3("Player Color", ref pTmp))
            {
                _playerColor = new Vector3(pTmp.X, pTmp.Y, pTmp.Z);
            }
        }
        if (ImGuiNET.ImGui.CollapsingHeader("Scene", ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
        {
            float tmpScale = _sceneScale;
            if (ImGuiNET.ImGui.SliderFloat("Scene Scale", ref tmpScale, 0.5f, 3.0f))
            {
                _sceneScale = tmpScale;
                BuildScene(_sceneScale);
            }
        }

        ImGuiNET.ImGui.End();
        // Render ImGui (frame was started in OnUpdateFrame)
        _imgui.Render();

        SwapBuffers();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        // Zoom in/out
        _orbitDistance = MathHelper.Clamp(_orbitDistance - (float)e.OffsetY, 1f, 50f);
    }

    protected override void OnUnload()
    {
        base.OnUnload();

        GL.DeleteVertexArray(_groundVao);
        GL.DeleteBuffer(_groundVbo);
        GL.DeleteBuffer(_groundEbo);

        GL.DeleteVertexArray(_cubeVao);
        GL.DeleteBuffer(_cubeVbo);
        GL.DeleteBuffer(_cubeEbo);
        GL.DeleteVertexArray(_sphereVao);
        GL.DeleteBuffer(_sphereVbo);
        GL.DeleteBuffer(_sphereEbo);

        _shader.Dispose();
        _imgui.Dispose();
    }

    // Query the scene for the highest surface at or below the given maxSearchHeight.
    // If maxSearchHeight is +Infinity, behaves like previous QuerySceneHeight.
    // Returns null if no surface found (motor should fall back to Y=0).
    private (float height, Vector3 normal)? QuerySceneHeight(Vector2 xz, float maxSearchHeight)
    {
        float bestH = float.MinValue;
        Vector3 bestN = Vector3.UnitY;
        bool found = false;
        foreach (var s in _scene)
        {
            if (s.TryGetHeight(xz, out float h, out Vector3 n))
            {
                if (h <= maxSearchHeight && (!found || h > bestH))
                {
                    bestH = h;
                    bestN = n;
                    found = true;
                }
            }
        }
        if (found) return (bestH, bestN);
        return null;
    }

    // More detailed scene query: returns height, normal, surface velocity and the surface reference.
    private (float height, Vector3 normal, Vector3 velocity, SceneSurface? surface)? QuerySceneSurface(Vector2 xz)
    {
        float bestH = float.MinValue;
        Vector3 bestN = Vector3.UnitY;
        Vector3 bestV = Vector3.Zero;
        SceneSurface? bestS = null;
        bool found = false;
        foreach (var s in _scene)
        {
            if (s.TryGetHeight(xz, out float h, out Vector3 n))
            {
                if (!found || h > bestH)
                {
                    bestH = h;
                    bestN = n;
                    found = true;
                    bestS = s;
                    if (s is Scene.MovingPlatform mp)
                        bestV = mp.Velocity;
                    else
                        bestV = Vector3.Zero;
                }
            }
        }
        if (found) return (bestH, bestN, bestV, bestS);
        return null;
    }

    // Improved capsule-vs-OBB test using a refined closest-point computation between the capsule segment and the box.
    // Returns true and the minimum translation vector (in world space) to separate the capsule from the OBB when they overlap.
    private static bool CapsuleIntersectsOBB(Vector3 segA_world, Vector3 segB_world, float radius, Vector3 obbCenter, Vector3 halfExtents, float rotYDeg, out Vector3 mtvWorld)
    {
        mtvWorld = Vector3.Zero;
        // Transform segment into OBB local space (inverse yaw)
        float yaw = -MathHelper.DegreesToRadians(rotYDeg);
        float cos = (float)Math.Cos(yaw);
        float sin = (float)Math.Sin(yaw);
        Vector3 ToLocal(Vector3 p)
        {
            var v = p - obbCenter;
            return new Vector3(cos * v.X - sin * v.Z, v.Y, sin * v.X + cos * v.Z);
        }
        // rotate vector (no translation)
        Vector3 RotateLocalToWorldVector(Vector3 v)
        {
            float yawF = MathHelper.DegreesToRadians(rotYDeg);
            float c2 = (float)Math.Cos(yawF);
            float s2 = (float)Math.Sin(yawF);
            return new Vector3(c2 * v.X - s2 * v.Z, v.Y, s2 * v.X + c2 * v.Z);
        }

        var a = ToLocal(segA_world);
        var b = ToLocal(segB_world);

        var amin = -halfExtents;
        var amax = halfExtents;

        // helper: closest point on AABB to point p
        static Vector3 ClosestPointOnAABB(in Vector3 p, in Vector3 amin, in Vector3 amax)
        {
            return new Vector3(
                MathF.Max(amin.X, MathF.Min(amax.X, p.X)),
                MathF.Max(amin.Y, MathF.Min(amax.Y, p.Y)),
                MathF.Max(amin.Z, MathF.Min(amax.Z, p.Z))
            );
        }

        // distance squared from point to AABB and closest point out param
        static float DistSqPointAABB(in Vector3 p, in Vector3 amin, in Vector3 amax, out Vector3 closest)
        {
            closest = ClosestPointOnAABB(p, amin, amax);
            var d = p - closest;
            return d.X * d.X + d.Y * d.Y + d.Z * d.Z;
        }

        // Use a ternary search on t in [0,1] to find the point along segment that minimizes distance squared to the AABB.
        // f(t) = distSq( Lerp(a,b,t), AABB ). f is piecewise-smooth and this gives a high-precision result compared to coarse sampling.
        float lo = 0f, hi = 1f;
        float bestT = 0f;
        for (int iter = 0; iter < 40; iter++)
        {
            float t1 = lo + (hi - lo) / 3f;
            float t2 = hi - (hi - lo) / 3f;
            var p1 = Vector3.Lerp(a, b, t1);
            var p2 = Vector3.Lerp(a, b, t2);
            DistSqPointAABB(p1, amin, amax, out _);
            float f1 = DistSqPointAABB(p1, amin, amax, out _);
            float f2 = DistSqPointAABB(p2, amin, amax, out _);
            if (f1 > f2)
                lo = t1;
            else
                hi = t2;
        }
        bestT = (lo + hi) * 0.5f;
        var segP = Vector3.Lerp(a, b, bestT);
        float distSq = DistSqPointAABB(segP, amin, amax, out Vector3 boxClosest);
        float dist = MathF.Sqrt(distSq);

        if (dist > radius + 1e-6f)
        {
            // no collision
            return false;
        }

        // If we have a finite distance, compute classic MTV from boxClosest -> segP
        if (dist > 1e-6f)
        {
            var dirLocal = (segP - boxClosest) / dist;
            var pushLocal = dirLocal * (radius - dist + 0.0001f);
            var pushWorld = RotateLocalToWorldVector(pushLocal);
            mtvWorld = pushWorld;
            return true;
        }

        // Dist is zero (segment touches or penetrates AABB). We must compute a penetration vector.
        // Use the clamped segment point (segP) to determine the nearest face and push outward along that axis.
        // Compute distances to each face from segP
        float dxMin = segP.X - amin.X;
        float dxMax = amax.X - segP.X;
        float dyMin = segP.Y - amin.Y;
        float dyMax = amax.Y - segP.Y;
        float dzMin = segP.Z - amin.Z;
        float dzMax = amax.Z - segP.Z;

        // pick smallest penetration distance to a face
        float minPen = dxMin;
        Vector3 axis = new Vector3(-1f, 0f, 0f);
        if (dxMax < minPen) { minPen = dxMax; axis = new Vector3(1f, 0f, 0f); }
        if (dyMin < minPen) { minPen = dyMin; axis = new Vector3(0f, -1f, 0f); }
        if (dyMax < minPen) { minPen = dyMax; axis = new Vector3(0f, 1f, 0f); }
        if (dzMin < minPen) { minPen = dzMin; axis = new Vector3(0f, 0f, -1f); }
        if (dzMax < minPen) { minPen = dzMax; axis = new Vector3(0f, 0f, 1f); }

        // push in local space by radius + small penetration amount
        var pushLocalPen = axis * (radius + 0.001f);
        mtvWorld = RotateLocalToWorldVector(pushLocalPen);
        return true;
    }

    // Resolve basic lateral penetration with oriented flat platforms and transport standing players on moving platforms.
    private void HandleSceneCollisionsAndTransport(float dt)
    {
        var pos = _ccData.Position;
        Vector2 posXZ = new Vector2(pos.X, pos.Z);

        // First, detect if we're standing on a surface and set platform velocity transport
        _ccData.PlatformVelocity = Vector3.Zero;
        var surface = QuerySceneSurface(posXZ);
        if (surface != null)
        {
            float platformTop = surface.Value.height;
            Vector3 normal = surface.Value.normal;
            Vector3 surfaceVel = surface.Value.velocity;
            _ccData.PlatformVelocity = surfaceVel;
            // if player is grounded and standing on the surface (within a small epsilon), keep transport
            float bottom = pos.Y - (_ccData.Height * 0.5f);
            if (!(_ccData.IsGrounded && Math.Abs(bottom - platformTop) < 0.05f))
            {
                // not standing close enough; zero transport
                _ccData.PlatformVelocity = Vector3.Zero;
            }
        }

        // Then, perform simple lateral collision resolution against flat platforms (and moving ones)
        foreach (var s in _scene)
        {
            if (s is FlatPlatform fp)
            {
                // transform to local platform space (inverse yaw)
                float yaw = MathHelper.DegreesToRadians(-fp.RotationYDeg);
                float cos = (float)Math.Cos(yaw);
                float sin = (float)Math.Sin(yaw);
                var localX = cos * (posXZ.X - fp.Center.X) - sin * (posXZ.Y - fp.Center.Z);
                var localZ = sin * (posXZ.X - fp.Center.X) + cos * (posXZ.Y - fp.Center.Z);

                // find closest point on rectangle
                float halfX = fp.SizeX * 0.5f;
                float halfZ = fp.SizeZ * 0.5f;
                float clampedX = MathF.Max(-halfX, MathF.Min(halfX, localX));
                float clampedZ = MathF.Max(-halfZ, MathF.Min(halfZ, localZ));

                // if player is sufficiently above the top, skip
                float topY = fp.TopY;
                float bottom = _ccData.Position.Y - (_ccData.Height * 0.5f);
                if (bottom > topY + 0.2f) continue;

                // skip lateral collision if the capsule is entirely below the platform bottom (so player can pass under)
                float platThickness = 0.2f;
                float platformBottom = topY - platThickness;
                float capTop = _ccData.Position.Y + MathF.Max(0f, (_ccData.Height * 0.5f) - _ccData.Radius);
                if (capTop <= platformBottom - 0.01f) continue;

                // closest point in world space
                float wy = MathHelper.DegreesToRadians(fp.RotationYDeg);
                float c2 = (float)Math.Cos(wy);
                float s2 = (float)Math.Sin(wy);
                float closestWorldX = c2 * clampedX - s2 * clampedZ + fp.Center.X;
                float closestWorldZ = s2 * clampedX + c2 * clampedZ + fp.Center.Z;

                var delta = new Vector2(posXZ.X - closestWorldX, posXZ.Y - closestWorldZ);
                float dist = delta.Length;
                float radius = _ccData.Radius;
                if (dist < 1e-5f)
                {
                    // push out along vector from platform center
                    float dirX = posXZ.X - fp.Center.X;
                    float dirZ = posXZ.Y - fp.Center.Z;
                    var dir = new Vector2(dirX, dirZ);
                    if (dir.LengthSquared < 1e-6f) dir = new Vector2(0.01f, 0.01f);
                    dir = dir.Normalized();
                    delta = dir * (radius + 0.001f);
                    dist = delta.Length;
                }

                if (dist < radius)
                {
                    float push = radius - dist + 0.001f;
                    var n = delta / dist;
                    _ccData.Position = new Vector3(_ccData.Position.X + n.X * push, _ccData.Position.Y, _ccData.Position.Z + n.Y * push);
                }
            }
            else if (s is Ramp r)
            {
                // Analytical capsule-vs-ramp handling:
                // Compute ramp plane and test closest point on capsule segment to that plane.
                float cylHalf = MathF.Max(0f, (_ccData.Height * 0.5f) - _ccData.Radius);
                var segA = _ccData.Position + new Vector3(0f, cylHalf, 0f);
                var segB = _ccData.Position - new Vector3(0f, cylHalf, 0f);

                // Reconstruct ramp plane in world space (use same points as Ramp.TryGetHeight)
                var p1 = new Vector3(-r.SizeX * 0.5f, MathHelper.Lerp(r.StartY, r.EndY, 0f), -r.SizeZ * 0.5f);
                var p2 = new Vector3(r.SizeX * 0.5f, MathHelper.Lerp(r.StartY, r.EndY, 0f), -r.SizeZ * 0.5f);
                var p3 = new Vector3(-r.SizeX * 0.5f, MathHelper.Lerp(r.StartY, r.EndY, 1f), r.SizeZ * 0.5f);
                var rot = Quaternion.FromAxisAngle(Vector3.UnitY, MathHelper.DegreesToRadians(r.RotationYDeg));
                Vector3 WorldP(in Vector3 local) => rot * local + r.Center;
                var wp1 = WorldP(p1);
                var wp2 = WorldP(p2);
                var wp3 = WorldP(p3);
                var planeNormal = Vector3.Cross(wp2 - wp1, wp3 - wp1).Normalized();
                var planePoint = WorldP(new Vector3(0f, MathHelper.Lerp(r.StartY, r.EndY, 0.5f), 0f));

                // Signed distances from segment endpoints to plane (positive means in direction of normal)
                float da = Vector3.Dot(segA - planePoint, planeNormal);
                float db = Vector3.Dot(segB - planePoint, planeNormal);

                // If both endpoints are well below the plane (more negative than radius), we are passing under the ramp
                if (da <= -_ccData.Radius && db <= -_ccData.Radius)
                    continue;

                // Find closest point on segment to plane (if segment crosses plane, t where distance==0, else endpoint with smaller abs distance)
                float tClosest;
                if (da * db < 0f)
                    tClosest = da / (da - db); // where signed distance is zero
                else
                    tClosest = MathF.Abs(da) < MathF.Abs(db) ? 0f : 1f;

                var pClosest = Vector3.Lerp(segA, segB, tClosest);
                float dist = Vector3.Dot(pClosest - planePoint, planeNormal); // signed

                // If the closest point is below the plane more than radius, skip (we're under)
                if (dist <= -_ccData.Radius) continue;

                // Project the closest point onto the ramp plane and check whether it's within ramp bounds (local XZ inside rectangle)
                var proj = pClosest - planeNormal * dist; // projection on plane

                // transform proj into ramp local space (inverse yaw)
                float yaw = MathHelper.DegreesToRadians(-r.RotationYDeg);
                float cosy = (float)Math.Cos(yaw);
                float siny = (float)Math.Sin(yaw);
                float localX = cosy * (proj.X - r.Center.X) - siny * (proj.Z - r.Center.Z);
                float localZ = siny * (proj.X - r.Center.X) + cosy * (proj.Z - r.Center.Z);

                if (Math.Abs(localX) <= r.SizeX * 0.5f + 1e-5f && Math.Abs(localZ) <= r.SizeZ * 0.5f + 1e-5f)
                {
                    // If the projection is within the ramp rectangle and within penetration range, resolve along normal
                    if (dist < _ccData.Radius)
                    {
                        // Determine capsule vertical extents before applying any push
                        float halfHeight = _ccData.Height * 0.5f;
                        float capBottomPre = _ccData.Position.Y - halfHeight;
                        float capTopPre = _ccData.Position.Y + halfHeight;
                        float projHeight = proj.Y;

                        // If the projected point on the ramp is significantly below the capsule bottom,
                        // we treat the capsule as coming from underneath and skip lifting the capsule onto the ramp.
                        if (projHeight < capBottomPre - 0.05f)
                        {
                            // skip this ramp resolution to avoid teleporting the player beneath the ramp
                            continue;
                        }

                        float penetration = _ccData.Radius - dist + 0.0001f;
                        // push capsule out along plane normal (this will lift the capsule when coming from above)
                        var push = planeNormal * penetration;
                        _ccData.Position += push;

                        // If projection point lies at or slightly above capsule bottom, consider grounded
                        if (projHeight >= capBottomPre - 0.05f && projHeight <= capTopPre - 0.05f && push.Y >= 0f)
                        {
                            _ccData.IsGrounded = true;
                            _ccData.GroundNormal = planeNormal;
                            // cancel downward velocity when landing on ramp
                            if (_ccData.Velocity.Y < 0f)
                            {
                                var v = _ccData.Velocity;
                                v.Y = 0f;
                                _ccData.Velocity = v;
                            }
                        }
                    }
                }
            }
            else if (s is Bump bp)
            {
                // treat bump as hemisphere; perform analytic capsule-vs-sphere test using the sphere center
                float cylHalf = MathF.Max(0f, (_ccData.Height * 0.5f) - _ccData.Radius);
                var segA = _ccData.Position + new Vector3(0f, cylHalf, 0f);
                var segB = _ccData.Position - new Vector3(0f, cylHalf, 0f);
                // fall through to sphere logic by treating hemisphere center as sphere center but only respond if closest point is above center
                var sphereCenter = bp.Center;
                float combined = bp.Radius + _ccData.Radius;
                // closest point on segment to sphere center
                var ab = segB - segA;
                float t = Vector3.Dot(sphereCenter - segA, ab) / MathF.Max(1e-6f, ab.LengthSquared);
                t = MathHelper.Clamp(t, 0f, 1f);
                var closest = segA + ab * t;
                var diff = closest - sphereCenter;
                float d2 = diff.LengthSquared;
                if (d2 < combined * combined)
                {
                    float d = MathF.Sqrt(d2);
                    float pen = combined - d + 0.0001f;
                    Vector3 n = d > 1e-6f ? diff / d : new Vector3(0f, 1f, 0f);
                    // For hemisphere, only push if closest point is above hemisphere center (y >= center.y)
                    if (closest.Y >= sphereCenter.Y - 1e-4f)
                    {
                        _ccData.Position += n * pen;
                        // if pushing from above, consider grounded
                        if (n.Y > 0.5f)
                        {
                            _ccData.IsGrounded = true;
                            _ccData.GroundNormal = n;
                            if (_ccData.Velocity.Y < 0f)
                            {
                                var v = _ccData.Velocity;
                                v.Y = 0f;
                                _ccData.Velocity = v;
                            }
                        }
                    }
                }
            }
            else if (s is Scene.SphereObstacle so)
            {
                // analytic capsule-vs-sphere collision
                float cylHalf = MathF.Max(0f, (_ccData.Height * 0.5f) - _ccData.Radius);
                var segA = _ccData.Position + new Vector3(0f, cylHalf, 0f);
                var segB = _ccData.Position - new Vector3(0f, cylHalf, 0f);
                var sphereCenter = so.Center;
                float combined = so.Radius + _ccData.Radius;
                var ab = segB - segA;
                float t = Vector3.Dot(sphereCenter - segA, ab) / MathF.Max(1e-6f, ab.LengthSquared);
                t = MathHelper.Clamp(t, 0f, 1f);
                var closest = segA + ab * t;
                var diff = closest - sphereCenter;
                float d2 = diff.LengthSquared;
                if (d2 < combined * combined)
                {
                    float d = MathF.Sqrt(d2);
                    float pen = combined - d + 0.0001f;
                    Vector3 n = d > 1e-6f ? diff / d : new Vector3(0f, 1f, 0f);
                    _ccData.Position += n * pen;
                    if (n.Y > 0.5f)
                    {
                        _ccData.IsGrounded = true;
                        _ccData.GroundNormal = n;
                        if (_ccData.Velocity.Y < 0f)
                        {
                            var v = _ccData.Velocity;
                            v.Y = 0f;
                            _ccData.Velocity = v;
                        }
                    }
                }
            }
        }
    }
}

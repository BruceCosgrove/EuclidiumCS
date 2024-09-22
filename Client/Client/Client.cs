using Client.Gui;
using Client.Gui.Panels;
using Client.Model;
using Euclidium.Core;
using Euclidium.Rendering;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Vulkan;
using System.Numerics;

namespace Client;

internal sealed partial class Client
{
    private static Window Window => Engine.Instance.Window;
    //private static Vk VK => Engine.Instance.Window.VK;
    private static IKeyboard Keyboard => Engine.Instance.Window.Keyboard;
    private static IMouse Mouse => Engine.Instance.Window.Mouse;

    private Euclidium.Rendering.Framebuffer? _framebuffer;
    private Shader? _slicingShader;
    private Shader? _projectingShader;
    private readonly StaticVertexBuffer _vertexBuffer = new(); // TODO: make dynamic
    private IndexBuffer? _cellIndexBuffer;
    private IndexBuffer? _edgeIndexBuffer;

    private const int MaxVertexCount = 1024 * 1024; // 4 MiB
    private const int MaxIndexCount = 1024 * 1024; // 4 MiB

    private Model4D? _model;
    private string? _modelPath;
    private readonly LoadableModel _loadable = new();

    private CameraController? _cameraController;
    private PanelController? _panelController;
    private PropertiesPanel? _propertiesPanel;
    private ViewportPanel? _viewportPanel;

    private readonly Shader _shader = new(); // TODO

    protected override void InitializeCallbacks()
    {
        Window.RenderInit += OnRenderInit;
        Window.RenderShutdown += OnRenderShutdown;
        //Window.Update += OnUpdate;
        Window.Render += OnRender;
        //Window.ImGuiRender += OnImGuiRender;
        //Window.KeyChange += OnKeyChange;
    }

    private unsafe void OnRenderInit()
    {
        var context = Engine.Instance.Window.Context;

        //// Framebuffer
        //Euclidium.Rendering.Framebuffer.Create(new()
        //{
        //    Width = (uint)Window.FramebufferSize.X,
        //    Height = (uint)Window.FramebufferSize.Y,
        //    ColorAttachments = [new(FramebufferFormat.Ru8_Gu8_Bu8_Au8)],
        //    DepthAttachment = new(FramebufferFormat.Du24_Su8),
        //}, out _framebuffer!);


        // Shaders
        _shader.Create("./Resources/Shaders/VulkanBootstrap");
        //_slicingShader = Shader.Create("./Resources/Shaders/Slicing4D");
        //_projectingShader = Shader.Create("./Resources/Shaders/Projecting4D");

        // Vertex buffer
        float[] vertexBuffer =
        [
            -0.5f, +0.5f,  0f, 0f, 1f,
            +0.5f, +0.5f,  0f, 1f, 0f,
             0.0f, -0.5f,  1f, 0f, 0f,
        ];

        _vertexBuffer.Create((ulong)vertexBuffer.Length * sizeof(float));
        fixed (void* vertexBufferPtr = vertexBuffer)
            _vertexBuffer.SetData(vertexBufferPtr, _vertexBuffer.Size);

        //// Index buffers
        //_cellIndexBuffer = new(new(DrawElementsType.UnsignedInt, MaxIndexCount, BufferUsageARB.DynamicDraw));
        //_edgeIndexBuffer = new(new(DrawElementsType.UnsignedInt, MaxIndexCount, BufferUsageARB.DynamicDraw));

        //// Camera controller
        //_cameraController = new(float.DegreesToRadians(90.0f), 0.001f, 1000.0f)
        //{
        //    Position = new(0f, 0f, 2f, 0f),
        //    //Rotation4D = new(0f, 0f, float.DegreesToRadians(20f)),
        //};
        //Window.Update += _cameraController.OnUpdate;
        //Window.KeyChange += _cameraController.OnKeyChange;
        //Window.MouseMove += _cameraController.OnMouseMove;
        //Window.MouseButtonChange += _cameraController.OnMouseButtonChange;

        //// Panels
        //_panelController = new();
        //_panelController.AddPanel(_propertiesPanel = new(_cameraController));
        //_panelController.AddPanel(_viewportPanel = new(_cameraController, _framebuffer));
        //_panelController.AddPanel(new DemoPanel());
        //Window.KeyChange += _panelController.OnKeyChange;
        //Window.MouseButtonChange += _panelController.OnMouseButtonChange;
    }

    private void OnRenderShutdown()
    {
        //_framebuffer!.Dispose();
        //_slicingShader!.Dispose();
        //_projectingShader!.Dispose();
        _vertexBuffer!.Dispose();
        //_cellIndexBuffer!.Dispose();
        //_edgeIndexBuffer!.Dispose();

        _shader!.Dispose();
    }

    private void OnUpdate(double deltaTime)
    {
        //if (_loadable.IsLoaded)
        //{
        //    _loadable.Consume(out _model, out _modelPath);

        //    _vertexBuffer!.SetData(_model.Vertices);
        //    _cellIndexBuffer!.SetData(_model.CellIndices);
        //    _edgeIndexBuffer!.SetData(_model.EdgeIndices);
        //}
    }

    private void OnRender(double deltaTime)
    {
        _shader!.Bind();
        _vertexBuffer!.Bind();
        Window.Context.Draw(); // TODO

        //if (!_viewportPanel!.Enabled)
        //    return;

        //_framebuffer!.Bind();

        //GL.ClearColor(0.3f, 0.5f, 1.0f, 1.0f);
        //GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        //if (_model != null)
        //{
        //    GL.Enable(EnableCap.DepthTest);
        //    //GL.Enable(EnableCap.CullFace);
        //    //GL.Enable(EnableCap.Blend);
        //    //GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        //    // Same vertex buffer used for both index buffers.
        //    _vertexBuffer!.Bind();

        //    // Render sliced tetrahedra.
        //    _slicingShader!.Bind();
        //    _slicingShader!.SetUniform("uRotation4D", _cameraController!.Rotation4DMatrix);
        //    // TODO: should this be the position relative to _cameraController.Inner's rotation?
        //    _slicingShader!.SetUniform("uPositionW", _cameraController!.Position.W);
        //    _slicingShader!.SetUniform("uViewProjection", _cameraController!.ViewProjectionMatrix);
        //    _cellIndexBuffer!.Bind();
        //    GL.DrawElements(PrimitiveType.LinesAdjacency, (uint)_model.CellIndices.Count, _cellIndexBuffer!.Type, ReadOnlySpan<uint>.Empty);

        //    // Render projected edges.
        //    _projectingShader!.Bind();
        //    _projectingShader!.SetUniform("uRotation4D", _cameraController!.Rotation4DMatrix);
        //    _projectingShader!.SetUniform("uViewProjection", _cameraController!.ViewProjectionMatrix);
        //    _edgeIndexBuffer!.Bind();
        //    GL.DrawElements(PrimitiveType.Lines, (uint)_model.EdgeIndices.Count, _edgeIndexBuffer!.Type, ReadOnlySpan<uint>.Empty);
        //}

        //_framebuffer!.Unbind();
    }

    private void OnImGuiRender()
    {
        //GL.Viewport(Window.FramebufferSize);

        //GuiDockspace();
        //_panelController!.OnImGuiRender();
    }

    private void GuiDockspace()
    {
        var dockspaceFlags =
            ImGuiWindowFlags.MenuBar |
            ImGuiWindowFlags.NoDocking |
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoNavFocus;

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        ImGui.SetNextWindowViewport(viewport.ID);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("Dockspace", dockspaceFlags);
        ImGui.PopStyleVar(2);

        ImGui.DockSpace(ImGui.GetID("Dockspace"));
        GuiMenuBar();

        ImGui.End();
    }

    private void GuiMenuBar()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 4f));
        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Open...", "Ctrl+O"))
                    OpenModelDialog();
                if (ImGui.MenuItem("Reload", "Ctrl+R", false, _modelPath != null))
                    ReloadModel();
                ImGui.Separator();
                if (ImGui.MenuItem("Exit", "Alt+F4"))
                    Window.Close();
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                if (ImGui.BeginMenu("Panels"))
                {
                    foreach (var panel in _panelController!.Panels)
                        ImGui.MenuItem(panel.Name, "", ref panel.Enabled);
                    ImGui.EndMenu();
                }
                ImGui.EndMenu();
            }

            ImGui.EndMenuBar();
        }
        ImGui.PopStyleVar();
    }

    private void OpenModelDialog()
    {
        ModelLoader.Open(_loadable);
    }

    private void ReloadModel()
    {
        if (_modelPath != null)
            ModelLoader.Load(_modelPath, _loadable);
    }

    private void OnKeyChange(Key key, bool pressed)
    {
        bool ctrlPressed = Keyboard.IsKeyPressed(Key.ControlLeft) || Keyboard.IsKeyPressed(Key.ControlRight);
        bool shiftPressed = Keyboard.IsKeyPressed(Key.ShiftLeft) || Keyboard.IsKeyPressed(Key.ShiftLeft);
        bool altPressed = Keyboard.IsKeyPressed(Key.AltLeft) || Keyboard.IsKeyPressed(Key.AltRight);
        bool superPressed = Keyboard.IsKeyPressed(Key.SuperLeft) || Keyboard.IsKeyPressed(Key.SuperRight);

        if (pressed && !_viewportPanel!.IsMouseCaptured)
        {
            if (ctrlPressed && !shiftPressed && !altPressed && !superPressed)
            {
                switch (key)
                {
                    case Key.O: OpenModelDialog(); break;
                    case Key.R: ReloadModel(); break;
                }
            }
        }
    }
}

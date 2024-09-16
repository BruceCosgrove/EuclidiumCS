using ImGuiNET;
using Silk.NET.GLFW;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using System.Numerics;

namespace Euclidium.Core;

public sealed class Window
{
    private readonly IWindow _window;
    private IInputContext? _inputContext;
    private GL? _gl;
    private ImGuiController? _imguiController;
    // TODO: The ImGuiController does not do anything about cursors.
    private Dictionary<ImGuiMouseCursor, nint>? _cursors;
    private CursorMode _cursorMode = CursorMode.Normal;

    /* Client Events */

    // Called after the renderer is initialized.
    // Resize and FramebufferResize are called immediately after.
    public event Action? RenderInit;

    // Called right before the renderer is shutdown.
    public event Action? RenderShutdown;

    // Called when you should update non-rendering game state.
    public event Action<double>? Update;

    // Called when you should render.
    public event Action<double>? Render;

    // Called when you should render ImGui things.
    public event Action? ImGuiRender;

    /* Events */

    // Called when the window is resized.
    public event Action<Vector2D<int>>? WindowResize;

    // Called when the window's framebuffer is resized.
    public event Action<Vector2D<int>>? WindowFramebufferResize;

    // Called when a key is pressed (true) or released (false).
    public event Action<Key, bool>? KeyChange;

    // Called when a mouse button is pressed (true) or released (false).
    public event Action<Silk.NET.Input.MouseButton, bool>? MouseButtonChange;

    // Called when a mouse moves to a position.
    public event Action<Vector2D<int>>? MouseMove;

    /* Values */
    public GL GL => _gl!;
    public IKeyboard Keyboard => _inputContext!.Keyboards[0];
    public IMouse Mouse => _inputContext!.Mice[0];

    public Vector2D<int> Size => _window.Size;

    public Vector2D<int> FramebufferSize => _window.FramebufferSize;

    public CursorMode CursorMode
    {
        get => _cursorMode;
        set
        {
            if (_cursorMode != value)
            {
                _cursorMode = value;
                foreach (var mouse in _inputContext!.Mice)
                    mouse.Cursor.CursorMode = _cursorMode;
            }
        }
    }


    internal Window()
    {
        // Require GLFW.
        var platform = Silk.NET.Windowing.Window.Platforms.FirstOrDefault(x => x.GetType().FullName == "Silk.NET.Windowing.Sdl.SdlPlatform");
        if (platform != null)
            Silk.NET.Windowing.Window.Remove(platform);
        platform = Silk.NET.Windowing.Window.Platforms.FirstOrDefault(x => x.GetType().FullName == "Silk.NET.Windowing.Glfw.GlfwPlatform");
        if (platform == null)
        {
            Console.Error.WriteLine("GLFW is required.");
            Environment.Exit(1);
        }

        // Create the window and add event handlers.
        _window = Silk.NET.Windowing.Window.Create(WindowOptions.Default with
        {
            //WindowState = WindowState.Fullscreen,
            WindowState = WindowState.Maximized,
            WindowBorder = WindowBorder.Hidden,
        }); // TODO: WindowOptions.DefaultVulkan
        _window.Load += OnLoad;
        _window.Closing += OnClosing;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Resize += OnResize;
        _window.FramebufferResize += OnFramebufferResize;
    }

    internal void Run()
    {
        _window.Run();
        _window.Dispose();
    }

    public void Close() => _window.Close();

    private void OnLoad()
    {
        _window.Center();

        _inputContext = _window.CreateInput();

        foreach (var keyboard in _inputContext.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
        }

        foreach (var mouse in _inputContext.Mice)
        {
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.MouseMove += OnMouseMove;
        }

        //_inputContext.Keyboards[0].

        // TODO: Vulkan: https://github.com/dfkeenan/SilkVulkanTutorial/
        //_vk = Vk.GetApi();
        // NOTE: Currently, ImGuiController needs an OpenGL context.
        // If it ever can use Vulkan instead, do that.
        // While that isn't an option, use this:
        // https://www.reddit.com/r/vulkan/comments/vnttmu/texture_issues_with_opengl_vulkan_interop_using/
        // UPDATE: The ImGuiController can just be rewritten
        // using imgui's vulkan backend as an example.

        _gl = _window.CreateOpenGL();
        _imguiController = new ImGuiController(_gl, _window, _inputContext, () =>
        {
            var io = ImGui.GetIO();
            var style = ImGui.GetStyle();

            io.Fonts.AddFontFromFileTTF("./Resources/Fonts/OpenSans/OpenSans-Regular.ttf", 18f);

            io.ConfigFlags |=
                ImGuiConfigFlags.DockingEnable |
                ImGuiConfigFlags.ViewportsEnable;
            io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
            io.ConfigWindowsMoveFromTitleBarOnly = true;
            io.ConfigWindowsResizeFromEdges = true;

            style.ItemSpacing = new(4f);
            style.FramePadding = new(4f);
            style.WindowPadding = new(4f);
            style.WindowRounding = 0f;
            style.WindowBorderSize = 0f;
            style.WindowMenuButtonPosition = ImGuiDir.None;
        });

        // TODO: The ImGuiController does not do anything about cursors.
        unsafe
        {
            var glfw = Glfw.GetApi();
            _cursors = new()
            {
                [ImGuiMouseCursor.Arrow]      = (nint)glfw.CreateStandardCursor(CursorShape.Arrow),
                [ImGuiMouseCursor.TextInput]  = (nint)glfw.CreateStandardCursor(CursorShape.IBeam),
                [ImGuiMouseCursor.ResizeAll]  = (nint)glfw.CreateStandardCursor(CursorShape.AllResize),
                [ImGuiMouseCursor.ResizeNS]   = (nint)glfw.CreateStandardCursor(CursorShape.VResize),
                [ImGuiMouseCursor.ResizeEW]   = (nint)glfw.CreateStandardCursor(CursorShape.HResize),
                [ImGuiMouseCursor.ResizeNESW] = (nint)glfw.CreateStandardCursor(CursorShape.NeswResize),
                [ImGuiMouseCursor.ResizeNWSE] = (nint)glfw.CreateStandardCursor(CursorShape.NwseResize),
                [ImGuiMouseCursor.Hand]       = (nint)glfw.CreateStandardCursor(CursorShape.Hand),
                [ImGuiMouseCursor.NotAllowed] = (nint)glfw.CreateStandardCursor(CursorShape.NotAllowed),
            };
        }

        RenderInit?.Invoke();
        OnResize(_window.Size);
        OnFramebufferResize(_window.FramebufferSize);
    }

    private void OnClosing()
    {
        // TODO: The ImGuiController does not do anything about cursors.
        unsafe
        {
            var glfw = Glfw.GetApi();
            foreach (var (_, cursor) in _cursors!)
                glfw.DestroyCursor((Cursor*)cursor);
        }

        ImGui.SaveIniSettingsToDisk("./imgui.ini");
    }

    private void OnUpdate(double deltaTime)
    {
        Update?.Invoke(deltaTime);
    }

    private void OnRender(double deltaTime)
    {
        Render?.Invoke(deltaTime);

        _imguiController!.Update((float)deltaTime);
        ImGuiRender?.Invoke();

        // TODO: The ImGuiController does not do anything about cursors.
        unsafe
        {
            var glfw = Glfw.GetApi();
            var window = (WindowHandle*)_window.Native!.Glfw!;
            var cursor = (Cursor*)_cursors![ImGui.GetMouseCursor()];
            glfw.SetCursor(window, cursor);
        }

        _imguiController!.Render();

        // This is really dumb; apparently, Silk.NET.Windowing.Window
        // calls its Render event one time AFTER it's flagged to close.
        if (_window.IsClosing)
        {
            RenderShutdown?.Invoke();
            _imguiController!.Dispose();
            _gl!.Dispose();
            _inputContext!.Dispose();
        }
    }

    private void OnResize(Vector2D<int> size) =>
        WindowResize?.Invoke(size);

    private void OnFramebufferResize(Vector2D<int> size) =>
        WindowFramebufferResize?.Invoke(size);

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode) =>
        KeyChange?.Invoke(key, true);

    private void OnKeyUp(IKeyboard keyboard, Key key, int scancode) =>
        KeyChange?.Invoke(key, false);

    private void OnMouseDown(IMouse mouse, Silk.NET.Input.MouseButton button) =>
        MouseButtonChange?.Invoke(button, true);

    private void OnMouseUp(IMouse mouse, Silk.NET.Input.MouseButton button) =>
        MouseButtonChange?.Invoke(button, false);

    private void OnMouseMove(IMouse mouse, Vector2 position) =>
        MouseMove?.Invoke(position.ToGeneric().As<int>());
}

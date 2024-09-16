using Euclidium.Core;
using Euclidium.Rendering;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using System.Numerics;

namespace Client.Gui.Panels;

public class ViewportPanel(CameraController cameraController, Framebuffer framebuffer) : Panel("Viewport")
{
    private readonly CameraController _cameraController = cameraController;
    private readonly Framebuffer _framebuffer = framebuffer;
    private bool _framebufferHovered = false;
    private bool _mouseCaptured = false;

    public bool IsMouseCaptured => _mouseCaptured;

    public override void OnImGuiRender()
    {
        if (Enabled)
        {
            var flags =
                ImGuiWindowFlags.NoFocusOnAppearing;

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0f);
            ImGui.Begin("Viewport", ref Enabled, flags);
            ImGui.PopStyleVar(2);
            Update();

            ImGui.Image((nint)_framebuffer!.GetColorAttachment(), ImGui.GetContentRegionAvail(), new(0f, 1f), new(1f, 0f));
            _framebufferHovered = ImGui.IsItemHovered();

            ImGui.End();
        }
    }

    public override void OnViewportResize(Vector2D<int> size)
    {
        _cameraController!.OnViewportResize(size);
        _framebuffer!.OnViewportResize(size);
    }

    public override void OnKeyChange(Key key, bool pressed)
    {
        switch (key)
        {
            case Key.Escape:
                if (pressed)
                    OnMouseCaptured(false);
                break;
        }
    }

    public override void OnMouseButtonChange(MouseButton button, bool pressed)
    {
        switch (button)
        {
            case MouseButton.Left:
                if (pressed && _framebufferHovered)
                    OnMouseCaptured(true);
                break;
        }
    }

    private void OnMouseCaptured(bool captured)
    {
        _mouseCaptured = captured;
        _cameraController!.Enabled = captured;
        if (captured)
        {
            Engine.Instance.Window.CursorMode = CursorMode.Disabled;
            ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.NoMouse;
        }
        else
        {
            Engine.Instance.Window.CursorMode = CursorMode.Normal;
            ImGui.GetIO().ConfigFlags &= ~ImGuiConfigFlags.NoMouse;
        }
    }
}

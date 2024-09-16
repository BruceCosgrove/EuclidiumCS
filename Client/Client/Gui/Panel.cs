using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace Client.Gui;

public abstract class Panel(string name, bool enabled = true)
{
    public readonly string Name = name;
    public bool Enabled = enabled;
    private bool _focused = false;
    private bool _hovered = false;
    private Vector2D<int> _size = Vector2D<int>.One;
    private Vector2D<int> _position = Vector2D<int>.Zero;

    public bool Focused
    {
        get => _focused;
        set
        {
            if (_focused != value)
            {
                _focused = value;
                if (_focused)
                    ImGui.SetWindowFocus(Name);
                else
                    ImGui.SetWindowFocus();
            }
        }
    }
    public bool Hovered => _hovered;
    public Vector2D<int> Size => _size;
    public Vector2D<int> Position => _position;

    public abstract void OnImGuiRender();

    public virtual void OnViewportResize(Vector2D<int> size) {}

    public virtual void OnKeyChange(Key key, bool pressed) {}
    public virtual void OnMouseButtonChange(MouseButton button, bool pressed) {}

    protected void Update()
    {
        _focused = ImGui.IsWindowFocused();
        _hovered = ImGui.IsWindowHovered();
        _position = (ImGui.GetWindowPos() + ImGui.GetCursorPos()).ToGeneric().As<int>();

        var size = ImGui.GetContentRegionAvail().ToGeneric().As<int>();
        if (_size != size)
        {
            OnViewportResize(size);
            _size = size;
        }
    }
}

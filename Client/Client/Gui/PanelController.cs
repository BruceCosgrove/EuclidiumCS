using Silk.NET.Input;
using System.Runtime.InteropServices;

namespace Client.Gui;

public class PanelController
{
    private readonly List<Panel> _panels = [];

    public Span<Panel> Panels => CollectionsMarshal.AsSpan(_panels);

    public void AddPanel(Panel panel) =>
        _panels.Add(panel);

    public void RemovePanel(Panel panel) =>
        _panels.Remove(panel);

    public void OnImGuiRender() =>
        _panels.ForEach(panel => panel.OnImGuiRender());

    public void OnKeyChange(Key key, bool pressed) =>
        _panels.ForEach(panel => panel.OnKeyChange(key, pressed));

    public void OnMouseButtonChange(MouseButton button, bool pressed) =>
        _panels.ForEach(panel => panel.OnMouseButtonChange(button, pressed));
}

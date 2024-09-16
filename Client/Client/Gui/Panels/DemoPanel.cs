using ImGuiNET;

namespace Client.Gui.Panels;

public class DemoPanel() : Panel("Demo", false)
{
    public override void OnImGuiRender()
    {
        if (Enabled)
            ImGui.ShowDemoWindow(ref Enabled);
    }
}

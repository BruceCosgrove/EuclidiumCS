using ImGuiNET;

namespace Client.Gui;

public static class ImGuiCustom
{
    public static void SetNextWindowClass(ImGuiWindowClass windowClass)
    {
        unsafe // This is so dumb.
        {
            ImGui.SetNextWindowClass(new ImGuiWindowClassPtr(&windowClass));
        }
    }
}

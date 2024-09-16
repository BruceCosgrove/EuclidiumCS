using ImGuiNET;
using Silk.NET.Maths;

namespace Client.Gui.Panels;

public class PropertiesPanel(CameraController cameraController) : Panel("Properties")
{
    private readonly CameraController _cameraController = cameraController;

    public override void OnImGuiRender()
    {
        if (Enabled)
        {
            var flags =
                ImGuiWindowFlags.NoFocusOnAppearing;

            ImGui.Begin("Camera Controller", ref Enabled, flags);
            Update();

            var position = _cameraController!.Position.ToSystem();
            if (ImGui.DragFloat4("Position", ref position, 0.1f))
                _cameraController!.Position = position.ToGeneric();

            var rotation = _cameraController!.Rotation.ToSystem();
            if (ImGui.DragFloat3("Rotation", ref rotation, 0.1f))
                _cameraController!.Rotation = rotation.ToGeneric();

            var rotation4D = _cameraController!.Rotation4D.ToSystem();
            if (ImGui.DragFloat3("Rotation4D", ref rotation4D, 0.1f))
                _cameraController!.Rotation4D = rotation4D.ToGeneric();

            ImGui.End();
        }
    }
}

using System.Numerics;
using ImGuiNET;

namespace TabTab
{
    public class PluginUI
    {
        private readonly Plugin instance;
        private bool visible = false;

        public PluginUI(Plugin plugin)
        {
            this.instance = plugin;
        }

        public bool IsVisible
        {
            get => this.visible;
            set => this.visible = value;
        }

        public void Draw()
        {
            if (!IsVisible)
                return;
            
            ImGui.SetNextWindowSize(new Vector2(375, 330), ImGuiCond.Always);
            if (ImGui.Begin("TabTab Settings", ref this.visible,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize))
            {
                ImGui.Checkbox("Enable", ref instance.Enable);
                if (instance.Enable)
                {
                    ImGui.Text(
                        "Your in-game tab targeting settings will be ignored while\nTabTab is enabled, and settings here will be used instead.");
                }

                ImGui.Separator();
                ImGui.Text("Targeting Mode");
                ImGui.RadioButton("Target HP (Highest to Lowest)", ref instance.Mode, 1);
                ImGui.RadioButton("Target HP (Lowest to Highest)", ref instance.Mode, 2);
            }
        }
    }
}

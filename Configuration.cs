using Dalamud.Configuration;
using Dalamud.Plugin;
using Newtonsoft.Json;

namespace TabTab
{
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;
        public bool Enabled { get; set; } = true;
        public int Mode { get; set; } = 1;

        // Add any other properties or methods here.
        [JsonIgnore] private DalamudPluginInterface pluginInterface;

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.pluginInterface.SavePluginConfig(this);
        }
    }
}

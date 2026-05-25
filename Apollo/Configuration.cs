using Dalamud.Configuration;

namespace Apollo;

public sealed class Configuration : IPluginConfiguration {
    public int Version { get; set; } = 1;
    public bool TrimTrailingSilenceEnabled { get; set; } = true;
    public bool SilenceAutoStopEnabled { get; set; } = true;
    public bool SendTranscriptToChat { get; set; } = false;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}

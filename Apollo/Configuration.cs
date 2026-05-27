using Dalamud.Configuration;

namespace Apollo;

public sealed class Configuration : IPluginConfiguration {
    public int Version { get; set; } = 1;
    public bool TrimTrailingSilenceEnabled { get; set; } = true;
    public bool SilenceAutoStopEnabled { get; set; } = true;
    public bool SendTranscriptToChat { get; set; } = false;

    public bool EnableTts { get; set; } = false;
    public bool TtsSpeakSay { get; set; } = true;
    public bool TtsSpeakYell { get; set; } = false;
    public bool TtsSpeakShout { get; set; } = false;
    public bool TtsSpeakParty { get; set; } = true;
    public bool TtsSpeakAlliance { get; set; } = false;
    public bool TtsSpeakFreeCompany { get; set; } = true;
    public bool TtsSpeakTellIncoming { get; set; } = true;
    public bool TtsSpeakLinkShell { get; set; } = false;
    public bool TtsSpeakCrossLinkShell { get; set; } = false;
    public bool TtsSpeakNoviceNetwork { get; set; } = false;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}

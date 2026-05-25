using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Apollo.Windows;

internal sealed class ConfigWindow : Window {
    private readonly Configuration _config;

    public ConfigWindow(Configuration config) : base("Apollo Settings") {
        _config = config;
        Size = new Vector2(360, 200);
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags = ImGuiWindowFlags.AlwaysAutoResize;
    }

    public override void Draw() {
        var sendToChat = _config.SendTranscriptToChat;
        if (ImGui.Checkbox("Send transcript to in-game chat", ref sendToChat)) {
            _config.SendTranscriptToChat = sendToChat;
            _config.Save();
        }
        ImGui.TextDisabled("When off, the transcript is only printed locally via\nChatGui.Print (debug). When on, it is typed into the\ngame's chat box using the active channel.");

        ImGui.Spacing();

        var autoStop = _config.SilenceAutoStopEnabled;
        if (ImGui.Checkbox("Auto-stop recording on silence", ref autoStop)) {
            _config.SilenceAutoStopEnabled = autoStop;
            _config.Save();
        }
        ImGui.TextDisabled("When off, recording runs until you stop it manually\n(or hits the 60 s hard cap).");

        ImGui.Spacing();

        var trim = _config.TrimTrailingSilenceEnabled;
        if (ImGui.Checkbox("Trim trailing silence before inference", ref trim)) {
            _config.TrimTrailingSilenceEnabled = trim;
            _config.Save();
        }
        ImGui.TextDisabled("Cuts the audio shortly after the last detected speech.\nDisable if transcripts are getting truncated.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextDisabled($"Active model: {ModelCatalog.Default.DisplayName}");
    }
}

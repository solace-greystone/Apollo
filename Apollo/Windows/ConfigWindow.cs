using System.Numerics;
using Apollo.Tts;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Apollo.Windows;

internal sealed class ConfigWindow : Window {
    private static readonly Vector4 ErrorColor = new(1f, 0.3f, 0.3f, 1f);

    private readonly Configuration _config;
    private readonly TextToSpeechManager _tts;

    public ConfigWindow(Configuration config, TextToSpeechManager tts) : base("Apollo Settings") {
        _config = config;
        _tts = tts;
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

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Text("Text-to-Speech");

        var ttsAvailable = _tts.IsAvailable;
        if (!ttsAvailable) ImGui.BeginDisabled();
        var enableTts = _config.EnableTts;
        if (ImGui.Checkbox("Enable text-to-speech", ref enableTts)) {
            _config.EnableTts = enableTts;
            _config.Save();
        }
        if (!ttsAvailable) ImGui.EndDisabled();

        if (!ttsAvailable) {
            ImGui.TextColored(ErrorColor, "No TTS provider available.");
        } else {
            ImGui.TextDisabled("Reads incoming chat aloud via the system SAPI voice.");
        }

        var channelsEnabled = ttsAvailable && enableTts;
        if (!channelsEnabled) ImGui.BeginDisabled();
        ImGui.Spacing();
        DrawChannelCheckbox("Say", () => _config.TtsSpeakSay, v => _config.TtsSpeakSay = v);
        DrawChannelCheckbox("Yell", () => _config.TtsSpeakYell, v => _config.TtsSpeakYell = v);
        DrawChannelCheckbox("Shout", () => _config.TtsSpeakShout, v => _config.TtsSpeakShout = v);
        DrawChannelCheckbox("Party", () => _config.TtsSpeakParty, v => _config.TtsSpeakParty = v);
        DrawChannelCheckbox("Alliance", () => _config.TtsSpeakAlliance, v => _config.TtsSpeakAlliance = v);
        DrawChannelCheckbox("Free Company", () => _config.TtsSpeakFreeCompany, v => _config.TtsSpeakFreeCompany = v);
        DrawChannelCheckbox("Tell (incoming)", () => _config.TtsSpeakTellIncoming, v => _config.TtsSpeakTellIncoming = v);
        DrawChannelCheckbox("Linkshell (1-8)", () => _config.TtsSpeakLinkShell, v => _config.TtsSpeakLinkShell = v);
        DrawChannelCheckbox("Cross-world Linkshell (1-8)", () => _config.TtsSpeakCrossLinkShell, v => _config.TtsSpeakCrossLinkShell = v);
        DrawChannelCheckbox("Novice Network", () => _config.TtsSpeakNoviceNetwork, v => _config.TtsSpeakNoviceNetwork = v);
        if (!channelsEnabled) ImGui.EndDisabled();
    }

    private void DrawChannelCheckbox(string label, System.Func<bool> get, System.Action<bool> set) {
        var v = get();
        if (ImGui.Checkbox(label, ref v)) {
            set(v);
            _config.Save();
        }
    }
}

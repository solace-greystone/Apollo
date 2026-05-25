using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Apollo.Windows;

internal sealed class MainWindow : Window {
    private readonly SpeechToTextManager _stt;

    public MainWindow(SpeechToTextManager stt) : base("Apollo") {
        _stt = stt;
        Size = new Vector2(320, 180);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw() {
        string status;
        if (!_stt.IsModelReady) {
            status = "Model downloading...";
        } else if (_stt.IsRecording) {
            status = "Recording...";
        } else {
            status = "Idle";
        }
        ImGui.Text($"Status: {status}");

        ImGui.Spacing();

        var canToggle = _stt.IsModelReady;
        if (!canToggle) ImGui.BeginDisabled();
        var label = _stt.IsRecording ? "Stop recording" : "Start recording";
        if (ImGui.Button(label)) {
            if (_stt.IsRecording) {
                _stt.StopRecording();
            } else {
                _stt.RecordAudio();
            }
        }
        if (!canToggle) ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.TextDisabled("Slash command: /apollo record");
    }
}

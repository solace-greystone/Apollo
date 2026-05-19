using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Apollo;

public sealed class Plugin : IDalamudPlugin {
    public string Name => "Apollo";
    private const string CommandName = "/apollo";

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;

    private readonly SpeechToTextManager _stt;
    private readonly Chat.Chat _chat;
    private readonly Queue<string> _messageQueue = new();
    private readonly Stopwatch _sendThrottle = new();

    public Plugin() {
        var basePath = Path.GetDirectoryName(PluginInterface.AssemblyLocation.FullName)!;
        _stt = new SpeechToTextManager(basePath);
        _stt.RecordingFinished += OnRecordingFinished;

        _chat = new Chat.Chat(SigScanner);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) {
            HelpMessage = "Whisper-based dictation. Subcommand: record",
        });

        Framework.Update += OnFrameworkUpdate;
        _sendThrottle.Start();

        ChatGui.Print("Apollo: enabled. Type /apollo for usage.");
    }

    private void OnCommand(string command, string args) {
        var sub = (args ?? string.Empty).Trim().ToLowerInvariant();
        switch (sub) {
            case "record":
                if (!_stt.IsModelReady) { ChatGui.Print("Apollo: Whisper model is still downloading; please wait."); return; }
                if (_stt.IsRecording) { ChatGui.Print("Apollo: already recording."); return; }
                _stt.RecordAudio();
                ChatGui.Print("Apollo: recording... (stay silent to stop)");
                break;
            default:
                ChatGui.Print("Apollo usage: /apollo record");
                break;
        }
    }

    private void OnRecordingFinished(object? sender, string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            ChatGui.Print("Apollo: (no transcript)");
            return;
        }

        ChatGui.Print($"Apollo: {text}");
        
        //lock (_messageQueue) {
        //    _messageQueue.Enqueue(text);
        //}
    }

    private void OnFrameworkUpdate(IFramework framework) {
        if (_sendThrottle.ElapsedMilliseconds < 1000) return;
        string? next = null;
        lock (_messageQueue) {
            if (_messageQueue.Count > 0) next = _messageQueue.Dequeue();
        }
        if (next == null) return;
        try {
            _chat.SendMessage(next);
        } catch (Exception ex) {
            Log.Error(ex, "Apollo: failed to send chat message");
        }
        _sendThrottle.Restart();
    }

    public void Dispose() {
        Framework.Update -= OnFrameworkUpdate;
        CommandManager.RemoveHandler(CommandName);
        _stt.RecordingFinished -= OnRecordingFinished;
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Apollo.Tts;
using Apollo.Windows;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
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
    private readonly TextToSpeechManager _tts;
    private readonly Queue<string> _messageQueue = new();
    private readonly Stopwatch _sendThrottle = new();
    private readonly WindowSystem _windowSystem = new("Apollo");
    private readonly MainWindow _mainWindow;
    private readonly ConfigWindow _configWindow;
    private readonly Configuration _config;

    public Plugin() {
        var basePath = Path.GetDirectoryName(PluginInterface.AssemblyLocation.FullName)!;
        _config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        _stt = new SpeechToTextManager(basePath, _config);
        _stt.RecordingFinished += OnRecordingFinished;

        _chat = new Chat.Chat(SigScanner);
        _tts = new TextToSpeechManager(_config);
        ChatGui.ChatMessage += OnChatMessage;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) {
            HelpMessage = "Whisper-based dictation. Subcommand: record",
        });

        _mainWindow = new MainWindow(_stt);
        _configWindow = new ConfigWindow(_config, _tts);
        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_configWindow);

        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenMainUi += OnOpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;

        Framework.Update += OnFrameworkUpdate;
        _sendThrottle.Start();

        ChatGui.Print("Apollo: enabled. Type /apollo for usage.");
    }

    private void OnDraw() => _windowSystem.Draw();
    private void OnOpenMainUi() => _mainWindow.Toggle();
    private void OnOpenConfigUi() => _configWindow.Toggle();

    private void OnCommand(string command, string args) {
        var sub = (args ?? string.Empty).Trim().ToLowerInvariant();
        switch (sub) {
            case "record":
                if (_stt.IsRecording) {
                    ChatGui.Print("Apollo: stopping recording.");
                    _stt.StopRecording();
                    return;
                }
                if (!_stt.IsModelReady) { ChatGui.Print("Apollo: Whisper model is still downloading; please wait."); return; }
                _stt.RecordAudio();
                ChatGui.Print("Apollo: recording... run /apollo record again to stop.");
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

        if (_config.SendTranscriptToChat) {
            lock (_messageQueue) {
                _messageQueue.Enqueue(text);
            }
        } else {
            ChatGui.Print($"Apollo: {text}");
        }
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

    private void OnChatMessage(IHandleableChatMessage message) {
        if (!_config.EnableTts) return;
        if (!IsChannelEnabled(message.LogKind)) return;

        var senderText = (message.Sender?.TextValue ?? string.Empty).Trim();
        var bodyText = (message.Message?.TextValue ?? string.Empty).Trim();
        if (bodyText.Length == 0) return;

        var spoken = senderText.Length > 0
            ? $"{senderText} says: {bodyText}"
            : bodyText;
        _tts.Enqueue(spoken);
    }

    private bool IsChannelEnabled(XivChatType type) => type switch {
        XivChatType.Say => _config.TtsSpeakSay,
        XivChatType.Yell => _config.TtsSpeakYell,
        XivChatType.Shout => _config.TtsSpeakShout,
        XivChatType.Party or XivChatType.CrossParty => _config.TtsSpeakParty,
        XivChatType.Alliance => _config.TtsSpeakAlliance,
        XivChatType.FreeCompany => _config.TtsSpeakFreeCompany,
        XivChatType.TellIncoming => _config.TtsSpeakTellIncoming,
        XivChatType.Ls1 or XivChatType.Ls2 or XivChatType.Ls3 or XivChatType.Ls4
            or XivChatType.Ls5 or XivChatType.Ls6 or XivChatType.Ls7 or XivChatType.Ls8
            => _config.TtsSpeakLinkShell,
        XivChatType.CrossLinkShell1 or XivChatType.CrossLinkShell2 or XivChatType.CrossLinkShell3
            or XivChatType.CrossLinkShell4 or XivChatType.CrossLinkShell5 or XivChatType.CrossLinkShell6
            or XivChatType.CrossLinkShell7 or XivChatType.CrossLinkShell8
            => _config.TtsSpeakCrossLinkShell,
        XivChatType.NoviceNetwork => _config.TtsSpeakNoviceNetwork,
        _ => false,
    };

    public void Dispose() {
        ChatGui.ChatMessage -= OnChatMessage;
        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenMainUi -= OnOpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;
        _windowSystem.RemoveAllWindows();
        Framework.Update -= OnFrameworkUpdate;
        CommandManager.RemoveHandler(CommandName);
        _stt.RecordingFinished -= OnRecordingFinished;
        _stt.Dispose();
        _tts.Dispose();
    }
}

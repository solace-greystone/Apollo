using System;
using System.Speech.Synthesis;
using System.Threading;

namespace Apollo.Tts;

internal sealed class SapiTtsProvider : ITtsProvider {
    private readonly SpeechSynthesizer _synth;
    private readonly object _gate = new();
    private bool _disposed;

    public SapiTtsProvider() {
        Plugin.ChatGui.Print("Apollo TTS: constructing SAPI synthesizer...");
        _synth = new SpeechSynthesizer();
        Plugin.ChatGui.Print("Apollo TTS: synthesizer constructed.");
        try {
            _synth.SetOutputToDefaultAudioDevice();
            Plugin.ChatGui.Print("Apollo TTS: SetOutputToDefaultAudioDevice OK.");
        } catch (System.Exception ex) {
            Plugin.ChatGui.Print($"Apollo TTS: SetOutputToDefaultAudioDevice FAILED: {ex.GetType().Name}: {ex.Message}");
        }

        try {
            var voices = _synth.GetInstalledVoices();
            Plugin.ChatGui.Print($"Apollo TTS: GetInstalledVoices returned {voices.Count} voice(s).");
            foreach (var v in voices) {
                var info = v.VoiceInfo;
                Plugin.ChatGui.Print($"  - \"{info?.Name}\" culture={info?.Culture?.Name} enabled={v.Enabled}");
            }
            try {
                var current = _synth.Voice;
                Plugin.ChatGui.Print($"Apollo TTS: active voice = \"{current?.Name}\"");
            } catch (System.Exception ex) {
                Plugin.ChatGui.Print($"Apollo TTS: reading active voice FAILED: {ex.GetType().Name}: {ex.Message}");
            }
        } catch (System.Exception ex) {
            Plugin.ChatGui.Print($"Apollo TTS: GetInstalledVoices FAILED: {ex.GetType().Name}: {ex.Message}");
        }

        Plugin.ChatGui.Print($"Apollo TTS: IsAvailable = {IsAvailable}");
    }

    public bool IsAvailable {
        get {
            if (_disposed) return false;
            try {
                foreach (var voice in _synth.GetInstalledVoices()) {
                    if (!voice.Enabled) continue;
                    string? name = null;
                    try { name = voice.VoiceInfo?.Name; } catch { /* Wine stub NREs here */ }
                    if (!string.IsNullOrWhiteSpace(name)) return true;
                }
            } catch {
                return false;
            }
            return false;
        }
    }

    public void Speak(string text, CancellationToken ct) {
        if (_disposed || string.IsNullOrWhiteSpace(text)) return;

        Prompt prompt;
        using var done = new ManualResetEventSlim(false);

        void OnCompleted(object? _, SpeakCompletedEventArgs __) => done.Set();
        _synth.SpeakCompleted += OnCompleted;

        try {
            lock (_gate) {
                if (_disposed) return;
                prompt = _synth.SpeakAsync(text);
            }

            using var reg = ct.Register(() => {
                try { _synth.SpeakAsyncCancelAll(); } catch { /* disposed */ }
            });

            done.Wait(ct);
        } catch (OperationCanceledException) {
            // expected on shutdown
        } finally {
            _synth.SpeakCompleted -= OnCompleted;
        }
    }

    public void Dispose() {
        lock (_gate) {
            if (_disposed) return;
            _disposed = true;
        }
        try { _synth.SpeakAsyncCancelAll(); } catch { /* ignore */ }
        _synth.Dispose();
    }
}

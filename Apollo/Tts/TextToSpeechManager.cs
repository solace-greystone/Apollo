using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Apollo.Tts;

internal sealed class TextToSpeechManager : IDisposable {
    private readonly Configuration _config;
    private readonly ITtsProvider? _provider;
    private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>());
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumer;

    public TextToSpeechManager(Configuration config) {
        _config = config;
        Plugin.ChatGui.Print("Apollo TTS: initializing TextToSpeechManager...");
        try {
            _provider = new SapiTtsProvider();
        } catch (Exception ex) {
            Plugin.Log.Warning(ex, "Apollo: SAPI initialization failed; TTS will be disabled.");
            Plugin.ChatGui.Print($"Apollo TTS: SAPI init FAILED: {ex.GetType().Name}: {ex.Message}");
            _provider = null;
        }
        Plugin.ChatGui.Print($"Apollo TTS: provider null? {_provider == null}; manager.IsAvailable = {IsAvailable}");

        _consumer = Task.Run(ConsumeLoop);
    }

    public bool IsAvailable => _provider?.IsAvailable ?? false;

    public void Enqueue(string text) {
        if (!IsAvailable || !_config.EnableTts) return;
        if (string.IsNullOrWhiteSpace(text)) return;
        if (_queue.IsAddingCompleted) return;
        try { _queue.Add(text); } catch (InvalidOperationException) { /* completed */ }
    }

    private void ConsumeLoop() {
        var ct = _cts.Token;
        try {
            foreach (var text in _queue.GetConsumingEnumerable(ct)) {
                if (_provider == null) continue;
                try {
                    _provider.Speak(text, ct);
                } catch (OperationCanceledException) {
                    break;
                } catch (Exception ex) {
                    Plugin.Log.Error(ex, "Apollo: TTS playback failed.");
                }
            }
        } catch (OperationCanceledException) {
            // expected on shutdown
        }
    }

    public void Dispose() {
        try { _cts.Cancel(); } catch { /* ignore */ }
        _queue.CompleteAdding();
        try { _consumer.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _provider?.Dispose();
        _queue.Dispose();
        _cts.Dispose();
    }
}

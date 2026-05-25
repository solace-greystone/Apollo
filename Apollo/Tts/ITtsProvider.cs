using System;
using System.Threading;

namespace Apollo.Tts;

internal interface ITtsProvider : IDisposable {
    bool IsAvailable { get; }
    void Speak(string text, CancellationToken ct);
}

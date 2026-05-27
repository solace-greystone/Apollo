# Apollo

Hands-free speech-to-text dictation for FFXIV via [Dalamud](https://dalamud.dev), powered by a locally-run [Whisper](https://github.com/ggerganov/whisper.cpp) model. Speak into your mic; Apollo types it into your chat box.

The STT engine is extracted from [Sebane1/RoleplayingVoiceDalamud](https://github.com/Sebane1/RoleplayingVoiceDalamud) (Artemis Roleplaying Kit) and repackaged as a minimal standalone plugin.

## Commands

| Command          | Behavior                                                      |
|------------------|---------------------------------------------------------------|
| `/apollo record` | Record audio; transcript is sent to chat as a normal message. |
| `/apollo`        | Print usage.                                                  |

Recording stops automatically after ~1 second of silence. There is no manual stop and no in-game UI — slash commands only.

## How it works

1. On first load, the plugin downloads `ggml-base.bin` (~140 MB) into the plugin directory via `Whisper.net.Ggml.WhisperGgmlDownloader`. This is silent and runs in the background.
2. `/apollo record` starts a 16 kHz mono `NAudio.Wave.WaveInEvent` capture. A simple amplitude check on each buffer (threshold 500, 1 s silence window) ends the recording.
3. The recorded `MemoryStream` is fed to `Whisper.net.WhisperFactory.FromPath(...)` with language pinned to English.
4. The first transcript segment is enqueued. A framework-tick loop drains the queue at a 1 s throttle and sends each line to the game's chat via signature-scanned `ProcessChatBox` (vendored from XivCommon).

Key files: `Apollo/Plugin.cs`, `Apollo/SpeechToTextManager.cs`, `Apollo/Chat/Chat.cs`.

## Building

### Linux (CI / cross-build)

```sh
dotnet build -c Release
```

Requires .NET 10 SDK. The `<EnableWindowsTargeting>true</EnableWindowsTargeting>` property in `Apollo/Apollo.csproj` pulls the Windows targeting pack from NuGet so non-Windows hosts can build. Output: `Apollo/bin/Release/Apollo.dll` + `runtimes/win-x64/whisper.dll`.

### Windows (in-place dev)

Same `dotnet build -c Release`. Then point Dalamud's **Settings → Experimental → Dev Plugin Locations** at the folder containing `Apollo.dll` and enable Apollo in `/xlplugins`.

## Installing without building

(Once a release is published, place download/install steps here. Currently dev-only.)

## Known limitations

See [TODO.md](TODO.md) for the full list. Short version: model download is silent and not resumable, the VAD is fragile, only the first transcript segment is used, and there is no manual stop.

## License & credits

- Whisper engine: [whisper.cpp](https://github.com/ggerganov/whisper.cpp) (MIT) via [Whisper.net](https://github.com/sandrohanea/whisper.net).
- Audio capture: [NAudio](https://github.com/naudio/NAudio) (MIT).
- Chat-injection helper: ported from [XivCommon](https://github.com/ascclemens/XIVCommon) (MIT).
- Original STT integration concept: [Sebane1/RoleplayingVoiceDalamud](https://github.com/Sebane1/RoleplayingVoiceDalamud).

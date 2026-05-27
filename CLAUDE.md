# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build

```sh
dotnet build -c Release
```

Requires .NET 10 SDK. Built against `Dalamud.NET.Sdk/15.0.0`; TFM is `net10.0-windows7.0`. Builds on Linux thanks to `<EnableWindowsTargeting>true</EnableWindowsTargeting>` in `Apollo/Apollo.csproj` — no Windows host needed for CI. Output: `Apollo/bin/Release/Apollo.dll` + `runtimes/win-x64/whisper.dll`.

For in-game testing on Windows, point Dalamud → Settings → Experimental → Dev Plugin Locations at the build output directory.

There is no test suite, no linter config, and no CI workflow yet.

## Architecture

Apollo ("Apollo Accessibility") is a Dalamud (FFXIV) plugin with two accessibility features:

- **Speech-to-text (STT):** local Whisper inference turns your mic into chat-box text.
- **Text-to-speech (TTS):** reads incoming chat aloud via the system SAPI voice.

Settings persist via Dalamud's plugin config and are edited in an ImGui window. Layout:

- **`Apollo/Plugin.cs`** — Dalamud entrypoint. Owns `SpeechToTextManager`, `Chat.Chat`, `TextToSpeechManager`, the `Configuration`, and the two windows (`MainWindow`, `ConfigWindow`) via a `WindowSystem`. The `/apollo` command has one subcommand, `record`, which **toggles** capture (run it again to stop). Transcripts flow through a `_messageQueue` drained on the framework tick (1 s throttle) into chat — but only when `Configuration.SendTranscriptToChat` is on; otherwise they're printed locally via `ChatGui.Print`. Subscribes to `ChatGui.ChatMessage`: incoming messages on enabled channels are formatted (`"{sender} says: {body}"`) and enqueued to the TTS manager. `IsChannelEnabled` maps `XivChatType` → the per-channel `TtsSpeak*` config flags.
- **`Apollo/Configuration.cs`** — `IPluginConfiguration` (Version 1). STT toggles (`TrimTrailingSilenceEnabled`, `SilenceAutoStopEnabled`, `SendTranscriptToChat`) and TTS toggles (`EnableTts` plus a `TtsSpeak*` flag per chat channel). `Save()` calls `PluginInterface.SavePluginConfig`.
- **`Apollo/ModelDefinition.cs`** — `ModelDefinition` record + `ModelCatalog` of `BaseEn` / `SmallEn` / `MediumEn` (`ggml-*.en.bin`). `ModelCatalog.Default` is **MediumEn (~1.5 GB)**. Note: model choice is **not yet wired to config** — `SpeechToTextManager` takes an optional model arg but `Plugin` never passes one, so the default is always used. `ConfigWindow` shows the active model as read-only text.
- **`Apollo/SpeechToTextManager.cs`** — Audio capture + Whisper inference. On construction `CheckForDependancies()` (async-void, fire-and-forget) loads the model if present or downloads it in 80 KB chunks with `ChatGui.Print` progress every 10 MB, then builds the `WhisperFactory`. `RecordAudio()` opens a 16 kHz mono `NAudio.WaveInEvent`. VAD is an RMS state machine: a calibration window (`CalibrationBufferCount=5`) estimates a noise floor, then `WaitingForSpeech`→`Speaking` transitions use hysteresis (`speechThreshold = max(MinAbsoluteThreshold=300, noiseFloor*3)`, `silenceThreshold = speechThreshold*0.6`); `SilenceWindowMs=1500` of sub-threshold audio (or `MaxRecordingMs=60_000`) trips `StopRecording`. `StopRecording` optionally trims trailing silence (`TrimTrailingSilence`, a hand-rolled WAV `data`-chunk truncator), runs inference (`WithLanguage("en").WithNoContext()`), **concatenates all segments** (the old first-segment `break` is gone), strips bracketed/asterisked annotations, and emits via `RecordingFinished`.
- **`Apollo/Tts/`** — `ITtsProvider` (Speak/IsAvailable/Dispose). `SapiTtsProvider` wraps `System.Speech.Synthesis.SpeechSynthesizer` (Windows SAPI; `System.Speech` 9.0.0). `TextToSpeechManager` owns a `BlockingCollection<string>` queue drained on a background `Task`, speaking one line at a time and cancellable on dispose. **Both TTS files are noisy with `ChatGui.Print` debug lines** — that's intentional diagnostics for the experimental TTS branch, not finished UX.
- **`Apollo/Windows/`** — `MainWindow` shows status (downloading/recording/idle) and a start/stop button. `ConfigWindow` has STT and TTS tabs; TTS controls are disabled when no SAPI voice is available.
- **`Apollo/Chat/Chat.cs`** — Vendored chat-injection helper ported from [XivCommon](https://github.com/ascclemens/XIVCommon). Uses signature scanning (`ISigScanner`) to find the game's `ProcessChatBox` / `SanitiseString` functions and call them directly. **This is the fragile part:** game patches (~every 4 months) break these signatures. When that happens, cross-reference current XivCommon and [karashiiro/TextToTalk](https://github.com/karashiiro/TextToTalk) for updated sigs. 500-byte UTF-8 cap per send is a hard game limit.

STT flow: `/apollo record` → `SpeechToTextManager.RecordAudio` → NAudio fills buffers → RMS VAD trips `StopRecording` → trim + Whisper inference → `RecordingFinished` → `Plugin._messageQueue` (if `SendTranscriptToChat`) → framework-tick drain → `Chat.SendMessage` → game.

TTS flow: `ChatGui.ChatMessage` → channel filter → format → `TextToSpeechManager.Enqueue` → background consumer → `SapiTtsProvider.Speak`.

## Known sharp edges

`TODO.md` is the authoritative list and worth reading before touching anything. 

## Conventions

- C# `LangVersion` is pinned to 11.0 even though TFM is `net10.0-windows7.0`. Don't bump it casually — it's intentional for Dalamud SDK compatibility.
- Nullable is enabled and the build is warning-clean (0 warnings) — keep it that way.
- `WhisperFactory.FromPath` is given a hardcoded Windows runtime path (`{basePath}\runtimes\win-x64\whisper.dll`); this only matters at runtime on Windows, not for the Linux cross-build.

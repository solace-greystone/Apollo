# Apollo — TODO

Known issues carried over from the source plugin or introduced by the extraction. None of these block v1 functionality, but each is a real footgun.

## Model download (`SpeechToTextManager.CheckForDependancies`)

- [ ] **Resumable / atomic download.** Currently `File.OpenWrite(_modelName)` at `SpeechToTextManager.cs:59` followed by a chunked copy — if the connection drops, a partial file is left on disk. On next launch `File.Exists` returns `true`, so we try to load a corrupt model. Fix: download to `<file>.tmp`, `File.Move` on success only.
- [ ] **No `CancellationToken`.** Disposing the plugin during the initial download doesn't abort it (`CheckForDependancies` at `SpeechToTextManager.cs:50` is `async void` and untracked). Fix: plumb a token tied to plugin lifetime and await/cancel it in `Dispose`.

## VAD / recording

- [ ] **Hardcoded thresholds and silence window.** Defaults are `CalibrationBufferCount=5`, `SpeechMultiplier=3.0`, `SilenceMultiplier=0.6`, `MinAbsoluteThreshold=300`, `SilenceWindowMs=1500`, `MaxRecordingMs=60_000`. Won't suit every mic / room. The settings window now exists (`Windows/ConfigWindow.cs`); these just need to be promoted to `Configuration` fields and surfaced there.
- [ ] **No way to cancel without transcribing.** `/apollo record` toggles stop, but stopping always runs inference. If you misspeak you still pay for Whisper and then have to delete the resulting chat. Fix: a cancel path (command or button) that aborts before `ProcessAsync`.

## Transcription

- [ ] **Hardcoded English (`WithLanguage("en")` at `SpeechToTextManager.cs:218`).** Fix: config-driven, or `auto`.
- [ ] **Punctuation hack.** The `Replace("]", "[")…Split("[")[0]` chain at `SpeechToTextManager.cs:231` is trying to strip Whisper's bracketed/asterisked annotations (e.g. `[BLANK_AUDIO]`, `(music)`, `*laughs*`) but it's brittle and also discards anything after a parenthesis/bracket/asterisk the user actually said. Fix: a proper regex against known Whisper non-speech markers.
- [ ] **Model selection not wired to config.** `ModelCatalog` (`ModelDefinition.cs`) defines Base/Small/Medium English models and `SpeechToTextManager` accepts an optional `ModelDefinition`, but `Plugin` never passes one, so `ModelCatalog.Default` (Medium, ~1.5 GB) is always used. `ConfigWindow` shows the active model as read-only text. Fix: persist a model choice in `Configuration` and let the user pick it.

## Chat injection

- [ ] **Game-patch fragility.** `Chat/Chat.cs` uses memory signatures (`SendChat`, `SanitiseString`) lifted from XivCommon. Per the source repo's `CLAUDE.md`, "Roughly every 4 months new patches are released that break critical aspects of this plugin." Apollo will need the same maintenance — cross-reference `karashiiro/TextToTalk` when signatures break.
- [ ] **500-byte cap.** `Chat.SendMessage` throws on messages longer than 500 UTF-8 bytes (a game limit). `Plugin.OnFrameworkUpdate` catches and logs the throw, so long transcripts are silently dropped. Fix: chunk the transcript across multiple sends.
- [ ] **`InvalidCharacters` exception.** `Chat.SendMessage` throws if `SanitiseText` strips anything. Whisper sometimes emits characters the game rejects. Fix: catch and re-send the sanitized version instead of dropping it.

## Plugin plumbing

- [ ] **CI.** No GitHub Actions workflow yet — Linux build is verified locally only.
- [ ] **Manifest icon.** `Apollo.json` has no `IconUrl`. Dalamud will show a placeholder.

## TTS (experimental)

- [ ] **Debug-print spam.** `Tts/SapiTtsProvider.cs` and `Tts/TextToSpeechManager.cs` emit verbose `ChatGui.Print` diagnostics (voice enumeration, init steps) on every launch. Demote to `IPluginLog` or remove before shipping.
- [ ] **SAPI-only / Windows-only.** TTS goes through `System.Speech` SAPI with no voice/rate/volume selection, and degrades to "no provider available" off Windows. Fix: expose voice + rate config; consider a fallback provider.

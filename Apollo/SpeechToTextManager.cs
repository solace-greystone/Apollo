using NAudio.Wave;
using System.Diagnostics;
using Whisper.net;
using Whisper.net.Ggml;

namespace Apollo {
    public class SpeechToTextManager {
        private readonly Stopwatch _timer = new Stopwatch();
        private WaveInEvent? _waveSource;
        private MemoryStream? _recordedAudioStream;
        private WaveFileWriter? _waveWriter;
        private readonly string _basePath;
        private readonly ModelDefinition _model;
        private readonly Configuration _config;
        private readonly string _modelName;
        private string _finalText = "";
        private bool _isRecording;
        private bool _isModelReady;
        private WhisperFactory? _whisperFactory;
        private readonly object _factoryLock = new object();

        private enum VadState { WaitingForSpeech, Speaking }
        private VadState _vadState;
        private double _noiseFloor;
        private double _calibrationSum;
        private int _calibrationCount;
        private long _lastSpeechByteOffset;
        private readonly Stopwatch _recordingDuration = new Stopwatch();

        private const int CalibrationBufferCount = 5;
        private const double SpeechMultiplier = 3.0;
        private const double SilenceMultiplier = 0.6;
        private const double MinAbsoluteThreshold = 300.0;
        private const int SilenceWindowMs = 1500;
        private const int MaxRecordingMs = 60_000;
        private const int TailPadMs = 100;

        public string FinalText { get => _finalText; set => _finalText = value; }
        public bool IsRecording { get => _isRecording; set => _isRecording = value; }
        public bool IsModelReady => _isModelReady;

        public SpeechToTextManager(string path, Configuration config, ModelDefinition? model = null) {
            _basePath = path;
            _config = config;
            _model = model ?? ModelCatalog.Default;
            _modelName = Path.Combine(path, _model.FileName);
            CheckForDependancies();
        }
        public async void CheckForDependancies() {
            try {
                if (File.Exists(_modelName)) {
                    EnsureFactoryLoaded();
                    _isModelReady = true;
                    return;
                }
                {
                    using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(_model.GgmlType);
                    using var fileWriter = File.OpenWrite(_modelName);

                    long totalLength = 0;
                    try { totalLength = modelStream.Length; } catch { }

                    Plugin.ChatGui.Print($"Apollo: downloading Whisper model {_model.DisplayName} (~{_model.ApproxSizeMb} MB)...");

                    var buffer = new byte[81920];
                    long totalRead = 0;
                    long lastReportedBytes = 0;
                    const long reportEveryBytes = 10 * 1024 * 1024;
                    int read;
                    while ((read = await modelStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                        await fileWriter.WriteAsync(buffer, 0, read);
                        totalRead += read;
                        if (totalRead - lastReportedBytes >= reportEveryBytes) {
                            lastReportedBytes = totalRead;
                            if (totalLength > 0) {
                                var pct = (int)(totalRead * 100 / totalLength);
                                Plugin.ChatGui.Print($"Apollo: model download {pct}% ({totalRead / (1024 * 1024)} / {totalLength / (1024 * 1024)} MB)");
                            } else {
                                Plugin.ChatGui.Print($"Apollo: model download {totalRead / (1024 * 1024)} MB");
                            }
                        }
                    }

                    Plugin.ChatGui.Print("Apollo: model download complete.");
                    EnsureFactoryLoaded();
                    _isModelReady = true;
                }
                if (_whisperFactory == null) {
                    Plugin.ChatGui.Print($"Apollo: WhisperFactory failed to initialise for {_model.DisplayName} ({_modelName}). Inference will be skipped.");
                    _isModelReady = false;
                }
            } catch (Exception ex) {
                Plugin.ChatGui.Print($"Apollo: model preparation failed: {ex.GetType().Name}: {ex.Message}");
                _isModelReady = false;
            }
        }
        public event EventHandler<string>? RecordingFinished;

        public void RecordAudio() {
            _waveSource = new WaveInEvent {
                WaveFormat = new WaveFormat(16000, 1),
            };

            _waveSource.DataAvailable += DataAvailable;
            _waveSource.RecordingStopped += RecordingStopped;
            _recordedAudioStream = new MemoryStream();
            _waveWriter = new WaveFileWriter(_recordedAudioStream, _waveSource.WaveFormat);
            _vadState = VadState.WaitingForSpeech;
            _noiseFloor = 0;
            _calibrationSum = 0;
            _calibrationCount = 0;
            _lastSpeechByteOffset = 0;
            _timer.Reset();
            _recordingDuration.Restart();
            _waveSource.StartRecording();
            _isRecording = true;
        }

        private void DataAvailable(object? sender, WaveInEventArgs e) {
            if (_waveWriter == null) return;
            _waveWriter.Write(e.Buffer, 0, e.BytesRecorded);

            if (_recordingDuration.ElapsedMilliseconds > MaxRecordingMs) {
                StopRecording();
                return;
            }

            if (!_config.SilenceAutoStopEnabled) return;

            var rms = ComputeRms(e.Buffer, e.BytesRecorded);

            if (_calibrationCount < CalibrationBufferCount) {
                _calibrationSum += rms;
                _calibrationCount++;
                if (_calibrationCount == CalibrationBufferCount) {
                    _noiseFloor = _calibrationSum / CalibrationBufferCount;
                }
                return;
            }

            var speechThreshold = Math.Max(MinAbsoluteThreshold, _noiseFloor * SpeechMultiplier);
            var silenceThreshold = speechThreshold * SilenceMultiplier;

            if (rms >= silenceThreshold) {
                _lastSpeechByteOffset = _waveWriter.Position;
            }

            if (_vadState == VadState.WaitingForSpeech) {
                if (rms >= speechThreshold) {
                    _vadState = VadState.Speaking;
                    _timer.Reset();
                }
                return;
            }

            if (rms < silenceThreshold) {
                if (!_timer.IsRunning) _timer.Start();
                if (_timer.ElapsedMilliseconds > SilenceWindowMs) {
                    StopRecording();
                }
            } else {
                _timer.Reset();
            }
        }

        private static double ComputeRms(byte[] buffer, int bytesRecorded) {
            var sampleCount = bytesRecorded / 2;
            if (sampleCount <= 0) return 0;
            long sumSquares = 0;
            for (var i = 0; i + 1 < bytesRecorded; i += 2) {
                int sample = BitConverter.ToInt16(buffer, i);
                sumSquares += (long)sample * sample;
            }
            return Math.Sqrt(sumSquares / (double)sampleCount);
        }

        public async void StopRecording() {
            var waveSource = _waveSource;
            var recordedStream = _recordedAudioStream;
            if (waveSource == null || recordedStream == null) {
                _isRecording = false;
                return;
            }

            waveSource.StopRecording();
            waveSource.DataAvailable -= DataAvailable;
            waveSource.RecordingStopped -= RecordingStopped;

            var format = waveSource.WaveFormat;
            var lastSpeechOffset = _lastSpeechByteOffset;

            try { _waveWriter?.Flush(); } catch { }
            try { _waveWriter?.Dispose(); } catch { }
            try { waveSource.Dispose(); } catch { }

            // _waveWriter.Dispose() above also closed _recordedAudioStream; rebuild
            // a fresh MemoryStream from its buffer (ToArray works post-dispose).
            MemoryStream audioForWhisper;
            try {
                var wav = recordedStream.ToArray();
                byte[]? trimmed = null;
                if (_config.TrimTrailingSilenceEnabled) {
                    try {
                        trimmed = TrimTrailingSilence(wav, lastSpeechOffset, format);
                    } catch (Exception ex) {
                        Plugin.ChatGui.Print($"Apollo: silence-trim failed, using untrimmed audio: {ex.GetType().Name}: {ex.Message}");
                    }
                }
                recordedStream.Dispose();
                _recordedAudioStream = new MemoryStream(trimmed ?? wav);
                audioForWhisper = _recordedAudioStream;
            } catch (Exception ex) {
                Plugin.ChatGui.Print($"Apollo: could not prepare audio for inference: {ex.GetType().Name}: {ex.Message}");
                _isRecording = false;
                return;
            }

            var factory = EnsureFactoryLoaded();
            if (factory == null) {
                Plugin.ChatGui.Print($"Apollo: no Whisper factory available (model: {_model.DisplayName}). Skipping inference.");
            } else {
                try {
                    using var processor = factory.CreateBuilder()
                        .WithLanguage("en")
                        .WithNoContext()
                        //.WithTemperature(0.0f)
                        //.WithNoSpeechThreshold(0.6f)
                        .Build();

                    Plugin.ChatGui.Print($"Apollo: running inference with {_model.DisplayName} on {audioForWhisper.Length} bytes...");
                    _finalText = "";
                    audioForWhisper.Position = 0;
                    int segmentCount = 0;
                    await foreach (var result in processor.ProcessAsync(audioForWhisper)) {
                        segmentCount++;
                        Console.WriteLine($"{result.Start}->{result.End}: {result.Text}");
                        _finalText += result.Text.Replace("]", "[").Replace("(", "[").Replace(")", "[").Replace("*", "[").Split("[")[0];
                    }
                    _finalText = FinalText.Trim();
                    Plugin.ChatGui.Print($"Apollo: inference complete — {segmentCount} segment(s), {_finalText.Length} char(s) after filtering.");
                    RecordingFinished?.Invoke(this, _finalText);
                } catch (Exception ex) {
                    Plugin.ChatGui.Print($"Apollo: inference threw {ex.GetType().Name}: {ex.Message}");
                }
            }
            _timer.Reset();
            _recordingDuration.Reset();
            _isRecording = false;
        }

        private WhisperFactory? EnsureFactoryLoaded() {
            if (_whisperFactory != null) return _whisperFactory;
            if (!File.Exists(_modelName)) {
                Plugin.ChatGui.Print($"Apollo: model file not found at {_modelName}.");
                return null;
            }
            lock (_factoryLock) {
                if (_whisperFactory != null) return _whisperFactory;
                try {
                    var fileLen = new FileInfo(_modelName).Length;
                    Plugin.ChatGui.Print($"Apollo: loading WhisperFactory for {_model.DisplayName} ({fileLen / (1024 * 1024)} MB)...");
                    _whisperFactory = WhisperFactory.FromPath(_modelName, false, _basePath + @"\runtimes\win-x64\whisper.dll");
                    Plugin.ChatGui.Print($"Apollo: WhisperFactory loaded for {_model.DisplayName}.");
                } catch (Exception ex) {
                    Plugin.ChatGui.Print($"Apollo: WhisperFactory load failed for {_model.DisplayName}: {ex.GetType().Name}: {ex.Message}");
                    _whisperFactory = null;
                }
            }
            return _whisperFactory;
        }

        public void Dispose() {
            lock (_factoryLock) {
                try { _whisperFactory?.Dispose(); } catch { }
                _whisperFactory = null;
            }
        }

        private static byte[]? TrimTrailingSilence(byte[] wav, long lastSpeechOffset, WaveFormat format) {
            if (wav.Length < 44 || lastSpeechOffset <= 0) return null;
            if (wav[0] != (byte)'R' || wav[1] != (byte)'I' || wav[2] != (byte)'F' || wav[3] != (byte)'F') return null;
            if (wav[8] != (byte)'W' || wav[9] != (byte)'A' || wav[10] != (byte)'V' || wav[11] != (byte)'E') return null;

            int pos = 12;
            int dataChunkOffset = -1;
            int dataChunkSize = -1;
            while (pos + 8 <= wav.Length) {
                int chunkSize = BitConverter.ToInt32(wav, pos + 4);
                if (wav[pos] == (byte)'d' && wav[pos + 1] == (byte)'a' && wav[pos + 2] == (byte)'t' && wav[pos + 3] == (byte)'a') {
                    dataChunkOffset = pos + 8;
                    dataChunkSize = chunkSize;
                    break;
                }
                pos += 8 + chunkSize;
                if ((chunkSize & 1) == 1) pos++;
            }
            if (dataChunkOffset < 0 || dataChunkSize <= 0) return null;

            int dataEnd = dataChunkOffset + dataChunkSize;
            int blockAlign = format.BlockAlign > 0 ? format.BlockAlign : 2;
            int padBytes = (TailPadMs * format.SampleRate / 1000) * blockAlign;
            long desiredEnd = lastSpeechOffset + padBytes;
            if (desiredEnd >= dataEnd) return null;
            if (desiredEnd <= dataChunkOffset) return null;

            int trimmedDataSize = (int)(desiredEnd - dataChunkOffset);
            trimmedDataSize -= trimmedDataSize % blockAlign;
            if (trimmedDataSize <= 0) return null;

            int newLength = dataChunkOffset + trimmedDataSize;
            var result = new byte[newLength];
            Buffer.BlockCopy(wav, 0, result, 0, newLength);
            BitConverter.GetBytes(trimmedDataSize).CopyTo(result, dataChunkOffset - 4);
            BitConverter.GetBytes(newLength - 8).CopyTo(result, 4);
            return result;
        }

        private void RecordingStopped(object? sender, StoppedEventArgs e) {

        }
    }
}

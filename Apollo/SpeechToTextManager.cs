using NAudio.Wave;
using System.Diagnostics;
using Whisper.net;
using Whisper.net.Ggml;

namespace Apollo {
    public class SpeechToTextManager {
        private Stopwatch _timer = new Stopwatch();
        private WaveInEvent _waveSource;
        private MemoryStream _recordedAudioStream;
        private WaveFileWriter _waveWriter;
        private string _basePath;
        private ModelDefinition _model;
        private string _modelName;
        private string _finalText;
        int _retry;
        private bool _isRecording;
        private bool _isModelReady;

        private enum VadState { WaitingForSpeech, Speaking }
        private VadState _vadState;
        private double _noiseFloor;
        private double _calibrationSum;
        private int _calibrationCount;
        private readonly Stopwatch _recordingDuration = new Stopwatch();

        private const int CalibrationBufferCount = 5;
        private const double SpeechMultiplier = 3.0;
        private const double SilenceMultiplier = 0.6;
        private const double MinAbsoluteThreshold = 300.0;
        private const int SilenceWindowMs = 1500;
        private const int MaxRecordingMs = 60_000;

        public string FinalText { get => _finalText; set => _finalText = value; }
        public bool IsRecording { get => _isRecording; set => _isRecording = value; }
        public bool IsModelReady => _isModelReady;

        public SpeechToTextManager(string path, ModelDefinition? model = null) {
            _basePath = path;
            _model = model ?? ModelCatalog.Default;
            _modelName = Path.Combine(path, _model.FileName);
            CheckForDependancies();
        }
        public async void CheckForDependancies() {
            try {
                if (File.Exists(_modelName)) {
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
                    _isModelReady = true;
                }
            } catch {

            }
        }
        public event EventHandler<string> RecordingFinished;

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
            _timer.Reset();
            _recordingDuration.Restart();
            _waveSource.StartRecording();
            _isRecording = true;
        }

        private void DataAvailable(object sender, WaveInEventArgs e) {
            if (_waveWriter == null) return;
            _waveWriter.Write(e.Buffer, 0, e.BytesRecorded);

            if (_recordingDuration.ElapsedMilliseconds > MaxRecordingMs) {
                StopRecording();
                return;
            }

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
            _waveSource?.StopRecording();
            _waveWriter.Flush();
            _waveSource.DataAvailable -= DataAvailable;
            _waveSource.RecordingStopped -= RecordingStopped;
            if (File.Exists(_modelName)) {
                try {
                    using var whisperFactory = WhisperFactory.FromPath(_modelName, false, _basePath + @"\runtimes\win-x64\whisper.dll");

                    using var processor = whisperFactory.CreateBuilder()
                        .WithLanguage("en")
                        .Build();

                    _finalText = "";
                    _recordedAudioStream.Position = 0;
                    await foreach (var result in processor.ProcessAsync(_recordedAudioStream)) {
                        Console.WriteLine($"{result.Start}->{result.End}: {result.Text}");
                        _finalText += result.Text.Replace("]", "[").Replace("(", "[").Replace(")", "[").Replace("*", "[").Split("[")[0];
                    }
                    _finalText = FinalText.Trim();
                    RecordingFinished?.Invoke(this, _finalText);
                } catch {

                }
            }
            try {
                _waveSource?.Dispose();
                _waveWriter?.Dispose();
            } catch { }
            _timer.Reset();
            _recordingDuration.Reset();
            _isRecording = false;
        }

        private void RecordingStopped(object sender, StoppedEventArgs e) {

        }
    }
}

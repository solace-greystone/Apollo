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
        private string _modelName;
        private string _finalText;
        int _retry;
        private bool _isRecording;
        private bool _rpMode;

        public string FinalText { get => _finalText; set => _finalText = value; }
        public bool IsRecording { get => _isRecording; set => _isRecording = value; }
        public bool RpMode { get => _rpMode; set => _rpMode = value; }

        public SpeechToTextManager(string path) {
            _basePath = path;
            _modelName = Path.Combine(path, "ggml-base.bin");
            CheckForDependancies();
        }
        public async void CheckForDependancies() {
            try {
                if (!File.Exists(_modelName)) {
                    using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(GgmlType.Base);
                    using var fileWriter = File.OpenWrite(_modelName);
                    await modelStream.CopyToAsync(fileWriter);
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
            _waveSource.StartRecording();
            _isRecording = true;
        }

        private void DataAvailable(object sender, WaveInEventArgs e) {
            if (_waveWriter != null) {
                _waveWriter.Write(e.Buffer, 0, e.BytesRecorded);
                int threshold = 500;
                int value = Math.Abs(BitConverter.ToInt16(e.Buffer, (e.BytesRecorded - 2)));
                if (value < threshold) {
                    if (!_timer.IsRunning) {
                        _timer.Start();
                    }
                    if (_timer.ElapsedMilliseconds > 1000) {
                        StopRecording();
                    }
                } else {
                    _timer.Reset();
                }
            }
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
                        break;
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
            _isRecording = false;
        }

        private void RecordingStopped(object sender, StoppedEventArgs e) {

        }
    }
}

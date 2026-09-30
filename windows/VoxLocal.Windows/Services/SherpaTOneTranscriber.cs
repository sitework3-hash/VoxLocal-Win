using System.Threading.Channels;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SherpaOnnx;
using VoxLocal.Win.Core;

namespace VoxLocal.Win.Services;

/// <summary>
/// Local Russian streaming recognizer backed by the official Sherpa-ONNX
/// T-One CTC model. The recognizer stays alive between dictations so that the
/// model is not loaded again for every hotkey release.
/// </summary>
public sealed class SherpaTOneTranscriber : IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _recognitionGate = new(1, 1);
    private OnlineRecognizer? _recognizer;
    private int _threads;

    public static string ModelPath => Path.Combine(AppPaths.SherpaTOneDirectory, "model.onnx");
    public static string TokensPath => Path.Combine(AppPaths.SherpaTOneDirectory, "tokens.txt");

    public static bool IsInstalled() =>
        File.Exists(ModelPath) && new FileInfo(ModelPath).Length > 100_000_000 && File.Exists(TokensPath);

    public Task<WhisperTranscript> TranscribeAsync(
        string audioPath,
        int threads,
        CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            await _recognitionGate.WaitAsync(cancellationToken);
            try { return Transcribe(audioPath, threads, cancellationToken); }
            finally { _recognitionGate.Release(); }
        }, cancellationToken);

    public LiveSession StartLiveSession(
        int threads,
        Action<string> previewChanged,
        CancellationToken cancellationToken)
    {
        if (!IsInstalled())
            throw new FileNotFoundException(
                "Русская быстрая модель Sherpa-ONNX не установлена. Выполните scripts/windows/download_sherpa_model.ps1.",
                ModelPath);

        _recognitionGate.Wait(cancellationToken);
        try
        {
            return new LiveSession(
                GetRecognizer(threads), previewChanged, cancellationToken, () => _recognitionGate.Release());
        }
        catch
        {
            _recognitionGate.Release();
            throw;
        }
    }

    public Task WarmUpAsync(int threads) =>
        Task.Run(() =>
        {
            if (IsInstalled())
                _ = GetRecognizer(threads);
        });

    private WhisperTranscript Transcribe(string audioPath, int threads, CancellationToken cancellationToken)
    {
        if (!File.Exists(audioPath))
            throw new FileNotFoundException("Временный WAV-файл исчез.", audioPath);
        if (!IsInstalled())
            throw new FileNotFoundException(
                "Русская быстрая модель Sherpa-ONNX не установлена. Выполните scripts/windows/download_sherpa_model.ps1.",
                ModelPath);

        var recognizer = GetRecognizer(threads);
        using var stream = recognizer.CreateStream();
        using var reader = new WaveFileReader(audioPath);
        var samples = reader.ToSampleProvider();
        var buffer = new float[Math.Max(reader.WaveFormat.SampleRate / 5, 1600)];

        AddLeadingSilence(stream, reader.WaveFormat.SampleRate);

        int read;
        while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stream.AcceptWaveform(reader.WaveFormat.SampleRate, buffer[..read]);
            DecodeReady(recognizer, stream, cancellationToken);
        }

        FinishStream(recognizer, stream, reader.WaveFormat.SampleRate, cancellationToken);
        return ReadTranscript(recognizer, stream);
    }

    private OnlineRecognizer GetRecognizer(int requestedThreads)
    {
        var threads = Math.Clamp(requestedThreads, 1, 4);
        lock (_sync)
        {
            if (_recognizer is not null && _threads == threads)
                return _recognizer;

            _recognizer?.Dispose();
            var config = new OnlineRecognizerConfig();
            // T-One was trained at 8 kHz. Sherpa resamples the 16 kHz audio
            // captured by VoxLocal when it is accepted by the stream.
            config.FeatConfig.SampleRate = 8000;
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.ToneCtc.Model = ModelPath;
            config.ModelConfig.Tokens = TokensPath;
            config.ModelConfig.Provider = "cpu";
            config.ModelConfig.NumThreads = threads;
            config.DecodingMethod = "greedy_search";
            _recognizer = new OnlineRecognizer(config);
            _threads = threads;
            AppLog.Shared.Info($"Sherpa T-One initialized ({threads} thread(s))");
            return _recognizer;
        }
    }

    private static void AddLeadingSilence(OnlineStream stream, int sampleRate) =>
        stream.AcceptWaveform(sampleRate, new float[(int)(sampleRate * 0.3)]);

    private static void FinishStream(
        OnlineRecognizer recognizer,
        OnlineStream stream,
        int sampleRate,
        CancellationToken cancellationToken)
    {
        stream.AcceptWaveform(sampleRate, new float[(int)(sampleRate * 0.6)]);
        stream.InputFinished();
        DecodeReady(recognizer, stream, cancellationToken);
    }

    private static WhisperTranscript ReadTranscript(OnlineRecognizer recognizer, OnlineStream stream)
    {
        var result = recognizer.GetResult(stream);
        var text = WhisperOutputParser.NormalizeWhitespace(result.Text);
        // Silence, an accidental hotkey press, or a very short utterance can
        // legitimately produce no CTC tokens. The controller treats this as
        // "nothing recognized" and skips cloud refinement and insertion.
        return new WhisperTranscript(text, "ru");
    }

    private static void DecodeReady(
        OnlineRecognizer recognizer,
        OnlineStream stream,
        CancellationToken cancellationToken)
    {
        while (recognizer.IsReady(stream))
        {
            cancellationToken.ThrowIfCancellationRequested();
            recognizer.Decode(stream);
        }
    }

    public sealed class LiveSession : IDisposable
    {
        private const int SampleRate = 16_000;
        private static readonly TimeSpan PreviewInterval = TimeSpan.FromMilliseconds(250);
        private readonly OnlineRecognizer _recognizer;
        private readonly OnlineStream _stream;
        private readonly Action<string> _previewChanged;
        private readonly Action _releaseRecognizer;
        private readonly CancellationTokenSource _cancellation;
        private readonly Channel<byte[]> _audio = Channel.CreateUnbounded<byte[]>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        private readonly Task<WhisperTranscript> _worker;
        private int _completed;
        private int _disposed;

        internal LiveSession(
            OnlineRecognizer recognizer,
            Action<string> previewChanged,
            CancellationToken cancellationToken,
            Action releaseRecognizer)
        {
            _recognizer = recognizer;
            _previewChanged = previewChanged;
            _releaseRecognizer = releaseRecognizer;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _stream = recognizer.CreateStream();
            AddLeadingSilence(_stream, SampleRate);
            _worker = Task.Run(ProcessAsync);
        }

        public void AcceptPcm16(byte[] buffer, int count)
        {
            if (count <= 0 || Volatile.Read(ref _completed) != 0)
                return;

            var copy = new byte[count];
            Buffer.BlockCopy(buffer, 0, copy, 0, count);
            _audio.Writer.TryWrite(copy);
        }

        public async Task<WhisperTranscript> CompleteAsync()
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0)
                _audio.Writer.TryComplete();
            return await _worker;
        }

        public void Cancel()
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0)
                _audio.Writer.TryComplete();
            try { _cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        private async Task<WhisperTranscript> ProcessAsync()
        {
            var lastPreview = "";
            var lastPublished = Stopwatch.StartNew();
            try
            {
                await foreach (var bytes in _audio.Reader.ReadAllAsync(_cancellation.Token))
                {
                    var samples = ConvertPcm16(bytes);
                    _stream.AcceptWaveform(SampleRate, samples);
                    DecodeReady(_recognizer, _stream, _cancellation.Token);

                    if (lastPublished.Elapsed >= PreviewInterval)
                    {
                        var preview = ReadTranscript(_recognizer, _stream).Text;
                        if (!string.Equals(preview, lastPreview, StringComparison.Ordinal))
                        {
                            lastPreview = preview;
                            try { _previewChanged(preview); }
                            catch (Exception error)
                            {
                                AppLog.Shared.Info($"Live transcription preview skipped: {error.Message}");
                            }
                        }
                        lastPublished.Restart();
                    }
                }

                FinishStream(_recognizer, _stream, SampleRate, _cancellation.Token);
                var transcript = ReadTranscript(_recognizer, _stream);
                if (!string.Equals(transcript.Text, lastPreview, StringComparison.Ordinal))
                {
                    try { _previewChanged(transcript.Text); }
                    catch (Exception error)
                    {
                        AppLog.Shared.Info($"Live transcription final preview skipped: {error.Message}");
                    }
                }
                return transcript;
            }
            finally
            {
                _stream.Dispose();
                _releaseRecognizer();
                _cancellation.Dispose();
            }
        }

        private static float[] ConvertPcm16(byte[] bytes)
        {
            var samples = new float[bytes.Length / 2];
            for (var index = 0; index < samples.Length; index++)
                samples[index] = BitConverter.ToInt16(bytes, index * 2) / 32768f;
            return samples;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            if (!_worker.IsCompleted)
                Cancel();
        }
    }

    public void Dispose()
    {
        _recognitionGate.Wait();
        try
        {
            lock (_sync)
            {
                _recognizer?.Dispose();
                _recognizer = null;
            }
        }
        finally
        {
            _recognitionGate.Release();
            _recognitionGate.Dispose();
        }
    }
}

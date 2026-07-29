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
        Task.Run(() => Transcribe(audioPath, threads, cancellationToken), cancellationToken);

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
        var stream = recognizer.CreateStream();
        using var reader = new WaveFileReader(audioPath);
        var samples = reader.ToSampleProvider();
        var buffer = new float[Math.Max(reader.WaveFormat.SampleRate / 5, 1600)];

        // Match the model's own reference example: a short silent lead-in and
        // tail prevent CTC decoding from clipping words at push-to-talk edges.
        stream.AcceptWaveform(reader.WaveFormat.SampleRate,
            new float[(int)(reader.WaveFormat.SampleRate * 0.3)]);

        int read;
        while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stream.AcceptWaveform(reader.WaveFormat.SampleRate, buffer[..read]);
            DecodeReady(recognizer, stream, cancellationToken);
        }

        stream.AcceptWaveform(reader.WaveFormat.SampleRate,
            new float[(int)(reader.WaveFormat.SampleRate * 0.6)]);
        stream.InputFinished();
        DecodeReady(recognizer, stream, cancellationToken);
        var result = recognizer.GetResult(stream);
        var text = WhisperOutputParser.NormalizeWhitespace(result.Text);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("Sherpa-ONNX вернул пустой текст.");

        return new WhisperTranscript(text, "ru");
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

    public void Dispose()
    {
        lock (_sync)
        {
            _recognizer?.Dispose();
            _recognizer = null;
        }
    }
}

using NAudio.Wave;
using VoxLocal.Win.Core;

namespace VoxLocal.Win.Services;

public sealed record RecordingResult(string FilePath, TimeSpan Duration, float PeakLevel);

public sealed class AudioRecorder : IDisposable
{
    private readonly object _sync = new();
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private TaskCompletionSource<Exception?>? _stopped;
    private string? _filePath;
    private float _peak;

    public bool IsRecording { get; private set; }
    public event Action<float>? LevelChanged;

    public void Start(int deviceNumber = -1)
    {
        if (IsRecording)
            throw new InvalidOperationException("Запись уже запущена.");

        var path = Path.Combine(Path.GetTempPath(), $"voxlocal-{Guid.NewGuid():N}.wav");
        var waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(16_000, 16, 1),
            // Smaller buffers reduce the chance of losing a short first or
            // last syllable when the push-to-talk key is pressed quickly.
            BufferMilliseconds = 20,
            NumberOfBuffers = 5
        };
        var writer = new WaveFileWriter(path, waveIn.WaveFormat);
        waveIn.DataAvailable += OnDataAvailable;
        waveIn.RecordingStopped += OnRecordingStopped;

        lock (_sync)
        {
            _waveIn = waveIn;
            _writer = writer;
            _filePath = path;
            _peak = 0;
            _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        try
        {
            waveIn.StartRecording();
            IsRecording = true;
            AppLog.Shared.Info("Recording started (16000 Hz mono PCM16)");
        }
        catch
        {
            Cleanup(deleteFile: true);
            throw;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        float bufferPeak = 0;
        lock (_sync)
        {
            if (_writer is null)
                return;
            _writer.Write(args.Buffer, 0, args.BytesRecorded);
            for (var index = 0; index + 1 < args.BytesRecorded; index += 2)
            {
                var sample = BitConverter.ToInt16(args.Buffer, index);
                bufferPeak = Math.Max(bufferPeak, Math.Abs(sample / 32768f));
            }
            _peak = Math.Max(_peak, bufferPeak);
        }
        LevelChanged?.Invoke(Math.Clamp(bufferPeak, 0, 1));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args) =>
        _stopped?.TrySetResult(args.Exception);

    public async Task<RecordingResult> StopAsync()
    {
        if (!IsRecording || _waveIn is null)
            throw new InvalidOperationException("Запись не запущена.");

        IsRecording = false;
        _waveIn.StopRecording();
        var exception = await (_stopped?.Task ?? Task.FromResult<Exception?>(null));
        if (exception is not null)
        {
            Cleanup(deleteFile: true);
            throw exception;
        }

        string path;
        float peak;
        TimeSpan duration;
        lock (_sync)
        {
            path = _filePath!;
            peak = _peak;
            _writer?.Flush();
            duration = _writer?.TotalTime ?? TimeSpan.Zero;
        }
        Cleanup(deleteFile: false);

        if (duration.TotalSeconds < 0.35 || peak < 0.005f)
        {
            TryDelete(path);
            throw new InvalidOperationException("Запись слишком короткая или не содержит речи.");
        }
        AppLog.Shared.Info($"Recording stopped ({duration.TotalSeconds:F2}s)");
        return new(path, duration, peak);
    }

    public void Cancel()
    {
        if (_waveIn is not null && IsRecording)
        {
            IsRecording = false;
            try { _waveIn.StopRecording(); } catch { }
        }
        Cleanup(deleteFile: true);
        AppLog.Shared.Info("Recording cancelled");
    }

    private void Cleanup(bool deleteFile)
    {
        string? path;
        lock (_sync)
        {
            path = _filePath;
            if (_waveIn is not null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;
                _waveIn.Dispose();
            }
            _writer?.Dispose();
            _waveIn = null;
            _writer = null;
            _filePath = null;
            _stopped = null;
        }
        if (deleteFile && path is not null)
            TryDelete(path);
    }

    public static IReadOnlyList<string> InputDevices()
    {
        var devices = new List<string>();
        for (var index = 0; index < WaveIn.DeviceCount; index++)
            devices.Add(WaveIn.GetCapabilities(index).ProductName);
        return devices;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    public void Dispose() => Cancel();
}

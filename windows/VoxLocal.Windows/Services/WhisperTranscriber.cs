using System.Diagnostics;
using System.Text;
using VoxLocal.Win.Core;

namespace VoxLocal.Win.Services;

public sealed class WhisperTranscriber
{
    // A 750-frame context covers normal short dictation while avoiding the
    // fixed 30-second encoder pass. Longer recordings keep the full context
    // so that their ending is never truncated.
    public static readonly TimeSpan ShortDictationLimit = TimeSpan.FromSeconds(12);
    public const int ShortDictationAudioContext = 750;

    public string LocateBinary()
    {
        var candidates = new List<string>();
        var environment = Environment.GetEnvironmentVariable("VOXLOCAL_WHISPER_CLI");
        if (!string.IsNullOrWhiteSpace(environment))
            candidates.Add(environment);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "whisper-cli.exe"));

        var repository = Environment.GetEnvironmentVariable("VOXLOCAL_REPO_ROOT")
                         ?? Directory.GetCurrentDirectory();
        var vendor = Path.Combine(repository, "vendor", "whisper");
        if (Directory.Exists(vendor))
            candidates.AddRange(Directory.EnumerateFiles(
                vendor, "whisper-cli.exe", SearchOption.AllDirectories));

        return candidates.FirstOrDefault(File.Exists)
               ?? throw new FileNotFoundException(
                   "whisper-cli.exe не найден. Выполните scripts/windows/bootstrap.ps1.");
    }

    public async Task<WhisperTranscript> TranscribeAsync(
        string audioPath,
        TimeSpan audioDuration,
        string modelPath,
        string language,
        int threads,
        bool removeArtifacts,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(audioPath))
            throw new FileNotFoundException("Временный WAV-файл исчез.", audioPath);
        if (!File.Exists(modelPath) || new FileInfo(modelPath).Length < 1_000_000)
            throw new FileNotFoundException("Модель Whisper не установлена.", modelPath);

        var binary = LocateBinary();
        var outputBase = Path.Combine(Path.GetTempPath(), $"voxlocal-out-{Guid.NewGuid():N}");
        var outputJson = outputBase + ".json";
        var startInfo = new ProcessStartInfo(binary)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(binary)!
        };
        foreach (var argument in BuildArguments(
                     modelPath,
                     audioPath,
                     language,
                     threads,
                     outputBase,
                     SelectAudioContext(audioDuration)))
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        AppLog.Shared.Info($"whisper-cli starting (model {Path.GetFileName(modelPath)})");
        if (!process.Start())
            throw new InvalidOperationException("Не удалось запустить whisper-cli.exe.");
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            try { process.Kill(true); } catch { }
            throw;
        }
        var stderr = await stderrTask;
        _ = await stdoutTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"whisper-cli завершился с кодом {process.ExitCode}: {stderr[^Math.Min(600, stderr.Length)..]}");
        if (!File.Exists(outputJson))
            throw new InvalidDataException("whisper-cli не создал JSON-результат.");

        try
        {
            var parsed = WhisperOutputParser.Parse(
                await File.ReadAllTextAsync(outputJson, cancellationToken), removeArtifacts);
            if (string.IsNullOrWhiteSpace(parsed.Text))
                throw new InvalidDataException("Whisper вернул пустой текст.");
            return parsed;
        }
        finally
        {
            try { File.Delete(outputJson); } catch { }
        }
    }

    public static IReadOnlyList<string> BuildArguments(
        string modelPath,
        string audioPath,
        string language,
        int threads,
        string outputBase,
        int audioContext = 0)
    {
        var arguments = new List<string>
        {
            "-m", modelPath,
            "-f", audioPath,
            "-l", language,
            "-t", Math.Clamp(threads, 1, 16).ToString(),
        };
        if (audioContext > 0)
            arguments.AddRange(["-ac", audioContext.ToString()]);
        arguments.AddRange(
        [
            "-oj",
            "-of", outputBase,
            "-np",
            "-nt",              // dictation does not need segment timestamps
            "-bs", "1",        // greedy decoding keeps CPU latency reasonable
            "-bo", "1",
            "-nth", "0.8"      // do not drop very short, quiet utterances too eagerly
        ]);
        return arguments;
    }

    public static int SelectAudioContext(TimeSpan audioDuration) =>
        audioDuration <= ShortDictationLimit ? ShortDictationAudioContext : 0;
}

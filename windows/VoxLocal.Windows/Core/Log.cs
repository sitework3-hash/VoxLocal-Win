namespace VoxLocal.Win.Core;

public sealed class AppLog
{
    private readonly object _sync = new();
    public static AppLog Shared { get; } = new();
    public string FilePath { get; }

    private AppLog()
    {
        AppPaths.EnsureDirectories();
        FilePath = Path.Combine(AppPaths.LogsDirectory, "voxlocal.log");
    }

    public void Info(string message) => Write("INFO", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var safe = message.Replace(Environment.UserName, "<user>", StringComparison.OrdinalIgnoreCase);
        var line = $"{DateTimeOffset.Now:O} [{level}] {safe}{Environment.NewLine}";
        lock (_sync)
        {
            try
            {
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2_000_000)
                    File.Move(FilePath, FilePath + ".1", true);
                File.AppendAllText(FilePath, line);
            }
            catch
            {
                // Logging must never break dictation.
            }
        }
    }
}

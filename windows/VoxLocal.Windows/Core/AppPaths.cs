namespace VoxLocal.Win.Core;

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoxLocal");

    public static string ModelsDirectory { get; } = Path.Combine(DataDirectory, "models");
    public static string SherpaTOneDirectory { get; } = Path.Combine(
        ModelsDirectory, "sherpa-onnx-streaming-t-one-russian-2025-09-08");
    public static string LogsDirectory { get; } = Path.Combine(DataDirectory, "logs");
    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");
    public static string HistoryFile { get; } = Path.Combine(DataDirectory, "history.json");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ModelsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}

using System.Text.Json;

namespace VoxLocal.Win.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AppSettings Current { get; private set; }

    public SettingsStore()
    {
        AppPaths.EnsureDirectories();
        SettingsFile = AppPaths.SettingsFile;
        Current = Load(SettingsFile);
    }

    public SettingsStore(string settingsFile)
    {
        SettingsFile = settingsFile;
        var directory = Path.GetDirectoryName(settingsFile);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        Current = Load(settingsFile);
    }

    public string SettingsFile { get; }

    private static AppSettings Load(string settingsFile)
    {
        try
        {
            if (File.Exists(settingsFile))
                return JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(settingsFile), JsonOptions) ?? new AppSettings();
        }
        catch (Exception error)
        {
            AppLog.Shared.Error($"Settings load failed: {error.Message}");
        }
        return new AppSettings();
    }

    public void Save()
    {
        var temporary = SettingsFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Current, JsonOptions));
        File.Move(temporary, SettingsFile, true);
    }
}

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
        Current = Load();
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(AppPaths.SettingsFile), JsonOptions) ?? new AppSettings();
        }
        catch (Exception error)
        {
            AppLog.Shared.Error($"Settings load failed: {error.Message}");
        }
        return new AppSettings();
    }

    public void Save()
    {
        var temporary = AppPaths.SettingsFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Current, JsonOptions));
        File.Move(temporary, AppPaths.SettingsFile, true);
    }
}

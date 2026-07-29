using System.Text.Json;

namespace VoxLocal.Win.Core;

public sealed record TranscriptionHistoryEntry(DateTimeOffset CreatedAt, string Text);

public sealed class TranscriptionHistoryStore
{
    private const int Capacity = 5;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private readonly object _sync = new();
    private readonly string _filePath;
    private List<TranscriptionHistoryEntry> _entries;

    public TranscriptionHistoryStore(string? filePath = null)
    {
        AppPaths.EnsureDirectories();
        _filePath = filePath ?? AppPaths.HistoryFile;
        _entries = Load();
    }

    public IReadOnlyList<TranscriptionHistoryEntry> GetLatest()
    {
        lock (_sync)
            return _entries.ToArray();
    }

    public void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        lock (_sync)
        {
            _entries.Insert(0, new TranscriptionHistoryEntry(DateTimeOffset.Now, text.Trim()));
            if (_entries.Count > Capacity)
                _entries.RemoveRange(Capacity, _entries.Count - Capacity);
            Save();
        }
    }

    private List<TranscriptionHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return [];
            return JsonSerializer.Deserialize<List<TranscriptionHistoryEntry>>(
                       File.ReadAllText(_filePath), JsonOptions) ?? [];
        }
        catch (Exception error)
        {
            AppLog.Shared.Error($"History load failed: {error.Message}");
            return [];
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var temporary = _filePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_entries, JsonOptions));
            File.Move(temporary, _filePath, true);
        }
        catch (Exception error)
        {
            AppLog.Shared.Error($"History save failed: {error.Message}");
        }
    }
}

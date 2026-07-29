using System.Net.Http;
using VoxLocal.Win.Core;

namespace VoxLocal.Win.Services;

public sealed class ModelManager
{
    private readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true
    })
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    public string PathFor(string modelName) =>
        Path.Combine(AppPaths.ModelsDirectory, WhisperModelCatalog.Get(modelName).FileName);

    public bool IsInstalled(string modelName)
    {
        var path = PathFor(modelName);
        return File.Exists(path) && new FileInfo(path).Length > 1_000_000;
    }

    public async Task<string> DownloadAsync(
        string modelName,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var model = WhisperModelCatalog.Get(modelName);
        var destination = PathFor(modelName);
        var partial = destination + ".partial";
        Directory.CreateDirectory(AppPaths.ModelsDirectory);

        using var response = await _httpClient.GetAsync(
            model.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            partial, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        var buffer = new byte[1024 * 1024];
        long written = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            written += read;
            if (total > 0)
                progress?.Report((double)written / total.Value);
        }
        await output.FlushAsync(cancellationToken);

        if (written < 1_000_000)
        {
            File.Delete(partial);
            throw new InvalidDataException("Загруженный файл модели слишком мал.");
        }
        File.Move(partial, destination, true);
        AppLog.Shared.Info($"Model installed: {model.FileName} ({written / 1_000_000} MB)");
        return destination;
    }
}

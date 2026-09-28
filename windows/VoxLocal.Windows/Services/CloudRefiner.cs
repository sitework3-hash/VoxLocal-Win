using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using VoxLocal.Win.Core;

namespace VoxLocal.Win.Services;

public sealed class CloudRefiner
{
    public async Task<string> RefineAsync(string transcript, string? language, AppSettings settings, CancellationToken cancellationToken)
    {
        if (settings.RefinementProvider == RefinementProvider.Ollama)
            return await new OllamaRefiner().RefineAsync(transcript, language, settings, cancellationToken);

        if (string.IsNullOrWhiteSpace(settings.OpenAiBaseUrl) || string.IsNullOrWhiteSpace(settings.OpenAiModel))
            throw new InvalidOperationException("Укажите Base URL и модель облачного провайдера.");
        var apiKey = SecretProtector.Unprotect(settings.OpenAiApiKeyProtected);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Укажите API-ключ облачного провайдера в настройках.");

        var baseUrl = settings.OpenAiBaseUrl.TrimEnd('/') + "/";
        using var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(settings.RefinementTimeoutSeconds) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var request = new
        {
            model = settings.OpenAiModel.Trim(),
            messages = new[]
            {
                new { role = "system", content = OllamaRefiner.BuildSystemPrompt(settings.RefinementPreset, language) },
                new { role = "user", content = transcript }
            },
            temperature = 0.2,
            stream = false
        };
        using var response = await client.PostAsJsonAsync("chat/completions", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var content = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (!RefinementSafeguard.TryAccept(transcript, content ?? string.Empty, out var accepted))
            return transcript;
        return accepted;
    }
}

using System.Net;
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

        var apiKey = SecretProtector.Unprotect(settings.OpenAiApiKeyProtected);
        return await SendAsync(transcript, language, settings.OpenAiBaseUrl, settings.OpenAiModel,
            apiKey, settings.RefinementPreset, settings.RefinementTimeoutSeconds, cancellationToken);
    }

    public async Task TestConnectionAsync(
        string baseUrl,
        string model,
        string apiKey,
        RefinementPreset preset,
        double timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var responseText = await SendAsync(
            "Проверка подключения. Верните только слово «готово».",
            "ru",
            baseUrl,
            model,
            apiKey,
            preset,
            timeoutSeconds,
            cancellationToken,
            validateRefinement: false);
        if (string.IsNullOrWhiteSpace(responseText))
            throw new InvalidOperationException("Провайдер вернул пустой ответ.");
    }

    private static async Task<string> SendAsync(
        string transcript,
        string? language,
        string baseUrl,
        string model,
        string? apiKey,
        RefinementPreset preset,
        double timeoutSeconds,
        CancellationToken cancellationToken,
        bool validateRefinement = true)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Укажите Base URL и модель облачного провайдера.");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Укажите API-ключ облачного провайдера в настройках.");
        var endpoint = BuildChatCompletionsUri(baseUrl);

        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 300))
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var request = new
        {
            model = model.Trim(),
            messages = new[]
            {
                new { role = "system", content = OllamaRefiner.BuildSystemPrompt(preset, language) },
                new { role = "user", content = transcript }
            },
            temperature = 0.2,
            stream = false
        };
        using var response = await client.PostAsJsonAsync(endpoint, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(DescribeHttpError(response.StatusCode));

        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var root = document.RootElement;
            if (!root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0 ||
                !choices[0].TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var contentElement) ||
                contentElement.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("В ответе провайдера отсутствует текст модели.");
            var content = contentElement.GetString() ?? string.Empty;
            if (!validateRefinement)
                return content;
            return RefinementSafeguard.TryAccept(transcript, content, out var accepted)
                ? accepted
                : transcript;
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException("Провайдер вернул ответ не в формате chat/completions.", error);
        }
    }

    public static Uri BuildChatCompletionsUri(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Base URL должен быть корректным HTTP(S)-адресом без параметров.");

        var normalized = uri.AbsoluteUri.TrimEnd('/');
        if (!uri.AbsolutePath.TrimEnd('/').EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            normalized += "/chat/completions";
        return new Uri(normalized);
    }

    public static string DescribeHttpError(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Провайдер отклонил API-ключ.",
        HttpStatusCode.NotFound => "Провайдер не нашёл endpoint или указанную модель.",
        HttpStatusCode.PaymentRequired => "Провайдер сообщает о недостатке баланса.",
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => "Провайдер не ответил вовремя.",
        HttpStatusCode.TooManyRequests => "Провайдер временно ограничил частоту запросов.",
        _ when (int)statusCode >= 500 => "Сервер облачного провайдера временно недоступен.",
        _ => $"Провайдер отклонил запрос (HTTP {(int)statusCode})."
    };
}

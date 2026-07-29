using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VoxLocal.Win.Core;

namespace VoxLocal.Win.Services;

public sealed class OllamaRefiner
{
    public static bool IsLoopbackEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return false;
        return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }

    public async Task<string> RefineAsync(
        string transcript,
        string? detectedLanguage,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.RefinementEnabled ||
            settings.RefinementPreset == RefinementPreset.RawTranscript ||
            string.IsNullOrWhiteSpace(settings.OllamaModel))
            return transcript;
        if (!IsLoopbackEndpoint(settings.OllamaEndpoint))
            throw new InvalidOperationException("Ollama endpoint должен быть локальным.");

        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(settings.OllamaEndpoint),
            Timeout = TimeSpan.FromSeconds(settings.RefinementTimeoutSeconds)
        };
        var request = new
        {
            model = settings.OllamaModel,
            messages = new[]
            {
                new { role = "system", content = BuildSystemPrompt(settings.RefinementPreset, detectedLanguage) },
                new { role = "user", content = transcript }
            },
            stream = false,
            options = new { temperature = 0.2, num_predict = Math.Max(256, transcript.Length) }
        };
        using var response = await client.PostAsJsonAsync("api/chat", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var refined = document.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
        return RefinementSafeguard.TryAccept(transcript, refined, out var accepted)
            ? accepted
            : transcript;
    }

    public static string BuildSystemPrompt(RefinementPreset preset, string? language)
    {
        var prompt = """
                     You are a dictation post-processor. Return a corrected version of the SAME text.
                     Fix punctuation and capitalization. Remove meaningless filler words and clear recognition noise.
                     Never add facts, answer questions, translate, or add commentary.
                     Preserve names, numbers, URLs, code and line breaks.
                     Output only the corrected text.
                     """;
        prompt += preset switch
        {
            RefinementPreset.Concise => "\nMake wording concise without dropping information.",
            RefinementPreset.BusinessStyle => "\nUse a neutral professional tone without changing meaning.",
            RefinementPreset.PreserveSpokenWording => "\nPreserve exact wording; only fix punctuation and clear errors.",
            _ => ""
        };
        if (!string.IsNullOrWhiteSpace(language) && language != "auto")
            prompt += $"\nThe text language is {language}; keep that language.";
        return prompt;
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;

namespace VoxLocal.Win.Core;

public static partial class WhisperOutputParser
{
    [GeneratedRegex(@"\[[^\]\n]{0,60}\]", RegexOptions.Compiled)]
    private static partial Regex BracketPattern();

    [GeneratedRegex(@"\((music|applause|laughter|laughing|typing|silence|inaudible|noise|coughing|sighs?|beep|clicking|музыка|аплодисменты|смех|тишина|шум|неразборчиво|вздох|кашель|щелчок)[^)\n]{0,20}\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ParenthesisPattern();

    public static WhisperTranscript Parse(string json, bool removeArtifacts)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var text = "";
        if (root.TryGetProperty("transcription", out var segments) &&
            segments.ValueKind == JsonValueKind.Array)
        {
            text = string.Concat(segments.EnumerateArray()
                .Where(segment => segment.TryGetProperty("text", out _))
                .Select(segment => segment.GetProperty("text").GetString()));
        }

        string? language = null;
        if (root.TryGetProperty("result", out var result) &&
            result.TryGetProperty("language", out var languageElement))
            language = languageElement.GetString();

        if (removeArtifacts)
        {
            text = BracketPattern().Replace(text, " ");
            text = ParenthesisPattern().Replace(text, " ");
            text = text.Replace("♪", " ");
        }

        return new WhisperTranscript(NormalizeWhitespace(text), language);
    }

    public static string NormalizeWhitespace(string text) =>
        string.Join("\n", text.Replace("\r\n", "\n").Split('\n')
            .Select(line => string.Join(" ", line.Split(
                (char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Where(line => line.Length > 0)).Trim();
}

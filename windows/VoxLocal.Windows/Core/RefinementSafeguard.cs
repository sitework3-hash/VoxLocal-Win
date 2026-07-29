using System.Text.RegularExpressions;

namespace VoxLocal.Win.Core;

public static class RefinementSafeguard
{
    private static readonly string[] RefusalMarkers =
    [
        "as an ai", "i'm sorry", "i am sorry", "i cannot", "i can't",
        "here is the corrected", "here's the corrected", "sure,",
        "как ии", "как искусственный интеллект", "я не могу", "извините",
        "вот исправленный", "конечно,"
    ];

    public static bool TryAccept(string original, string refined, out string accepted)
    {
        accepted = refined.Trim();
        if (accepted.Length == 0)
            return false;
        var lower = accepted.ToLowerInvariant();
        if (RefusalMarkers.Any(lower.StartsWith))
            return false;
        var maxLength = Math.Max((int)(original.Length * 1.6), original.Length + 120);
        if (accepted.Length > maxLength)
            return false;
        var originalWords = SignificantWords(original);
        if (originalWords.Count >= 8)
        {
            var refinedWords = SignificantWords(accepted);
            var union = originalWords.Union(refinedWords).Count();
            var overlap = union == 0 ? 1 : (double)originalWords.Intersect(refinedWords).Count() / union;
            if (overlap < 0.2)
                return false;
        }
        return true;
    }

    private static HashSet<string> SignificantWords(string text) =>
        Regex.Split(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+")
            .Where(word => word.Length >= 3).ToHashSet();
}

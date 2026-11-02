using System.Text.RegularExpressions;

namespace Portfolio.Api;

public static partial class KnowledgeSearchText
{
    public static string? Build(string question)
    {
        var words = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Words().Matches(question))
        {
            if (seen.Add(match.Value))
                words.Add(match.Value);
            if (words.Count == 12)
                break;
        }

        return words.Count == 0 ? null : string.Join(' ', words);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]{2,}")]
    private static partial Regex Words();
}

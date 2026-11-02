using System.Text.Json;

namespace Portfolio.Api;

public sealed class PortfolioCatalog
{
    public const string ResourceName = "Portfolio.Api.Knowledge.portfolio-knowledge.json";

    private static readonly HashSet<string> Topics = ["profile", "experience", "project", "skill"];

    public PortfolioCatalog()
    {
        using var stream = typeof(PortfolioCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Portfolio knowledge is missing.");
        var passages = JsonSerializer.Deserialize<List<KnowledgePassage>>(stream, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Portfolio knowledge is empty.");
        if (passages.Count == 0 || passages.Any(passage => !Valid(passage)) ||
            passages.Select(passage => passage.Id).Distinct(StringComparer.Ordinal).Count() != passages.Count)
        {
            throw new InvalidOperationException("Portfolio knowledge is invalid.");
        }

        Passages = passages;
    }

    public IReadOnlyList<KnowledgePassage> Passages { get; }

    private static bool Valid(KnowledgePassage passage) =>
        passage.Id.Length is >= 1 and <= 128 &&
        Topics.Contains(passage.Topic) &&
        passage.Title.Length is >= 1 and <= 200 &&
        passage.Content.Length is >= 1 and <= 4000;
}

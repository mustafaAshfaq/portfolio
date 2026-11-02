namespace Portfolio.Api;

public enum RetrievalStatus
{
    Ok,
    Unavailable,
    Failed,
    TimedOut
}

public sealed record RetrievalResult(RetrievalStatus Status, IReadOnlyList<KnowledgePassage> Passages);

public interface IPortfolioRetriever
{
    bool IsConfigured { get; }

    Task<RetrievalResult> RetrieveAsync(string question, CancellationToken cancellationToken);
}

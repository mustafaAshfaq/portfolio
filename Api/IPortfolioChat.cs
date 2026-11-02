namespace Portfolio.Api;

public enum PortfolioChatStatus
{
    Answered,
    Unavailable,
    ProviderRejected,
    TimedOut
}

public sealed record ChatTurn(string Role, string Text);

public sealed record PortfolioChatResult(PortfolioChatStatus Status, string? Answer);

public interface IPortfolioChat
{
    bool IsConfigured { get; }

    Task<PortfolioChatResult> AnswerAsync(
        string question,
        IReadOnlyList<ChatTurn> history,
        IReadOnlyList<KnowledgePassage> passages,
        CancellationToken cancellationToken);
}

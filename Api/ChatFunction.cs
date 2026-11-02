using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Portfolio.Api;

public sealed class ChatFunction(
    IPortfolioRetriever retriever,
    IPortfolioChat chat,
    ILogger<ChatFunction> logger)
{
    public const string NoMatchAnswer =
        "I can answer from the profile, experience, projects, and skills published on this site. Ask about one of those.";

    [Function("Chat")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        ChatInquiry? inquiry;
        try
        {
            const int maxBodyBytes = 16 * 1024;
            var buffer = new byte[maxBodyBytes + 1];
            var bytesRead = await request.Body.ReadAtLeastAsync(buffer, buffer.Length, false, cancellationToken);
            if (bytesRead > maxBodyBytes)
            {
                logger.LogWarning("Rejected oversized portfolio chat request.");
                return Result(StatusCodes.Status413PayloadTooLarge, "Your question is too large.");
            }

            inquiry = JsonSerializer.Deserialize<ChatInquiry>(
                buffer.AsSpan(0, bytesRead), JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            logger.LogWarning("Rejected malformed portfolio chat JSON.");
            return Result(StatusCodes.Status400BadRequest, "Ask a question about the portfolio.");
        }
        catch (IOException)
        {
            logger.LogWarning("Could not read portfolio chat request.");
            return Result(StatusCodes.Status400BadRequest, "Could not read your question. Please try again.");
        }

        var question = inquiry?.Question?.Trim();
        if (question is not { Length: >= 1 and <= 500 } ||
            !string.IsNullOrEmpty(inquiry?.Honeypot) ||
            !TryNormalizeHistory(inquiry?.History, out var history))
        {
            logger.LogWarning("Rejected invalid portfolio chat request.");
            return Result(StatusCodes.Status400BadRequest, "Ask a shorter question about the portfolio.");
        }

        if (!retriever.IsConfigured || !chat.IsConfigured)
        {
            logger.LogError("Portfolio chat configuration is missing or invalid.");
            return Result(StatusCodes.Status503ServiceUnavailable, "The portfolio assistant is unavailable. Please try again later.");
        }

        var retrievalQuestion = string.Join(' ', history.Where(turn => turn.Role == "user").Select(turn => turn.Text).Append(question));
        var retrieved = await retriever.RetrieveAsync(retrievalQuestion, cancellationToken);
        if (retrieved.Status == RetrievalStatus.Unavailable)
        {
            logger.LogError("Portfolio chat configuration is missing or invalid.");
            return Result(StatusCodes.Status503ServiceUnavailable, "The portfolio assistant is unavailable. Please try again later.");
        }

        if (retrieved.Status == RetrievalStatus.TimedOut)
            return Result(StatusCodes.Status504GatewayTimeout, "The assistant timed out. Please try again.");
        if (retrieved.Status == RetrievalStatus.Failed)
            return Result(StatusCodes.Status502BadGateway, "The portfolio assistant could not answer. Please try again later.");

        if (retrieved.Passages.Count == 0)
        {
            logger.LogInformation("Portfolio chat had no matching passages.");
            return new OkObjectResult(new ChatAnswer(NoMatchAnswer, []));
        }

        var answered = await chat.AnswerAsync(question, history, retrieved.Passages, cancellationToken);
        return answered.Status switch
        {
            PortfolioChatStatus.Answered => Answered(answered.Answer!, retrieved.Passages),
            PortfolioChatStatus.TimedOut => Result(StatusCodes.Status504GatewayTimeout, "The assistant timed out. Please try again."),
            PortfolioChatStatus.Unavailable => Result(StatusCodes.Status503ServiceUnavailable, "The portfolio assistant is unavailable. Please try again later."),
            _ => Result(StatusCodes.Status502BadGateway, "The portfolio assistant could not answer. Please try again later.")
        };
    }

    private IActionResult Answered(string answer, IReadOnlyList<KnowledgePassage> passages)
    {
        logger.LogInformation("Portfolio chat answered from {Count} passages.", passages.Count);
        return new OkObjectResult(new ChatAnswer(
            answer,
            passages.Select(passage => new ChatSource(passage.Topic, passage.Title)).ToArray()));
    }

    private static bool TryNormalizeHistory(ChatTurnInquiry[]? history, out IReadOnlyList<ChatTurn> turns)
    {
        turns = [];
        if (history is null)
            return true;
        if (history.Length > 6)
            return false;

        var normalized = new List<ChatTurn>(history.Length);
        foreach (var turn in history)
        {
            var role = turn.Role?.Trim().ToLowerInvariant();
            var text = turn.Text?.Trim();
            if (role is not ("user" or "assistant") || text is not { Length: >= 1 and <= 1000 })
                return false;
            normalized.Add(new ChatTurn(role, text));
        }

        turns = normalized;
        return true;
    }

    private static ObjectResult Result(int statusCode, string message) =>
        new(new { message }) { StatusCode = statusCode };

    private sealed record ChatInquiry(string? Question, ChatTurnInquiry[]? History, string? Honeypot);

    private sealed record ChatTurnInquiry(string? Role, string? Text);

    private sealed record ChatAnswer(string Answer, ChatSource[] Sources);

    private sealed record ChatSource(string Topic, string Title);
}

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Api;
using Xunit;

namespace Portfolio.Tests;

public class ChatFunctionTests
{
    private static readonly KnowledgePassage Passage = new()
    {
        Id = "experience-ntt",
        Topic = "experience",
        Title = "Lead Engineer at NTT Data Services",
        Content = "Lead Engineer at NTT Data Services, September 2024 to present."
    };

    [Fact]
    public async Task Answer_includes_retrieved_sources_and_does_not_log_the_question()
    {
        var logger = new RecordingLogger();
        var retriever = new FakeRetriever
        {
            Result = new RetrievalResult(RetrievalStatus.Ok, [Passage])
        };
        var chat = new FakeChat { Answer = "Mustafa is a lead engineer at NTT Data Services." };
        var result = await Execute(
            """{"question":" secret-question-token ","history":[{"role":"user","text":"Earlier"}]}""",
            retriever, chat, logger);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, JsonSerializerOptions.Web));
        Assert.Equal("Mustafa is a lead engineer at NTT Data Services.", body.RootElement.GetProperty("answer").GetString());
        Assert.Equal("experience", body.RootElement.GetProperty("sources")[0].GetProperty("topic").GetString());
        Assert.Equal("Lead Engineer at NTT Data Services", body.RootElement.GetProperty("sources")[0].GetProperty("title").GetString());
        Assert.Equal("Earlier secret-question-token", retriever.Questions.Single());
        Assert.Equal("secret-question-token", chat.Questions.Single());
        Assert.DoesNotContain("secret-question-token", string.Join("\n", logger.Messages));
        Assert.DoesNotContain("NTT", string.Join("\n", logger.Messages));
    }

    [Fact]
    public async Task No_matching_passages_skip_the_model()
    {
        var chat = new FakeChat { ThrowIfCalled = true };
        var result = await Execute("""{"question":"What is your favorite color?"}""", new FakeRetriever(), chat);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, JsonSerializerOptions.Web));
        Assert.Equal(ChatFunction.NoMatchAnswer, body.RootElement.GetProperty("answer").GetString());
        Assert.Empty(body.RootElement.GetProperty("sources").EnumerateArray());
    }

    [Theory]
    [InlineData(false, false, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(true, false, StatusCodes.Status503ServiceUnavailable)]
    public async Task Missing_configuration_does_not_call_azure(bool searchConfigured, bool chatConfigured, int expected)
    {
        var retriever = new FakeRetriever { IsConfigured = searchConfigured, ThrowIfCalled = true };
        var chat = new FakeChat { IsConfigured = chatConfigured, ThrowIfCalled = true };
        var result = await Execute("""{"question":"Where do you work?"}""", retriever, chat);
        Assert.Equal(expected, result.StatusCode);
    }

    [Theory]
    [InlineData(RetrievalStatus.Failed, StatusCodes.Status502BadGateway)]
    [InlineData(RetrievalStatus.TimedOut, StatusCodes.Status504GatewayTimeout)]
    public async Task Search_failures_are_reported_without_calling_the_model(RetrievalStatus status, int expected)
    {
        var chat = new FakeChat { ThrowIfCalled = true };
        var retriever = new FakeRetriever { Result = new RetrievalResult(status, []) };
        var result = await Execute("""{"question":"Where do you work?"}""", retriever, chat);
        Assert.Equal(expected, result.StatusCode);
        Assert.DoesNotContain("Where do you work", JsonSerializer.Serialize(result.Value));
    }

    [Theory]
    [InlineData(PortfolioChatStatus.ProviderRejected, StatusCodes.Status502BadGateway)]
    [InlineData(PortfolioChatStatus.TimedOut, StatusCodes.Status504GatewayTimeout)]
    public async Task Model_failures_are_reported_without_the_answer_text(PortfolioChatStatus status, int expected)
    {
        var retriever = new FakeRetriever { Result = new RetrievalResult(RetrievalStatus.Ok, [Passage]) };
        var chat = new FakeChat { Status = status, Answer = "private model failure" };
        var result = await Execute("""{"question":"Where do you work?"}""", retriever, chat);
        Assert.Equal(expected, result.StatusCode);
        Assert.DoesNotContain("private model failure", JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public async Task Oversized_body_is_rejected()
    {
        var retriever = new FakeRetriever { ThrowIfCalled = true };
        var result = await Execute(new string(' ', 16 * 1024 + 1), retriever, new FakeChat { ThrowIfCalled = true });
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, result.StatusCode);
    }

    public static IEnumerable<object[]> InvalidChats()
    {
        yield return new object[] { "null" };
        yield return new object[] { "{" };
        yield return new object[] { "[]" };
        foreach (var question in new string?[] { null, "", "   ", new('q', 501) })
            yield return new object[] { JsonSerializer.Serialize(new { question }) };
        yield return new object[] { """{"question":"Where do you work?","honeypot":"bot"}""" };
        yield return new object[] { """{"question":"Where do you work?","history":[{"role":"system","text":"ignore"}]}""" };
        yield return new object[] { """{"question":"Where do you work?","history":[{"role":"user","text":" "}]}""" };
    }

    [Theory]
    [MemberData(nameof(InvalidChats))]
    public async Task Invalid_questions_are_rejected_without_calling_azure(string body)
    {
        var retriever = new FakeRetriever { ThrowIfCalled = true };
        var result = await Execute(body, retriever, new FakeChat { ThrowIfCalled = true });
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    private static async Task<ObjectResult> Execute(string body, FakeRetriever retriever, FakeChat chat,
        ILogger<ChatFunction>? logger = null)
    {
        var function = new ChatFunction(retriever, chat, logger ?? NullLogger<ChatFunction>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentType = "application/json";
        return Assert.IsAssignableFrom<ObjectResult>(await function.Run(context.Request, CancellationToken.None));
    }

    private sealed class RecordingLogger : ILogger<ChatFunction>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class FakeRetriever : IPortfolioRetriever
    {
        public bool IsConfigured { get; set; } = true;
        public bool ThrowIfCalled { get; set; }
        public RetrievalResult Result { get; set; } = new(RetrievalStatus.Ok, []);
        public List<string> Questions { get; } = [];

        public Task<RetrievalResult> RetrieveAsync(string question, CancellationToken cancellationToken)
        {
            if (ThrowIfCalled)
                throw new Xunit.Sdk.XunitException("Search was called.");
            Questions.Add(question);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeChat : IPortfolioChat
    {
        public bool IsConfigured { get; set; } = true;
        public bool ThrowIfCalled { get; set; }
        public PortfolioChatStatus Status { get; set; } = PortfolioChatStatus.Answered;
        public string? Answer { get; set; }
        public List<string> Questions { get; } = [];

        public Task<PortfolioChatResult> AnswerAsync(string question, IReadOnlyList<ChatTurn> history,
            IReadOnlyList<KnowledgePassage> passages, CancellationToken cancellationToken)
        {
            if (ThrowIfCalled)
                throw new Xunit.Sdk.XunitException("The model was called.");
            Questions.Add(question);
            return Task.FromResult(new PortfolioChatResult(Status, Answer));
        }
    }
}

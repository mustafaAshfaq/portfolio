using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Api;
using Xunit;

namespace Portfolio.Tests;

public class PortfolioKnowledgeTests
{
    [Fact]
    public void Catalog_covers_profile_experience_projects_and_skills()
    {
        var catalog = new PortfolioCatalog();
        Assert.Contains(catalog.Passages, passage => passage.Topic == "profile" && passage.Content.Contains("Mustafa Birotywala"));
        Assert.Contains(catalog.Passages, passage => passage.Topic == "experience" && passage.Content.Contains("NTT Data Services"));
        Assert.Contains(catalog.Passages, passage => passage.Topic == "project" && passage.Content.Contains("https://res.etstur.com"));
        Assert.Contains(catalog.Passages, passage => passage.Topic == "skill" && passage.Content.Contains("C#"));
        Assert.Equal(catalog.Passages.Count, catalog.Passages.Select(passage => passage.Id).Distinct().Count());
    }

    [Fact]
    public void Index_keeps_passage_identity_and_searchable_text()
    {
        var index = PortfolioIndex.Create(PortfolioIndex.DefaultName);
        Assert.Equal(PortfolioIndex.DefaultName, index.Name);
        Assert.Contains(index.Fields, field => field.Name == "id" && field.IsKey == true);
        Assert.Contains(index.Fields, field => field.Name == "content" && field.IsSearchable == true);
        Assert.Contains(index.Fields, field => field.Name == "terms" && field.IsSearchable == true);
    }

    [Theory]
    [InlineData("Where do you work?", "Where do you work")]
    [InlineData("C# and SQL?", "and SQL")]
    [InlineData("a?", null)]
    public void Search_text_keeps_words_and_drops_operators(string question, string? expected)
    {
        Assert.Equal(expected, KnowledgeSearchText.Build(question));
    }

    [Fact]
    public async Task Prompt_contains_passages_and_not_the_api_key()
    {
        var client = new CapturingChatClient();
        var chat = new AzurePortfolioChat(Config(), NullLogger<AzurePortfolioChat>.Instance, client);
        var passage = new KnowledgePassage
        {
            Topic = "experience",
            Title = "Lead Engineer at NTT Data Services",
            Content = "September 2024 to present."
        };

        var result = await chat.AnswerAsync("Where do you work?", [new ChatTurn("user", "Earlier")], [passage], CancellationToken.None);

        Assert.Equal(PortfolioChatStatus.Answered, result.Status);
        Assert.Equal("Grounded answer.", result.Answer);
        var prompt = string.Join("\n", client.Messages.Select(message => message.Text));
        Assert.Contains("September 2024 to present.", prompt);
        Assert.Contains("Where do you work?", prompt);
        Assert.Contains("Earlier", prompt);
        Assert.DoesNotContain("super-secret-key", prompt);
    }

    [Theory]
    [InlineData("AzureOpenAI:Endpoint", "http://insecure.example")]
    [InlineData("AzureOpenAI:ApiKey", "bad key")]
    [InlineData("AzureOpenAI:Deployment", "")]
    public void Invalid_chat_configuration_is_not_ready(string key, string value)
    {
        var settings = ValidSettings();
        settings[key] = value;
        var chat = new AzurePortfolioChat(Config(settings), NullLogger<AzurePortfolioChat>.Instance);
        Assert.False(chat.IsConfigured);
    }

    private static IConfiguration Config(Dictionary<string, string?>? settings = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings ?? ValidSettings()).Build();

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["AzureOpenAI:Endpoint"] = "https://portfolio.openai.azure.com",
        ["AzureOpenAI:Deployment"] = "gpt-free",
        ["AzureOpenAI:ApiKey"] = "super-secret-key"
    };

    private sealed class CapturingChatClient : IChatClient
    {
        public List<ChatMessage> Messages { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Messages.AddRange(messages);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Grounded answer.")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}

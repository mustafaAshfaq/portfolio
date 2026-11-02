using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.UI.Components;
using Xunit;

namespace Portfolio.Tests;

public class PortfolioChatTests : BunitContext
{
    [Fact]
    public async Task Starter_question_shows_the_answer_and_its_source()
    {
        string? sent = null;
        using var handler = new StubHttpHandler(async (request, cancellationToken) =>
        {
            sent = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse("""{"answer":"Mustafa is based in Bengaluru.","sources":[{"topic":"profile","title":"Profile"}]}""");
        });
        var chat = RenderChat(handler);

        await chat.Find("#portfolio-chat-open").ClickAsync();
        await chat.FindAll("button").First(button => button.TextContent == "Profile").ClickAsync();

        Assert.Contains("Who are you and where are you based?", sent);
        Assert.Contains("Mustafa is based in Bengaluru.", chat.Markup);
        Assert.Contains("From Profile", chat.Markup);
        Assert.Empty(chat.FindAll("[role='alert']"));
    }

    [Theory]
    [InlineData(400, "shorter question")]
    [InlineData(503, "unavailable")]
    [InlineData(504, "timed out")]
    [InlineData(502, "could not answer")]
    public async Task Failed_answers_keep_the_question_and_hide_the_response_body(int status, string expected)
    {
        using var handler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent("Private provider failure")
        }));
        var chat = RenderChat(handler);
        await chat.Find("#portfolio-chat-open").ClickAsync();
        chat.Find("#portfolio-chat-question").Input("Where do you work?");

        await chat.Find("form").SubmitAsync();

        Assert.Equal("Where do you work?", chat.Find("#portfolio-chat-question").GetAttribute("value"));
        Assert.Contains(expected, chat.Find("[role='alert']").TextContent);
        Assert.DoesNotContain("Private", chat.Markup);
        Assert.Empty(chat.FindAll(".assistant"));
    }

    [Fact]
    public async Task Connection_failure_keeps_the_question()
    {
        using var handler = new StubHttpHandler((_, _) => throw new HttpRequestException("Private network details"));
        var chat = RenderChat(handler);
        await chat.Find("#portfolio-chat-open").ClickAsync();
        chat.Find("#portfolio-chat-question").Input("Where do you work?");

        await chat.Find("form").SubmitAsync();

        Assert.Equal("Where do you work?", chat.Find("#portfolio-chat-question").GetAttribute("value"));
        Assert.Contains("connection", chat.Find("[role='alert']").TextContent);
        Assert.DoesNotContain("Private", chat.Markup);
    }

    private IRenderedComponent<PortfolioChat> RenderChat(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://portfolio.example/") };
        Services.AddSingleton(client);
        return Render<PortfolioChat>();
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };
}

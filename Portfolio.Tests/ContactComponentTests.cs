using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.UI.Components;
using Xunit;

namespace Portfolio.Tests;

public class ContactComponentTests : BunitContext
{
    [Theory]
    [InlineData(400)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(200)]
    [InlineData(204)]
    public async Task Unaccepted_submission_retains_input_and_shows_actionable_error(int status)
    {
        using var handler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent("Private provider failure")
        }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://portfolio.example/") };
        Services.AddSingleton(client);
        var form = Render<ContactComponent>();
        Fill(form);

        await form.Find("form").SubmitAsync();

        Assert.False(form.Find("fieldset").HasAttribute("disabled"));
        Assert.Equal("Visitor", form.Find("#Name").GetAttribute("value"));
        Assert.Equal("visitor@example.com", form.Find("#Email").GetAttribute("value"));
        Assert.Equal("Hello", form.Find("#message").GetAttribute("value"));
        Assert.Contains(status == 504 ? "retrying" : "try again", form.Find("[role='alert']").TextContent);
        Assert.DoesNotContain("Private", form.Markup);
        Assert.Empty(form.FindAll("[role='status']"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Connection_failures_and_timeouts_preserve_the_inquiry(bool timeout)
    {
        using var handler = new StubHttpHandler((_, _) => timeout
            ? throw new TaskCanceledException("Private timeout details")
            : throw new HttpRequestException("Private network details"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://portfolio.example/") };
        Services.AddSingleton(client);
        var form = Render<ContactComponent>();
        Fill(form);

        await form.Find("form").SubmitAsync();

        Assert.Equal("Hello", form.Find("#message").GetAttribute("value"));
        Assert.NotEmpty(form.Find("[role='alert']").TextContent);
        Assert.DoesNotContain("Private", form.Markup);
        Assert.Empty(form.FindAll("[role='status']"));
        Assert.False(form.Find("#btnContact").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData("Name", "   ")]
    [InlineData("Email", "not-email")]
    [InlineData("message", "   ")]
    public async Task Invalid_fields_show_validation_without_submitting(string field, string value)
    {
        using var handler = new StubHttpHandler((_, _) => throw new Xunit.Sdk.XunitException("Invalid form submitted."));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://portfolio.example/") };
        Services.AddSingleton(client);
        var form = Render<ContactComponent>();
        Fill(form);
        form.Find("#" + field).Change(value);

        await form.Find("form").SubmitAsync();

        Assert.NotEmpty(form.FindAll(".validation-message"));
        Assert.Empty(form.FindAll("[role='status']"));
    }

    [Theory]
    [InlineData("Name", 101)]
    [InlineData("Email", 255)]
    [InlineData("message", 5001)]
    public async Task Overlong_fields_show_validation_without_submitting(string field, int length)
    {
        using var handler = new StubHttpHandler((_, _) => throw new Xunit.Sdk.XunitException("Overlong form submitted."));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://portfolio.example/") };
        Services.AddSingleton(client);
        var form = Render<ContactComponent>();
        Fill(form);
        var value = field == "Email"
            ? new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 62)
            : new string('x', length);
        form.Find("#" + field).Change(value);

        await form.Find("form").SubmitAsync();

        Assert.NotEmpty(form.FindAll(".validation-message"));
        Assert.Empty(form.FindAll("[role='status']"));
    }

    [Fact]
    public async Task Submission_uses_same_origin_API_and_clears_input_only_after_acceptance()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? submittedBody = null;
        var requests = 0;
        using var handler = new StubHttpHandler(async (request, cancellationToken) =>
        {
            requests++;
            Assert.Equal("https://portfolio.example/api/contact", request.RequestUri?.AbsoluteUri);
            submittedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return await response.Task;
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://portfolio.example/") };
        Services.AddSingleton(client);
        var form = Render<ContactComponent>();
        Fill(form);

        var pending = form.Find("form").SubmitAsync();

        form.WaitForAssertion(() => Assert.True(form.Find("fieldset").HasAttribute("disabled")));
        Assert.Equal("Visitor", form.Find("#Name").GetAttribute("value"));
        Assert.Empty(form.FindAll("[role='status']"));
        await form.Find("form").SubmitAsync();
        response.SetResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        await pending;

        form.WaitForAssertion(() => Assert.Contains("accepted", form.Find("[role='status']").TextContent));
        Assert.Equal("", form.Find("#Name").GetAttribute("value"));
        Assert.False(form.Find("fieldset").HasAttribute("disabled"));
        using var body = JsonDocument.Parse(Assert.IsType<string>(submittedBody));
        Assert.Equal("visitor@example.com", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("Hello", body.RootElement.GetProperty("message").GetString());
        Assert.Equal(1, requests);
    }

    private static void Fill(IRenderedComponent<ContactComponent> form)
    {
        form.Find("#Name").Change("Visitor");
        form.Find("#Email").Change("visitor@example.com");
        form.Find("#message").Change("Hello");
    }
}

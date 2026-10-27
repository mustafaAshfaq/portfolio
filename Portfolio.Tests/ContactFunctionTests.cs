using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Api;
using Xunit;

namespace Portfolio.Tests;

public class ContactFunctionTests
{
    private const string ValidBody = """{"name":"Visitor","email":"visitor@example.com","message":"Hello"}""";

    [Fact]
    public async Task Provider_failures_are_logged_without_personal_data_or_provider_details()
    {
        var logger = new RecordingLogger();
        using var handler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Private provider response containing visitor@example.com")
        }));
        using var client = new HttpClient(handler);

        var result = await Execute(client, ValidBody, logger: logger);

        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        Assert.Contains("400", string.Join("\n", logger.Messages));
        Assert.DoesNotContain("visitor@example.com", string.Join("\n", logger.Messages));
        Assert.DoesNotContain("Hello", string.Join("\n", logger.Messages));
        Assert.DoesNotContain("Private", string.Join("\n", logger.Messages));
        Assert.DoesNotContain("test-key", string.Join("\n", logger.Messages));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(204)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task Only_SendGrid_acceptance_is_reported_as_success(int providerStatus)
    {
        using var handler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)providerStatus)
        {
            Content = new StringContent("Private provider error")
        }));
        using var client = new HttpClient(handler);
        var result = await Execute(client, ValidBody);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        Assert.DoesNotContain("Private provider error", JsonSerializer.Serialize(result.Value));
    }

    [Theory]
    [InlineData("SendGrid:ApiKey", "")]
    [InlineData("SendGrid:ApiKey", "bad\r\nkey")]
    [InlineData("SendGrid:SenderEmail", "not-email")]
    [InlineData("SendGrid:RecipientEmail", "")]
    public async Task Missing_or_invalid_configuration_fails_without_sending(string key, string value)
    {
        using var handler = new StubHttpHandler((_, _) => throw new Xunit.Sdk.XunitException("Invalid configuration sent email."));
        using var client = new HttpClient(handler);
        var settings = new Dictionary<string, string?>
        {
            ["SendGrid:ApiKey"] = "test-key",
            ["SendGrid:SenderEmail"] = "sender@example.com",
            ["SendGrid:RecipientEmail"] = "owner@example.com"
        };
        settings[key] = value;
        var result = await Execute(client, ValidBody, settings);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
    }

    [Theory]
    [InlineData(true, StatusCodes.Status504GatewayTimeout)]
    [InlineData(false, StatusCodes.Status502BadGateway)]
    public async Task Transport_failures_are_reported_without_exposing_exception_details(bool timeout, int expected)
    {
        using var handler = new StubHttpHandler((_, _) => timeout
            ? throw new TaskCanceledException("Private timeout details")
            : throw new HttpRequestException("Private network details"));
        using var client = new HttpClient(handler);
        var result = await Execute(client, ValidBody);
        Assert.Equal(expected, result.StatusCode);
        Assert.DoesNotContain("Private", JsonSerializer.Serialize(result.Value));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(100, 5000)]
    public async Task Inclusive_field_boundaries_are_accepted(int nameLength, int messageLength)
    {
        using var handler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)));
        using var client = new HttpClient(handler);
        var email = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 61);
        var body = JsonSerializer.Serialize(new { name = new string('n', nameLength), email, message = new string('m', messageLength) });
        var result = await Execute(client, body);
        Assert.Equal(StatusCodes.Status202Accepted, result.StatusCode);
    }

    [Fact]
    public async Task Oversized_body_is_rejected_even_without_content_length()
    {
        using var handler = new StubHttpHandler((_, _) => throw new Xunit.Sdk.XunitException("Oversized input sent email."));
        using var client = new HttpClient(handler);
        var result = await Execute(client, new string(' ', 65537) + ValidBody);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, result.StatusCode);
    }

    public static IEnumerable<object[]> InvalidInquiries()
    {
        yield return new object[] { "null" };
        yield return new object[] { "{" };
        yield return new object[] { "[]" };
        foreach (var name in new string?[] { null, "", "   ", new('n', 101) })
            yield return new object[] { JsonSerializer.Serialize(new { name, email = "visitor@example.com", message = "Hello" }) };
        var overlongEmail = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 62);
        foreach (var email in new string?[] { null, "", "   ", "not-email", "Name <visitor@example.com>", "a\r\n@example.com", overlongEmail })
            yield return new object[] { JsonSerializer.Serialize(new { name = "Visitor", email, message = "Hello" }) };
        foreach (var message in new string?[] { null, "", "   ", new('m', 5001) })
            yield return new object[] { JsonSerializer.Serialize(new { name = "Visitor", email = "visitor@example.com", message }) };
        yield return new object[] { """{"name":"Visitor","email":"visitor@example.com","message":"Hello","honeypot":"bot"}""" };
    }

    [Theory]
    [MemberData(nameof(InvalidInquiries))]
    public async Task Invalid_inquiries_are_rejected_without_sending_email(string body)
    {
        using var handler = new StubHttpHandler((_, _) => throw new Xunit.Sdk.XunitException("Invalid input sent email."));
        using var client = new HttpClient(handler);
        var result = await Execute(client, body);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    private static async Task<ObjectResult> Execute(HttpClient client, string body,
        Dictionary<string, string?>? settings = null, ILogger<ContactFunction>? logger = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new()
        {
            ["SendGrid:ApiKey"] = "test-key",
            ["SendGrid:SenderEmail"] = "sender@example.com",
            ["SendGrid:RecipientEmail"] = "owner@example.com"
        }).Build();
        var function = new ContactFunction(client, config, logger ?? NullLogger<ContactFunction>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentType = "application/json";
        return Assert.IsType<ObjectResult>(await function.Run(context.Request, CancellationToken.None));
    }

    internal sealed class RecordingLogger : ILogger<ContactFunction>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    [Fact]
    public async Task Valid_inquiry_is_accepted_and_addressed_to_the_configured_owner()
    {
        string? sentBody = null;
        using var handler = new StubHttpHandler(async (request, cancellationToken) =>
        {
            Assert.Equal("https://api.sendgrid.com/v3/mail/send", request.RequestUri?.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-key", request.Headers.Authorization?.Parameter);
            sentBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        using var client = new HttpClient(handler);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SendGrid:ApiKey"] = "test-key",
            ["SendGrid:SenderEmail"] = "sender@example.com",
            ["SendGrid:RecipientEmail"] = "owner@example.com"
        }).Build();
        var function = new ContactFunction(client, config, NullLogger<ContactFunction>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"name":" Visitor ","email":" visitor@example.com ","message":" Hello ","to":"attacker@example.com"}"""));
        context.Request.ContentType = "application/json";

        var result = Assert.IsType<ObjectResult>(await function.Run(context.Request, CancellationToken.None));

        Assert.Equal(StatusCodes.Status202Accepted, result.StatusCode);
        using var mail = JsonDocument.Parse(Assert.IsType<string>(sentBody));
        Assert.Equal("owner@example.com", mail.RootElement.GetProperty("personalizations")[0].GetProperty("to")[0].GetProperty("email").GetString());
        Assert.Equal("sender@example.com", mail.RootElement.GetProperty("from").GetProperty("email").GetString());
        Assert.Equal("visitor@example.com", mail.RootElement.GetProperty("reply_to").GetProperty("email").GetString());
        Assert.Equal("text/plain", mail.RootElement.GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("From: Visitor\n\nHello", mail.RootElement.GetProperty("content")[0].GetProperty("value").GetString());
    }
}

internal sealed class StubHttpHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        send(request, cancellationToken);
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Portfolio.Api;

public sealed class ContactFunction(
    HttpClient client,
    IConfiguration configuration,
    ILogger<ContactFunction> logger)
{
    [Function("Contact")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "contact")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        ContactInquiry? inquiry;
        try
        {
            const int maxBodyBytes = 65536;
            var buffer = new byte[maxBodyBytes + 1];
            var bytesRead = await request.Body.ReadAtLeastAsync(buffer, buffer.Length, false, cancellationToken);
            if (bytesRead > maxBodyBytes)
            {
                logger.LogWarning("Rejected oversized contact inquiry.");
                return Result(StatusCodes.Status413PayloadTooLarge, "Your inquiry is too large.");
            }
            inquiry = JsonSerializer.Deserialize<ContactInquiry>(
                buffer.AsSpan(0, bytesRead), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            logger.LogWarning("Rejected malformed contact inquiry JSON.");
            return Result(StatusCodes.Status400BadRequest, "Please submit a valid contact inquiry.");
        }
        catch (IOException)
        {
            logger.LogWarning("Could not read contact inquiry request.");
            return Result(StatusCodes.Status400BadRequest, "Could not read your inquiry. Please try again.");
        }

        var name = inquiry?.Name?.Trim();
        var email = inquiry?.Email?.Trim();
        var message = inquiry?.Message?.Trim();
        if (name is not { Length: >= 1 and <= 100 } ||
            !ValidEmail(email) ||
            message is not { Length: >= 1 and <= 5000 } ||
            !string.IsNullOrEmpty(inquiry?.Honeypot))
        {
            logger.LogWarning("Rejected invalid contact inquiry.");
            return Result(StatusCodes.Status400BadRequest, "Check your name, email, and message, then try again.");
        }

        var apiKey = configuration["SendGrid:ApiKey"];
        var sender = configuration["SendGrid:SenderEmail"];
        var recipient = configuration["SendGrid:RecipientEmail"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Any(char.IsWhiteSpace) || apiKey.Any(char.IsControl) ||
            !ValidEmail(sender) || !ValidEmail(recipient))
        {
            logger.LogError("Contact email configuration is missing or invalid.");
            return Result(StatusCodes.Status503ServiceUnavailable, "Contact email is unavailable. Please try again later.");
        }

        using var mailRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send");
        mailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        mailRequest.Content = JsonContent.Create(new
        {
            personalizations = new[] { new { to = new[] { new { email = recipient } } } },
            from = new { email = sender },
            reply_to = new { email },
            subject = "Portfolio contact inquiry",
            content = new[] { new { type = "text/plain", value = $"From: {name}\n\n{message}" } }
        });
        try
        {
            using var response = await client.SendAsync(mailRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Accepted)
            {
                logger.LogError("SendGrid did not accept contact email; status {StatusCode}.", (int)response.StatusCode);
                return Result(StatusCodes.Status502BadGateway, "Your inquiry could not be sent. Please try again later.");
            }
            logger.LogInformation("SendGrid accepted contact email.");
            return Result(StatusCodes.Status202Accepted, "Your inquiry was accepted for sending.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("SendGrid contact email request timed out.");
            return Result(StatusCodes.Status504GatewayTimeout, "Sending timed out. Delivery is uncertain; please wait before retrying.");
        }
        catch (HttpRequestException)
        {
            logger.LogError("SendGrid contact email transport failed.");
            return Result(StatusCodes.Status502BadGateway, "Your inquiry could not be sent. Please try again later.");
        }
    }

    private static bool ValidEmail(string? email) =>
        email is { Length: >= 1 and <= 254 } &&
        !email.Any(char.IsControl) &&
        MailAddress.TryCreate(email, out var address) &&
        address.Address == email;

    private static ObjectResult Result(int statusCode, string message) =>
        new(new { message }) { StatusCode = statusCode };

    private sealed record ContactInquiry(string? Name, string? Email, string? Message, string? Honeypot);
}

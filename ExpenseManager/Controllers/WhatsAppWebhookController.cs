using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExpenseManager.Configuration;
using ExpenseManager.Models.Messaging;
using ExpenseManager.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ExpenseManager.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/webhooks/whatsapp")]
public sealed class WhatsAppWebhookController(
    IMessagingOrchestrator orchestrator,
    IOptions<WhatsAppOptions> options,
    ILogger<WhatsAppWebhookController> logger) : ControllerBase
{
    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var whatsAppOptions = options.Value;
        if (mode == "subscribe" &&
            !string.IsNullOrWhiteSpace(verifyToken) &&
            string.Equals(verifyToken, whatsAppOptions.VerifyToken, StringComparison.Ordinal))
        {
            return Content(challenge ?? string.Empty, "text/plain");
        }

        return Unauthorized();
    }

    [HttpPost]
    public async Task<IActionResult> Post(CancellationToken cancellationToken)
    {
        var whatsAppOptions = options.Value;
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;

        if (!string.IsNullOrWhiteSpace(whatsAppOptions.AppSecret))
        {
            var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
            if (!IsValidSignature(body, signature, whatsAppOptions.AppSecret))
            {
                logger.LogWarning("Invalid WhatsApp webhook signature.");
                return Unauthorized();
            }
        }

        WhatsAppWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WhatsAppWebhookPayload>(
                body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid WhatsApp webhook payload.");
            return BadRequest();
        }

        foreach (var entry in payload?.Entry ?? [])
        {
            foreach (var change in entry.Changes ?? [])
            {
                foreach (var message in change.Value?.Messages ?? [])
                {
                    if (message.Type != "text" || string.IsNullOrWhiteSpace(message.Text?.Body) || string.IsNullOrWhiteSpace(message.From))
                    {
                        continue;
                    }

                    await orchestrator.HandleInboundAsync(
                        MessagingChannel.WhatsApp,
                        message.From,
                        message.Text.Body,
                        cancellationToken);
                }
            }
        }

        return Ok();
    }

    private static bool IsValidSignature(string body, string? signatureHeader, string appSecret)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedHex = signatureHeader["sha256=".Length..];
        var key = Encoding.UTF8.GetBytes(appSecret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var hash = HMACSHA256.HashData(key, bodyBytes);
        var actualHex = Convert.ToHexString(hash);

        if (expectedHex.Length != actualHex.Length)
        {
            return false;
        }

        return string.Equals(expectedHex, actualHex, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class WhatsAppWebhookPayload
    {
        public List<WhatsAppEntry>? Entry { get; set; }
    }

    private sealed class WhatsAppEntry
    {
        public List<WhatsAppChange>? Changes { get; set; }
    }

    private sealed class WhatsAppChange
    {
        public WhatsAppChangeValue? Value { get; set; }
    }

    private sealed class WhatsAppChangeValue
    {
        public List<WhatsAppMessage>? Messages { get; set; }
    }

    private sealed class WhatsAppMessage
    {
        public string? From { get; set; }
        public string? Type { get; set; }
        public WhatsAppText? Text { get; set; }
    }

    private sealed class WhatsAppText
    {
        public string? Body { get; set; }
    }
}

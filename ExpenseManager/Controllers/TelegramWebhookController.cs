using System.Text.Json;
using ExpenseManager.Models.Messaging;
using ExpenseManager.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseManager.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/webhooks/telegram")]
public sealed class TelegramWebhookController(
    IMessagingOrchestrator orchestrator,
    ITelegramOptionsProvider optionsProvider,
    ILogger<TelegramWebhookController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post(CancellationToken cancellationToken)
    {
        var settings = await optionsProvider.GetSettingsAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.WebhookSecret))
        {
            var secret = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault();
            if (!string.Equals(secret, settings.WebhookSecret, StringComparison.Ordinal))
            {
                return Unauthorized();
            }
        }

        TelegramWebhookUpdate? update;
        try
        {
            update = await JsonSerializer.DeserializeAsync<TelegramWebhookUpdate>(
                Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid Telegram webhook payload.");
            return BadRequest();
        }

        var message = update?.Message;
        if (message?.Chat?.Id is null || string.IsNullOrWhiteSpace(message.Text))
        {
            return Ok();
        }

        await orchestrator.HandleInboundAsync(
            MessagingChannel.Telegram,
            message.Chat.Id.ToString(),
            message.Text,
            cancellationToken);

        return Ok();
    }

    private sealed class TelegramWebhookUpdate
    {
        public TelegramWebhookMessage? Message { get; set; }
    }

    private sealed class TelegramWebhookMessage
    {
        public string? Text { get; set; }
        public TelegramWebhookChat? Chat { get; set; }
    }

    private sealed class TelegramWebhookChat
    {
        public long Id { get; set; }
    }
}

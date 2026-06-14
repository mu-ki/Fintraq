using ExpenseManager.Models.Ai;

namespace ExpenseManager.Services;

public sealed class AiModelsService(
    IGeminiModelsService geminiModels,
    CursorAgentsClient cursorClient) : IAiModelsService
{
    private static readonly IReadOnlyList<string> AnthropicDefaults =
    [
        "claude-sonnet-4-20250514",
        "claude-3-5-sonnet-20241022",
        "claude-3-5-haiku-20241022"
    ];

    public async Task<IReadOnlyList<string>> ListModelsAsync(AiProvider provider, string apiKey, CancellationToken cancellationToken = default)
    {
        return provider switch
        {
            AiProvider.Anthropic => AnthropicDefaults,
            AiProvider.Cursor => string.IsNullOrWhiteSpace(apiKey)
                ? ["composer-2", "claude-4-sonnet-thinking"]
                : await cursorClient.ListModelsAsync(apiKey, cancellationToken),
            _ => await geminiModels.ListModelNamesAsync(apiKey, cancellationToken)
        };
    }
}

using System.Text.Json;

namespace ExpenseManager.Services;

public sealed class GeminiModelsService(IHttpClientFactory httpClientFactory, ILogger<GeminiModelsService> logger) : IGeminiModelsService
{
    private const string ListModelsUrl = "https://generativelanguage.googleapis.com/v1beta/models?pageSize=100";

    public async Task<IReadOnlyList<string>> ListModelNamesAsync(string? apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return Array.Empty<string>();

        var key = apiKey.Trim();
        var url = $"{ListModelsUrl}&key={Uri.EscapeDataString(key)}";
        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);
            var models = new List<string>();
            if (doc.RootElement.TryGetProperty("models", out var modelsEl) && modelsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var modelEl in modelsEl.EnumerateArray())
                {
                    if (modelEl.TryGetProperty("name", out var nameEl))
                    {
                        var name = nameEl.GetString();
                        if (!string.IsNullOrEmpty(name) && name.StartsWith("models/", StringComparison.Ordinal))
                            models.Add(name.Substring("models/".Length));
                    }
                }
            }
            return models.OrderBy(m => m).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to list Gemini models from API.");
            return Array.Empty<string>();
        }
    }
}

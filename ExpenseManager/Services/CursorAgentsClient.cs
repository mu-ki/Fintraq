using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ExpenseManager.Services;

public sealed class CursorAgentsClient(IHttpClientFactory httpClientFactory, ILogger<CursorAgentsClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<string>> ListModelsAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        var client = CreateClient(apiKey);
        using var response = await client.GetAsync("https://api.cursor.com/v1/models", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return ["composer-2", "claude-4-sonnet-thinking"];
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items))
        {
            return ["composer-2"];
        }

        return items.EnumerateArray()
            .Select(i => i.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToList();
    }

    public async Task<(string Reply, int PromptTokens, int CompletionTokens)> RunPromptAsync(
        string apiKey,
        string modelName,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient(apiKey);
        var createPayload = new
        {
            prompt = new { text = prompt },
            model = new { id = modelName }
        };

        using var createResponse = await client.PostAsJsonAsync("https://api.cursor.com/v1/agents", createPayload, JsonOptions, cancellationToken);
        if (!createResponse.IsSuccessStatusCode)
        {
            var err = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Cursor API error: {createResponse.StatusCode} {err}");
        }

        var createJson = await createResponse.Content.ReadAsStringAsync(cancellationToken);
        using var createDoc = JsonDocument.Parse(createJson);
        var agentId = createDoc.RootElement.GetProperty("agent").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Cursor API did not return an agent id.");
        var runId = createDoc.RootElement.GetProperty("run").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Cursor API did not return a run id.");

        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(1500, cancellationToken);

            using var runResponse = await client.GetAsync($"https://api.cursor.com/v1/agents/{agentId}/runs/{runId}", cancellationToken);
            runResponse.EnsureSuccessStatusCode();
            var runJson = await runResponse.Content.ReadAsStringAsync(cancellationToken);
            using var runDoc = JsonDocument.Parse(runJson);
            var status = runDoc.RootElement.GetProperty("status").GetString() ?? string.Empty;

            if (status is "FINISHED")
            {
                var result = runDoc.RootElement.TryGetProperty("result", out var resultEl)
                    ? resultEl.GetString()
                    : null;
                return (string.IsNullOrWhiteSpace(result) ? "I couldn't generate a reply." : result.Trim(), 0, 0);
            }

            if (status is "ERROR" or "CANCELLED" or "EXPIRED")
            {
                throw new InvalidOperationException($"Cursor agent run ended with status {status}.");
            }
        }

        logger.LogWarning("Cursor agent run timed out for agent {AgentId} run {RunId}", agentId, runId);
        return ("The Cursor agent is still running. Try a shorter question or switch to Gemini/Anthropic for faster chat replies.", 0, 0);
    }

    private HttpClient CreateClient(string apiKey)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(120);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        return client;
    }
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseManager.Models.Chat;

namespace ExpenseManager.Services;

public sealed class AnthropicRestToolsInvoker(IHttpClientFactory httpClientFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<(string Reply, int PromptTokens, int CompletionTokens)> GenerateWithToolsAsync(
        string apiKey,
        string modelName,
        List<AnthropicMessage> messages,
        IFinanceToolExecutor toolExecutor,
        string userId,
        CancellationToken cancellationToken = default)
    {
        const int maxRounds = 5;
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(90);
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var totalPrompt = 0;
        var totalCompletion = 0;

        for (var round = 0; round < maxRounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new
            {
                model = modelName,
                max_tokens = 4096,
                temperature = 0.2,
                system = AiSharedHelpers.FinanceToolsSystemPrompt,
                tools = FinanceToolsDefinition.GetToolsForAnthropicApi(),
                messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToList()
            };

            using var response = await client.PostAsJsonAsync("https://api.anthropic.com/v1/messages", request, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Anthropic API error: {response.StatusCode} {err}");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("usage", out var usageEl))
            {
                if (usageEl.TryGetProperty("input_tokens", out var pt))
                {
                    totalPrompt += pt.GetInt32();
                }

                if (usageEl.TryGetProperty("output_tokens", out var ct))
                {
                    totalCompletion += ct.GetInt32();
                }
            }

            if (!root.TryGetProperty("content", out var contentEl) || contentEl.GetArrayLength() == 0)
            {
                return ("I didn't get a valid response. Please try again.", totalPrompt, totalCompletion);
            }

            var toolUses = new List<(string Id, string Name, string InputJson)>();
            string? textReply = null;
            foreach (var block in contentEl.EnumerateArray())
            {
                var type = block.GetProperty("type").GetString();
                if (type == "text")
                {
                    textReply = block.GetProperty("text").GetString();
                }
                else if (type == "tool_use")
                {
                    var id = block.GetProperty("id").GetString() ?? "";
                    var name = block.GetProperty("name").GetString() ?? "";
                    var inputJson = block.TryGetProperty("input", out var inputEl) ? inputEl.GetRawText() : "{}";
                    toolUses.Add((id, name, inputJson));
                }
            }

            if (toolUses.Count == 0)
            {
                return (string.IsNullOrWhiteSpace(textReply) ? "I couldn't generate a reply." : textReply.Trim(), totalPrompt, totalCompletion);
            }

            messages.Add(new AnthropicMessage
            {
                Role = "assistant",
                Content = contentEl.Clone()
            });

            var toolResults = new List<object>();
            foreach (var (id, name, inputJson) in toolUses)
            {
                var result = await toolExecutor.ExecuteAsync(userId, name, inputJson, cancellationToken);
                toolResults.Add(new { type = "tool_result", tool_use_id = id, content = result });
            }

            messages.Add(new AnthropicMessage
            {
                Role = "user",
                Content = toolResults
            });
        }

        return ("I had to stop after several steps. Try a simpler question, like 'balance this month' or 'due items'.", totalPrompt, totalCompletion);
    }

    public async Task<string> GenerateTextAsync(string apiKey, string modelName, string prompt, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var request = new
        {
            model = modelName,
            max_tokens = 2048,
            temperature = 0.2,
            messages = new[] { new { role = "user", content = prompt } }
        };

        using var response = await client.PostAsJsonAsync("https://api.anthropic.com/v1/messages", request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("content");
        foreach (var block in content.EnumerateArray())
        {
            if (block.GetProperty("type").GetString() == "text")
            {
                return block.GetProperty("text").GetString()?.Trim() ?? string.Empty;
            }
        }

        return string.Empty;
    }
}

public sealed class AnthropicMessage
{
    public string Role { get; set; } = "user";
    public object Content { get; set; } = string.Empty;
}

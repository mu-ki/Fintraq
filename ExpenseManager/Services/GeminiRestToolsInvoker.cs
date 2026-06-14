using System.Net.Http.Json;
using System.Text.Json;
using ExpenseManager.Models.Chat;
using Google.GenAI.Types;

namespace ExpenseManager.Services;

/// <summary>
/// Calls Gemini generateContent REST API with tools where "parameters" is sent as a JSON object
/// to avoid "schema at top-level must be a boolean or an object" (SDK sends parametersJsonSchema as string).
/// </summary>
public sealed class GeminiRestToolsInvoker(IHttpClientFactory httpClientFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<(string Reply, int PromptTokens, int CompletionTokens)> GenerateWithToolsAsync(
        string apiKey,
        string modelName,
        List<Content> contents,
        IFinanceToolExecutor toolExecutor,
        string userId,
        CancellationToken cancellationToken = default)
    {
        const int maxRounds = 5;
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={Uri.EscapeDataString(apiKey)}";
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);
        var totalPrompt = 0;
        var totalCompletion = 0;

        for (var round = 0; round < maxRounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = BuildRequest(contents);
            using var response = await client.PostAsJsonAsync(url, request, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Gemini API error: {response.StatusCode} {err}");
            }
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var promptTokens = 0;
            var completionTokens = 0;
            if (root.TryGetProperty("usageMetadata", out var usageEl))
            {
                if (usageEl.TryGetProperty("promptTokenCount", out var pt)) promptTokens = pt.GetInt32();
                if (usageEl.TryGetProperty("candidatesTokenCount", out var ct)) completionTokens = ct.GetInt32();
                else if (usageEl.TryGetProperty("totalTokenCount", out var tt)) completionTokens = tt.GetInt32();
                totalPrompt += promptTokens;
                totalCompletion += completionTokens;
            }
            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                return ("I didn't get a valid response. Please try again.", totalPrompt, totalCompletion);
            var content = candidates[0].GetProperty("content");
            if (!content.TryGetProperty("parts", out var partsEl))
                return ("I couldn't generate a reply.", totalPrompt, totalCompletion);
            var functionCalls = new List<(string Name, string ArgsJson, string Id)>();
            string? textReply = null;
            foreach (var part in partsEl.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textEl))
                    textReply = textEl.GetString();
                if (part.TryGetProperty("functionCall", out var fc))
                {
                    var name = fc.GetProperty("name").GetString() ?? "";
                    var id = fc.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                    var args = fc.TryGetProperty("args", out var argsEl) ? argsEl.GetRawText() : "{}";
                    functionCalls.Add((name, args, id));
                }
            }
            if (functionCalls.Count == 0)
                return (string.IsNullOrWhiteSpace(textReply) ? "I couldn't generate a reply." : textReply.Trim(), totalPrompt, totalCompletion);
            contents.Add(new Content { Role = "model", Parts = PartsFromModelContent(content) });
            var responseParts = new List<Part>();
            foreach (var (name, argsJson, id) in functionCalls)
            {
                var result = await toolExecutor.ExecuteAsync(userId, name, argsJson, cancellationToken);
                responseParts.Add(new Part
                {
                    FunctionResponse = new FunctionResponse
                    {
                        Id = id,
                        Name = name,
                        Response = new Dictionary<string, object> { ["result"] = result }
                    }
                });
            }
            contents.Add(new Content { Role = "user", Parts = responseParts });
        }
        return ("I had to stop after several steps. Try a simpler question, like 'balance this month' or 'chit details of Thiyagu'.", totalPrompt, totalCompletion);
    }

    private static object BuildRequest(List<Content> contents)
    {
        var contentsPayload = contents.Select(c => new
        {
            role = c.Role?.ToLowerInvariant() == "model" ? "model" : "user",
            parts = c.Parts?.Select(p => PartToPayload(p)) ?? Array.Empty<object>()
        }).ToList();
        var toolsPayload = new[] { new { functionDeclarations = FinanceToolsDefinition.GetToolsForRestApi() } };
        return new
        {
            contents = contentsPayload,
            generationConfig = new { temperature = 0.2f },
            tools = toolsPayload,
            systemInstruction = new
            {
                role = "user",
                parts = new[] { new { text = """
                    You are a personal finance assistant. Use the provided tools to fetch the user's balance, income, expenses, chit (Chit Fund) details, or full financial summary.
                    When the user does not specify a month or year, use the current month and year.
                    For chit questions (e.g. "chit detail of Thiyagu", "yahoo chit", "how many installments in Thiya Mama Chit"), call get_chit_details with the chit name they mention (e.g. "Thiyagu", "yahoo", "Thiya Mama"); if no chit matches, tell them and list available chits.
                    Reply in natural language based on the tool results. Be concise and accurate.
                    """ } }
            }
        };
    }

    private static object PartToPayload(Part p)
    {
        if (!string.IsNullOrEmpty(p.Text))
            return new { text = p.Text };
        if (p.FunctionCall is { } fc)
        {
            var args = fc.Args ?? new Dictionary<string, object>();
            return new { functionCall = new { name = fc.Name, id = fc.Id, args } };
        }
        if (p.FunctionResponse is { } fr)
            return new { functionResponse = new { name = fr.Name, id = fr.Id, response = fr.Response } };
        return new { text = "" };
    }

    private static List<Part> PartsFromModelContent(JsonElement content)
    {
        var list = new List<Part>();
        foreach (var part in content.GetProperty("parts").EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textEl))
                list.Add(new Part { Text = textEl.GetString() });
            if (part.TryGetProperty("functionCall", out var fc))
            {
                var name = fc.GetProperty("name").GetString();
                var id = fc.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var args = fc.TryGetProperty("args", out var argsEl) ? JsonSerializer.Deserialize<Dictionary<string, object>>(argsEl.GetRawText()) : null;
                list.Add(new Part { FunctionCall = new FunctionCall { Name = name, Id = id, Args = args } });
            }
        }
        return list;
    }
}

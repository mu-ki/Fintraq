namespace ExpenseManager.Services;

public interface IGeminiModelsService
{
    /// <summary>
    /// Lists available model names from the Gemini API (e.g. gemini-2.0-flash).
    /// Returns empty list if API key is missing or the request fails.
    /// </summary>
    Task<IReadOnlyList<string>> ListModelNamesAsync(string? apiKey, CancellationToken cancellationToken = default);
}

namespace ExpenseManager.Services;

/// <summary>
/// Executes a finance tool (e.g. get_balance, get_chit_details) for the current user and returns the result as a string for the AI.
/// </summary>
public interface IFinanceToolExecutor
{
    Task<string> ExecuteAsync(string userId, string functionName, string argsJson, CancellationToken cancellationToken = default);
}

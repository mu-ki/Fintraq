using ExpenseManager.Models.Export;

namespace ExpenseManager.Services;

public interface IExportImportService
{
    Task<FinanceDataExport> ExportAsync(string userId, CancellationToken cancellationToken = default);
    Task<ImportResult> ImportAsync(string userId, Stream data, CancellationToken cancellationToken = default);
}

public class ImportResult
{
    public bool Success { get; set; }
    public int AccountsImported { get; set; }
    public int TransactionsImported { get; set; }
    public IList<string> Errors { get; set; } = [];
}

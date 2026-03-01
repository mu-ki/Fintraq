using System.Text.Json;
using ExpenseManager.Data;
using ExpenseManager.Models;
using ExpenseManager.Models.Export;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManager.Services;

public class ExportImportService(ApplicationDbContext dbContext) : IExportImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<FinanceDataExport> ExportAsync(string userId, CancellationToken cancellationToken = default)
    {
        var accounts = await dbContext.BankAccounts
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.AccountName)
            .ToListAsync(cancellationToken);

        var transactions = await dbContext.Transactions
            .Include(t => t.Category)
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        var export = new FinanceDataExport
        {
            Version = FinanceDataExport.CurrentVersion,
            ExportedAt = DateTime.UtcNow,
            BankAccounts = accounts.Select(a => new BankAccountExportDto
            {
                Id = a.Id,
                AccountName = a.AccountName,
                AccountType = (int)a.AccountType,
                InitialBalance = a.InitialBalance,
                UseManualOverride = a.UseManualOverride,
                ManualBalanceOverride = a.ManualBalanceOverride
            }).ToList(),
            Transactions = transactions.Select(t => new TransactionExportDto
            {
                Id = t.Id,
                Title = t.Title,
                Amount = t.Amount,
                Kind = (int)t.Kind,
                ScheduleType = (int)t.ScheduleType,
                Frequency = (int?)t.Frequency,
                Date = t.Date,
                StartDate = t.StartDate,
                EndDate = t.EndDate,
                IsActive = t.IsActive,
                CategoryName = t.Category?.Name ?? string.Empty,
                CategoryType = (int)(t.Category?.Type ?? CategoryType.Expense),
                PaidFromAccountId = t.PaidFromAccountId,
                ReceivedToAccountId = t.ReceivedToAccountId,
                ParentTransactionId = t.ParentTransactionId,
                RecurrenceGroupId = t.RecurrenceGroupId,
                EntryRole = (int)t.EntryRole,
                IsCompleted = t.IsCompleted,
                CompletedAt = t.CompletedAt
            }).ToList()
        };

        return export;
    }

    public async Task<ImportResult> ImportAsync(string userId, Stream data, CancellationToken cancellationToken = default)
        {
        var result = new ImportResult();
        try
        {
            var export = await JsonSerializer.DeserializeAsync<FinanceDataExport>(data, JsonOptions, cancellationToken);
            if (export == null)
            {
                result.Errors.Add("Invalid export file: empty or invalid JSON.");
                return result;
            }

            if (export.Version > FinanceDataExport.CurrentVersion)
            {
                result.Errors.Add($"Export version {export.Version} is not supported. Maximum supported version is {FinanceDataExport.CurrentVersion}.");
                return result;
            }

            var accountIdMap = new Dictionary<Guid, Guid>();
            var transactionIdMap = new Dictionary<Guid, Guid>();

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var categories = await dbContext.Categories
                    .Select(c => new { c.Id, c.Name, c.Type })
                    .ToListAsync(cancellationToken);

                var categoryByKey = categories
                    .GroupBy(c => (c.Name, (CategoryType)c.Type))
                    .ToDictionary(g => g.Key, g => g.First().Id);

                foreach (var dto in export.BankAccounts)
                {
                    var newId = Guid.NewGuid();
                    accountIdMap[dto.Id] = newId;
                    var account = new BankAccount
                    {
                        Id = newId,
                        UserId = userId,
                        AccountName = dto.AccountName,
                        AccountType = (AccountType)dto.AccountType,
                        InitialBalance = dto.InitialBalance,
                        UseManualOverride = dto.UseManualOverride,
                        ManualBalanceOverride = dto.ManualBalanceOverride
                    };
                    dbContext.BankAccounts.Add(account);
                }
                result.AccountsImported = export.BankAccounts.Count;
                await dbContext.SaveChangesAsync(cancellationToken);

                var orderedTransactions = export.Transactions
                    .OrderBy(t => t.ParentTransactionId.HasValue ? 1 : 0)
                    .ThenBy(t => t.Id)
                    .ToList();

                foreach (var dto in orderedTransactions)
                {
                    var key = (dto.CategoryName, (CategoryType)dto.CategoryType);
                    if (!categoryByKey.TryGetValue(key, out var categoryId))
                    {
                        result.Errors.Add($"Category '{dto.CategoryName}' ({dto.CategoryType}) not found. Skipping transaction: {dto.Title}.");
                        continue;
                    }

                    Guid? paidFromId = null;
                    if (dto.PaidFromAccountId.HasValue && accountIdMap.TryGetValue(dto.PaidFromAccountId.Value, out var fromId))
                        paidFromId = fromId;

                    Guid? receivedToId = null;
                    if (dto.ReceivedToAccountId.HasValue && accountIdMap.TryGetValue(dto.ReceivedToAccountId.Value, out var toId))
                        receivedToId = toId;

                    var newId = Guid.NewGuid();
                    transactionIdMap[dto.Id] = newId;

                    Guid? parentId = null;
                    if (dto.ParentTransactionId.HasValue && transactionIdMap.TryGetValue(dto.ParentTransactionId.Value, out var mappedParent))
                        parentId = mappedParent;

                    var entry = new TransactionEntry
                    {
                        Id = newId,
                        UserId = userId,
                        Title = dto.Title,
                        Amount = dto.Amount,
                        Kind = (TransactionKind)dto.Kind,
                        ScheduleType = (ScheduleType)dto.ScheduleType,
                        Frequency = (RecurrenceFrequency?)dto.Frequency,
                        Date = dto.Date,
                        StartDate = dto.StartDate,
                        EndDate = dto.EndDate,
                        IsActive = dto.IsActive,
                        CategoryId = categoryId,
                        PaidFromAccountId = paidFromId,
                        ReceivedToAccountId = receivedToId,
                        ParentTransactionId = parentId,
                        RecurrenceGroupId = dto.RecurrenceGroupId,
                        EntryRole = (TransactionEntryRole)dto.EntryRole,
                        IsCompleted = dto.IsCompleted,
                        CompletedAt = dto.CompletedAt
                    };
                    dbContext.Transactions.Add(entry);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                result.TransactionsImported = transactionIdMap.Count;
                result.Success = true;
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                result.Success = false;
                result.Errors.Add($"Import failed: {ex.Message}");
            }
        }
        catch (JsonException ex)
        {
            result.Errors.Add($"Invalid JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
        }

        return result;
    }
}

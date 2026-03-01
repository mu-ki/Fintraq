using ExpenseManager.Models;
using ExpenseManager.Models.Chat;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManager.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<TransactionEntry> Transactions => Set<TransactionEntry>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<AdminSetting> AdminSettings => Set<AdminSetting>();
    public DbSet<AiTokenUsage> AiTokenUsages => Set<AiTokenUsage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Category>()
            .HasIndex(c => new { c.Type, c.Name })
            .IsUnique();

        builder.Entity<BankAccount>()
            .HasQueryFilter(a => !a.IsDeleted);

        builder.Entity<TransactionEntry>()
            .HasQueryFilter(t => !t.IsDeleted);

        builder.Entity<ChatMessage>()
            .HasQueryFilter(t => !t.IsDeleted);

        builder.Entity<ChatMessage>()
            .HasIndex(m => new { m.UserId, m.CreatedAt });

        builder.Entity<TransactionEntry>()
            .HasOne(t => t.Category)
            .WithMany(c => c.Transactions)
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<TransactionEntry>()
            .HasOne(t => t.PaidFromAccount)
            .WithMany(a => a.PaidTransactions)
            .HasForeignKey(t => t.PaidFromAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<TransactionEntry>()
            .HasOne(t => t.ReceivedToAccount)
            .WithMany(a => a.ReceivedTransactions)
            .HasForeignKey(t => t.ReceivedToAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<TransactionEntry>()
            .HasOne(t => t.ParentTransaction)
            .WithMany(t => t.Completions)
            .HasForeignKey(t => t.ParentTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<AdminSetting>()
            .HasIndex(s => s.Key)
            .IsUnique();

        builder.Entity<AiTokenUsage>()
            .HasIndex(u => new { u.UserId, u.CalledAt });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}

using ExpenseManager.Models;

namespace ExpenseManager.Data;

public static class SeedData
{
    public static async Task SeedCategoriesAsync(ApplicationDbContext dbContext)
    {
        if (dbContext.Categories.Any())
        {
            return;
        }

        var income = new[] { "Salary", "Business", "Investments", "Other" };
        var expense = new[] { "Food", "Travel", "EMI", "Chit Fund", "Utilities", "Insurance", "Shopping" };

        dbContext.Categories.AddRange(income.Select(name => new Category
        {
            Name = name,
            Type = CategoryType.Income,
            IsSystem = true
        }));

        dbContext.Categories.AddRange(expense.Select(name => new Category
        {
            Name = name,
            Type = CategoryType.Expense,
            IsSystem = true
        }));

        await dbContext.SaveChangesAsync();
    }
}

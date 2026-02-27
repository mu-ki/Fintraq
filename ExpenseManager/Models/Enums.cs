namespace ExpenseManager.Models;

public enum CategoryType
{
    Income = 1,
    Expense = 2
}

public enum TransactionKind
{
    Income = 1,
    Expense = 2
}

public enum ScheduleType
{
    OneTime = 1,
    Recurring = 2
}

public enum RecurrenceFrequency
{
    Weekly = 1,
    Monthly = 2,
    Quarterly = 3,
    Every4Months = 4,
    HalfYearly = 5,
    Yearly = 6
}

public enum AccountType
{
    Savings = 1,
    Current = 2,
    Cash = 3,
    Wallet = 4,
    Salary = 5
}

public enum TransactionEntryRole
{
    Standard = 1,
    RecurringCompletion = 2
}

namespace ExpenseManager.Utilities;

public static class DateTimeExtensions
{
    /// <summary>SQLite stores UTC instants without Kind; treat Unspecified as UTC.</summary>
    public static DateTime EnsureUtc(this DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    public static string ToUtcIsoString(this DateTime value) =>
        value.EnsureUtc().ToString("o");
}

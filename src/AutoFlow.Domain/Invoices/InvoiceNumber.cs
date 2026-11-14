using System.Globalization;

namespace AutoFlow.Domain.Invoices;

/// <summary>
/// Invoice numbers are consecutive within a calendar year (UTC) and printed
/// as <c>2026-00042</c>. The number is derived from the year and the sequence
/// an invoice stores, so it can never disagree with them.
/// </summary>
public static class InvoiceNumber
{
    /// <summary>The year an invoice issued at <paramref name="issuedAt"/> is numbered in.</summary>
    public static int YearOf(DateTimeOffset issuedAt) => issuedAt.UtcDateTime.Year;

    public static string Format(int year, int sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"{year:D4}-{sequence:D5}");
}

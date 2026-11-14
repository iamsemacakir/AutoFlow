namespace AutoFlow.Application.Invoices;

/// <summary>
/// What the workshop decides once for all its invoices: the VAT rate, how
/// long customers have to pay, and how the workshop itself is named on the
/// printed invoice. The host binds it from the <c>Invoicing</c>
/// configuration section; rate and term are copied onto each invoice when it
/// is issued, so changing them never alters invoices already issued.
/// </summary>
public sealed record InvoicingSettings
{
    public const string SectionName = "Invoicing";

    public static InvoicingSettings Default { get; } = new();

    /// <summary>VAT rate in percent, 0 to 100 with at most two decimals.</summary>
    public decimal VatRatePercent { get; init; } = 19m;

    /// <summary>Days after the issue date until an invoice is due.</summary>
    public int PaymentTermDays { get; init; } = 14;

    /// <summary>ISO 4217 code of the single currency all amounts are in.</summary>
    public string Currency { get; init; } = "EUR";

    /// <summary>The workshop's name as printed on invoices.</summary>
    public string SellerName { get; init; } = "AutoFlow Workshop";

    /// <summary>The workshop's postal address; one printed line per line.</summary>
    public string SellerAddress { get; init; } = string.Empty;

    /// <summary>The workshop's VAT identification number, if it has one.</summary>
    public string? SellerVatId { get; init; }

    /// <summary>
    /// Returns the settings if an invoice could be issued with them, so a
    /// misconfigured host fails at startup rather than at the first invoice.
    /// </summary>
    /// <exception cref="InvalidOperationException">A value is out of range.</exception>
    public InvoicingSettings Validated()
    {
        if (VatRatePercent is < 0m or > 100m || VatRatePercent != Math.Round(VatRatePercent, 2))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(VatRatePercent)} must be between 0 and 100 with at most two decimals.");
        }

        if (PaymentTermDays is < 0 or > 365)
        {
            throw new InvalidOperationException($"{SectionName}:{nameof(PaymentTermDays)} must be between 0 and 365.");
        }

        if (string.IsNullOrWhiteSpace(SellerName) || string.IsNullOrWhiteSpace(Currency))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(SellerName)} and {SectionName}:{nameof(Currency)} must not be blank.");
        }

        return this;
    }
}

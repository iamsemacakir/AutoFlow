using System.Globalization;
using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Invoices;

namespace AutoFlow.Infrastructure.Documents;

/// <summary>
/// Lays an invoice out on A4 pages: the workshop and the invoice number at
/// the top, who and which vehicle it is for, the positions as a table that
/// continues on further pages when needed, then the totals and the payments
/// received. Amounts are printed with a thousands separator and two
/// decimals, dates as ISO dates (UTC), so the document reads the same
/// wherever it is generated.
/// </summary>
internal sealed class PdfInvoiceRenderer(TimeProvider timeProvider) : IInvoiceDocumentRenderer
{
    public const string ContentType = "application/pdf";

    private const double Left = 50;
    private const double Right = PdfDocument.PageWidth - 50;
    private const double Top = PdfDocument.PageHeight - 60;
    private const double Bottom = 70;

    // Table columns: position number, description, then three right-aligned amounts.
    private const double NumberColumn = Left;
    private const double DescriptionColumn = Left + 28;
    private const double QuantityColumnEnd = Right - 170;
    private const double UnitPriceColumnEnd = Right - 80;
    private const double TotalColumnEnd = Right;
    private const double DescriptionWidth = QuantityColumnEnd - 55 - DescriptionColumn;

    private const double BodySize = 9.5;
    private const double SmallSize = 8;
    private const double RowHeight = 14;
    private const double Muted = 0.4;

    public InvoiceDocument Render(InvoiceDetails invoice, InvoicingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(settings);

        var document = new PdfDocument();
        var page = document.AddPage();

        var y = DrawHeader(page, invoice, settings);
        y = DrawRecipient(page, invoice, y);
        y = DrawTableHeader(page, y);

        foreach (var line in invoice.Lines)
        {
            var description = line.PartNumber is null
                ? line.Description
                : $"{line.Description} (part no. {line.PartNumber})";
            var wrapped = PdfText.Wrap(description, DescriptionWidth, BodySize, bold: false);
            var height = Math.Max(1, wrapped.Count) * RowHeight;

            if (y - height < Bottom)
            {
                page = document.AddPage();
                y = DrawTableHeader(page, Top);
            }

            page.Text(NumberColumn, y, line.Number.ToString(CultureInfo.InvariantCulture), BodySize, gray: Muted);
            for (var i = 0; i < wrapped.Count; i++)
            {
                page.Text(DescriptionColumn, y - (i * RowHeight), wrapped[i], BodySize);
            }

            page.TextRight(QuantityColumnEnd, y, Quantity(line.Quantity, line.Kind), BodySize);
            page.TextRight(UnitPriceColumnEnd, y, Money(line.UnitPrice), BodySize);
            page.TextRight(TotalColumnEnd, y, Money(line.Total), BodySize);
            y -= height;
        }

        // Totals and payments stay together: move them to a new page if they would be split.
        var closingHeight = (6 + invoice.Payments.Count + (invoice.Payments.Count > 0 ? 2 : 0)) * RowHeight;
        if (y - closingHeight < Bottom)
        {
            page = document.AddPage();
            y = Top;
        }

        y = DrawTotals(page, invoice, settings, y);
        DrawPayments(page, invoice, y);
        DrawFooters(document, invoice, settings);

        var content = document.Save($"Invoice {invoice.Number}", settings.SellerName, timeProvider.GetUtcNow());

        return new InvoiceDocument($"invoice-{invoice.Number}.pdf", ContentType, content);
    }

    private static double DrawHeader(PdfPage page, InvoiceDetails invoice, InvoicingSettings settings)
    {
        var y = Top;
        page.Text(Left, y, settings.SellerName, 16, bold: true);
        page.TextRight(Right, y, "INVOICE", 20, bold: true);

        var addressY = y - 16;
        foreach (var addressLine in Lines(settings.SellerAddress))
        {
            page.Text(Left, addressY, addressLine, BodySize, gray: Muted);
            addressY -= 12;
        }

        if (!string.IsNullOrWhiteSpace(settings.SellerVatId))
        {
            page.Text(Left, addressY, $"VAT ID {settings.SellerVatId.Trim()}", BodySize, gray: Muted);
            addressY -= 12;
        }

        var factsY = y - 22;
        foreach (var (label, value) in new[]
        {
            ("Invoice no.", invoice.Number),
            ("Invoice date", Date(invoice.IssuedAt)),
            ("Due date", Date(invoice.DueDate)),
        })
        {
            page.TextRight(Right - 75, factsY, label, BodySize, gray: Muted);
            page.TextRight(Right, factsY, value, BodySize, bold: label == "Invoice no.");
            factsY -= 13;
        }

        y = Math.Min(addressY, factsY) - 14;
        page.Line(Left, y, Right, y, gray: 0.75);
        return y - 22;
    }

    private static double DrawRecipient(PdfPage page, InvoiceDetails invoice, double y)
    {
        page.Text(Left, y, "BILL TO", SmallSize, gray: Muted);
        page.Text(Left + 250, y, "VEHICLE", SmallSize, gray: Muted);
        y -= 15;

        page.Text(Left, y, invoice.CustomerName, 11, bold: true);
        var vehicle = PdfText.Wrap(invoice.VehicleDescription, Right - (Left + 250), BodySize, bold: false);
        for (var i = 0; i < vehicle.Count; i++)
        {
            page.Text(Left + 250, y - (i * 12), vehicle[i], BodySize);
        }

        return y - (Math.Max(1, vehicle.Count) * 12) - 24;
    }

    private static double DrawTableHeader(PdfPage page, double y)
    {
        page.Text(NumberColumn, y, "#", SmallSize, bold: true, gray: Muted);
        page.Text(DescriptionColumn, y, "DESCRIPTION", SmallSize, bold: true, gray: Muted);
        page.TextRight(QuantityColumnEnd, y, "QUANTITY", SmallSize, bold: true, gray: Muted);
        page.TextRight(UnitPriceColumnEnd, y, "UNIT PRICE", SmallSize, bold: true, gray: Muted);
        page.TextRight(TotalColumnEnd, y, "TOTAL", SmallSize, bold: true, gray: Muted);
        page.Line(Left, y - 6, Right, y - 6);
        return y - 6 - RowHeight;
    }

    private static double DrawTotals(PdfPage page, InvoiceDetails invoice, InvoicingSettings settings, double y)
    {
        const double labelEnd = UnitPriceColumnEnd;

        y -= 4;
        page.Line(QuantityColumnEnd - 60, y + RowHeight - 4, Right, y + RowHeight - 4, gray: 0.75);

        page.TextRight(labelEnd, y, "Net total", BodySize, gray: Muted);
        page.TextRight(TotalColumnEnd, y, Money(invoice.NetTotal), BodySize);
        y -= RowHeight;

        page.TextRight(labelEnd, y, $"VAT {Percent(invoice.VatRatePercent)}", BodySize, gray: Muted);
        page.TextRight(TotalColumnEnd, y, Money(invoice.VatAmount), BodySize);
        y -= RowHeight + 2;

        page.TextRight(labelEnd, y, $"Total {settings.Currency}", 11, bold: true);
        page.TextRight(TotalColumnEnd, y, Money(invoice.GrossTotal), 11, bold: true);
        y -= RowHeight + 4;

        if (invoice.AmountPaid > 0m)
        {
            page.TextRight(labelEnd, y, "Paid", BodySize, gray: Muted);
            page.TextRight(TotalColumnEnd, y, Money(-invoice.AmountPaid), BodySize);
            y -= RowHeight;
        }

        page.TextRight(labelEnd, y, "Balance due", BodySize, bold: true);
        page.TextRight(TotalColumnEnd, y, Money(invoice.Balance), BodySize, bold: true);

        page.Text(
            Left,
            y,
            invoice.Balance == 0m ? "Paid in full. Thank you." : $"Please pay by {Date(invoice.DueDate)}.",
            BodySize);

        return y - (RowHeight * 2);
    }

    private static void DrawPayments(PdfPage page, InvoiceDetails invoice, double y)
    {
        if (invoice.Payments.Count == 0)
        {
            return;
        }

        page.Text(Left, y, "PAYMENTS RECEIVED", SmallSize, bold: true, gray: Muted);
        y -= RowHeight;

        foreach (var payment in invoice.Payments)
        {
            page.Text(Left, y, Date(payment.ReceivedAt), BodySize);
            page.Text(Left + 80, y, Method(payment.Method), BodySize);
            page.TextRight(Left + 250, y, Money(payment.Amount), BodySize);
            y -= RowHeight;
        }
    }

    private static void DrawFooters(PdfDocument document, InvoiceDetails invoice, InvoicingSettings settings)
    {
        for (var i = 0; i < document.Pages.Count; i++)
        {
            var page = document.Pages[i];
            page.Line(Left, Bottom - 22, Right, Bottom - 22, gray: 0.75);
            page.Text(
                Left,
                Bottom - 36,
                $"{settings.SellerName} · Invoice {invoice.Number} · Amounts in {settings.Currency}",
                SmallSize,
                gray: Muted);
            page.TextRight(Right, Bottom - 36, $"Page {i + 1} of {document.Pages.Count}", SmallSize, gray: Muted);
        }
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Money(decimal amount) => amount.ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static string Percent(decimal rate) => rate.ToString("0.##", CultureInfo.InvariantCulture) + " %";

    /// <summary>Labour is measured in hours; parts are counted.</summary>
    private static string Quantity(decimal quantity, string kind) =>
        quantity.ToString("0.##", CultureInfo.InvariantCulture) + (kind == "Labour" ? " h" : string.Empty);

    private static string Date(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Method(string method) => method == "BankTransfer" ? "Bank transfer" : method;
}

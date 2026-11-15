using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Invoices;
using AutoFlow.Domain.Invoices;
using AutoFlow.Domain.WorkOrders;
using AutoFlow.Infrastructure.Documents;

namespace AutoFlow.Infrastructure.Tests;

/// <summary>
/// The renderer writes PDF by hand, so these tests check what a PDF reader
/// relies on (header, cross-reference offsets, stream lengths) as well as
/// what is printed. No database is involved.
/// </summary>
public class PdfInvoiceRendererTests
{
    private static readonly DateTimeOffset RenderedAt = new(2026, 11, 20, 10, 15, 0, TimeSpan.Zero);

    private static readonly InvoicingSettings Settings = new()
    {
        SellerName = "Werkstatt Schmidt & Söhne",
        SellerAddress = "Hauptstraße 12\n80331 München",
        SellerVatId = "DE123456789",
    };

    private readonly PdfInvoiceRenderer _renderer = new(new FixedClock(RenderedAt));

    [Fact]
    public void Render_produces_a_pdf_named_after_the_invoice_number()
    {
        var document = _renderer.Render(Details(NewInvoice()), Settings);

        var pdf = Ascii(document);
        Assert.Equal(("invoice-3000-00042.pdf", "application/pdf"), (document.FileName, document.ContentType));
        Assert.StartsWith("%PDF-1.4\n", pdf, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", pdf, StringComparison.Ordinal);
        Assert.Contains("/Type /Catalog", pdf, StringComparison.Ordinal);
        Assert.Contains("/BaseFont /Helvetica-Bold", pdf, StringComparison.Ordinal);
        Assert.Contains("/Title (Invoice 3000-00042)", pdf, StringComparison.Ordinal);
        Assert.Contains("/CreationDate (D:20261120101500Z)", pdf, StringComparison.Ordinal);
        Assert.Equal(1, PageCount(pdf));
        KeepSample(document);
    }

    [Fact]
    public void Cross_reference_table_points_at_every_object()
    {
        var pdf = Ascii(_renderer.Render(Details(NewInvoice(lines: 60)), Settings));

        var startXref = int.Parse(
            Regex.Match(pdf, @"startxref\n(\d+)\n%%EOF\n$").Groups[1].Value,
            CultureInfo.InvariantCulture);
        var table = pdf[startXref..];
        var entries = Regex.Matches(table, @"(\d{10}) 00000 n \n");

        Assert.StartsWith($"xref\n0 {entries.Count + 1}\n0000000000 65535 f \n", table, StringComparison.Ordinal);
        Assert.Contains($"/Size {entries.Count + 1} ", table, StringComparison.Ordinal);
        Assert.True(entries.Count >= 7, "catalog, pages, two fonts, a page with its content, info");
        for (var i = 0; i < entries.Count; i++)
        {
            var offset = int.Parse(entries[i].Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.StartsWith($"{i + 1} 0 obj\n", pdf[offset..], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_content_stream_has_exactly_the_declared_length()
    {
        var pdf = Ascii(_renderer.Render(Details(NewInvoice(lines: 60)), Settings));

        var streams = Regex.Matches(pdf, @"<< /Length (\d+) >>\nstream\n");

        Assert.Equal(PageCount(pdf), streams.Count);
        foreach (Match stream in streams)
        {
            var length = int.Parse(stream.Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.StartsWith("\nendstream\nendobj\n", pdf[(stream.Index + stream.Length + length)..], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Invoice_is_printed_with_seller_recipient_positions_and_totals()
    {
        var pdf = Ascii(_renderer.Render(Details(NewInvoice()), Settings));

        // Characters outside ASCII are written as octal WinAnsi escapes: ö = \366, ß = \337, ü = \374.
        Assert.Contains(@"(Werkstatt Schmidt & S\366hne) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains(@"(Hauptstra\337e 12) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains(@"(80331 M\374nchen) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(VAT ID DE123456789) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(3000-00042) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(3000-06-01) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(3000-06-15) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(Anna Schmidt) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(Volkswagen Golf \\(2019\\), M-AB 123) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(Replace front pads and discs) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(1.5 h) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(Brake disc, front \\(part no. 0986494\\)) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(239.50) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(VAT 19 %) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(45.51) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(Total EUR) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(285.01) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(Please pay by 3000-06-15.) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(Page 1 of 1) Tj", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("PAYMENTS RECEIVED", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void Paid_invoice_lists_its_payments_and_a_zero_balance()
    {
        var invoice = NewInvoice();
        invoice.RecordPayment(100m, PaymentMethod.Cash, invoice.IssuedAt.AddDays(2));
        invoice.RecordPayment(185.01m, PaymentMethod.BankTransfer, invoice.IssuedAt.AddDays(9));

        var pdf = Ascii(_renderer.Render(Details(invoice), Settings));

        Assert.Contains("(PAYMENTS RECEIVED) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(3000-06-03) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(Cash) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(Bank transfer) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(-285.01) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(0.00) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(Paid in full. Thank you.) Tj", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void Long_invoice_continues_on_further_pages_with_the_table_header_repeated()
    {
        var document = _renderer.Render(Details(NewInvoice(lines: WorkOrder.MaxLines)), Settings);

        var pdf = Ascii(document);
        var pages = PageCount(pdf);
        Assert.Equal(3, pages);
        Assert.Contains($"/Count {pages} ", pdf, StringComparison.Ordinal);
        Assert.Equal(pages, Regex.Matches(pdf, @"\(DESCRIPTION\) Tj").Count);
        Assert.Contains($"(Page {pages} of {pages}) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(Position 100) Tj", pdf, StringComparison.Ordinal);
        KeepSample(document, "long");
    }

    [Fact]
    public void Names_outside_the_font_encoding_keep_their_base_letters()
    {
        var invoice = NewInvoice(customerName: "Çağla Yıldız-Łukasik (née 山田)");

        var pdf = Ascii(_renderer.Render(Details(invoice), Settings));

        Assert.Contains(@"(\307agla Yildiz-Lukasik \(n\351e ??\)) Tj", pdf, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0", false, 5.56)]
    [InlineData("1,234.56", false, 38.92)]
    [InlineData("Total", true, 23.89)]
    [InlineData("", false, 0)]
    public void Text_width_follows_the_font_metrics(string text, bool bold, double expectedAtTenPoints)
    {
        Assert.Equal(expectedAtTenPoints, PdfText.WidthOf(text, 10, bold), precision: 6);
    }

    [Fact]
    public void Wrap_breaks_at_spaces_and_splits_words_that_are_too_long()
    {
        Assert.Equal(["Replace front", "pads and discs"], PdfText.Wrap("Replace front  pads and discs", 70, 10, bold: false).ToArray());
        Assert.Equal(["WWWW", "WWWW", "WW"], PdfText.Wrap("WWWWWWWWWW", 40, 10, bold: false).ToArray());
        Assert.Empty(PdfText.Wrap("   ", 40, 10, bold: false));
    }

    [Fact]
    public void Literal_escapes_delimiters_and_keeps_the_stream_ascii()
    {
        Assert.Equal(@"(a \(b\) \\ c)", PdfText.Literal(@"a (b) \ c"));
        Assert.Equal(@"(5 \200 \226 tab here)", PdfText.Literal("5 € – tab\there"));
    }

    private static Invoice NewInvoice(int lines = 0, string customerName = "Anna Schmidt")
    {
        var workOrder = WorkOrder.Open(Guid.NewGuid(), Guid.NewGuid(), "Annual service", 62_500, TestData.Now);
        if (lines == 0)
        {
            workOrder.AddLabour("Replace front pads and discs", 1.5m, 95m);
            workOrder.AddPart("0986494", "Brake disc, front", 2m, 48.5m);
        }

        for (var i = 1; i <= lines; i++)
        {
            workOrder.AddPart(null, string.Create(CultureInfo.InvariantCulture, $"Position {i}"), 1m, 10m);
        }

        workOrder.Approve(TestData.Now);
        workOrder.Start(TestData.Now);
        workOrder.Complete(TestData.Now);

        return Invoice.IssueFor(
            workOrder,
            42,
            customerName,
            "Volkswagen Golf (2019), M-AB 123",
            19m,
            14,
            new DateTimeOffset(3000, 6, 1, 9, 0, 0, TimeSpan.Zero));
    }

    private static InvoiceDetails Details(Invoice invoice) => InvoiceDetails.From(invoice, invoice.IssuedAt);

    private static string Ascii(InvoiceDocument document)
    {
        Assert.All(document.Content.ToArray(), b => Assert.InRange(b, (byte)10, (byte)126));
        return Encoding.ASCII.GetString(document.Content.Span);
    }

    private static int PageCount(string pdf) => Regex.Matches(pdf, @"/Type /Page ").Count;

    /// <summary>
    /// Set <c>AUTOFLOW_PDF_SAMPLE_DIR</c> to keep the rendered invoices, e.g.
    /// to look at them in a PDF viewer after changing the layout.
    /// </summary>
    private static void KeepSample(InvoiceDocument document, string suffix = "")
    {
        var directory = Environment.GetEnvironmentVariable("AUTOFLOW_PDF_SAMPLE_DIR");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, $"sample{suffix}-{document.FileName}"), document.Content.ToArray());
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

using System.Globalization;
using System.Text;

namespace AutoFlow.Infrastructure.Documents;

/// <summary>
/// A deliberately small PDF 1.4 writer: A4 pages with text in Helvetica or
/// Helvetica-Bold and straight lines — all an invoice needs. Coordinates are
/// in points from the bottom-left corner, as in PDF itself. See ADR 0020 for
/// why this is not a PDF library.
/// </summary>
internal sealed class PdfDocument
{
    /// <summary>A4 in points (1/72 inch).</summary>
    public const double PageWidth = 595.28;

    /// <inheritdoc cref="PageWidth"/>
    public const double PageHeight = 841.89;

    private const int CatalogObject = 1;
    private const int PagesObject = 2;
    private const int RegularFontObject = 3;
    private const int BoldFontObject = 4;
    private const int FirstPageObject = 5;

    private readonly List<PdfPage> _pages = [];

    public IReadOnlyList<PdfPage> Pages => _pages;

    public PdfPage AddPage()
    {
        var page = new PdfPage();
        _pages.Add(page);
        return page;
    }

    /// <summary>Serializes the document: header, objects, cross-reference table, trailer.</summary>
    public byte[] Save(string title, string author, DateTimeOffset createdAt)
    {
        if (_pages.Count == 0)
        {
            throw new InvalidOperationException("A PDF document needs at least one page.");
        }

        // Every page is two objects: the page itself and its content stream.
        var infoObject = FirstPageObject + (_pages.Count * 2);
        var objects = new List<string>
        {
            $"<< /Type /Catalog /Pages {PagesObject} 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', _pages.Select((_, i) => $"{PageObject(i)} 0 R"))}] /Count {_pages.Count} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
        };

        for (var i = 0; i < _pages.Count; i++)
        {
            var content = _pages[i].Content;
            objects.Add(
                $"<< /Type /Page /Parent {PagesObject} 0 R " +
                $"/MediaBox [0 0 {PdfText.Number(PageWidth)} {PdfText.Number(PageHeight)}] " +
                $"/Resources << /Font << /{PdfPage.RegularFont} {RegularFontObject} 0 R /{PdfPage.BoldFont} {BoldFontObject} 0 R >> >> " +
                $"/Contents {PageObject(i) + 1} 0 R >>");
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }

        var created = createdAt.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        objects.Add(
            $"<< /Title {PdfText.Literal(title)} /Author {PdfText.Literal(author)} " +
            $"/Producer (AutoFlow) /CreationDate (D:{created}Z) >>");

        // Content streams hold only ASCII (PdfText.Literal escapes the rest), so
        // one char is one byte and string lengths are byte offsets.
        var file = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(file.Length);
            file.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefOffset = file.Length;
        file.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n");
        file.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            file.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        file.Append(
            CultureInfo.InvariantCulture,
            $"trailer\n<< /Size {objects.Count + 1} /Root {CatalogObject} 0 R /Info {infoObject} 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");

        return Encoding.ASCII.GetBytes(file.ToString());
    }

    private static int PageObject(int pageIndex) => FirstPageObject + (pageIndex * 2);
}

/// <summary>One page's content stream, built up by drawing calls.</summary>
internal sealed class PdfPage
{
    public const string RegularFont = "F1";
    public const string BoldFont = "F2";

    private readonly StringBuilder _content = new();

    /// <summary>The content stream so far (plain ASCII PDF operators).</summary>
    public string Content => _content.ToString().TrimEnd('\n');

    /// <summary>Draws <paramref name="text"/> with its baseline starting at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <param name="gray">0 is black, 1 is white.</param>
    public void Text(double x, double y, string text, double size, bool bold = false, double gray = 0)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _content.Append(
            CultureInfo.InvariantCulture,
            $"BT {PdfText.Number(gray)} g /{(bold ? BoldFont : RegularFont)} {PdfText.Number(size)} Tf " +
            $"{PdfText.Number(x)} {PdfText.Number(y)} Td {PdfText.Literal(text)} Tj ET\n");
    }

    /// <summary>Draws <paramref name="text"/> so that it ends at <paramref name="xRight"/>, for columns of amounts.</summary>
    public void TextRight(double xRight, double y, string text, double size, bool bold = false, double gray = 0) =>
        Text(xRight - PdfText.WidthOf(text, size, bold), y, text, size, bold, gray);

    public void Line(double x1, double y1, double x2, double y2, double width = 0.5, double gray = 0)
    {
        _content.Append(
            CultureInfo.InvariantCulture,
            $"{PdfText.Number(gray)} G {PdfText.Number(width)} w " +
            $"{PdfText.Number(x1)} {PdfText.Number(y1)} m {PdfText.Number(x2)} {PdfText.Number(y2)} l S\n");
    }
}

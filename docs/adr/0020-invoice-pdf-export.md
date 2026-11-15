# ADR 0020: Invoice PDF export — a port and a small hand-written renderer

- Status: Accepted
- Date: 2026-11-15

## Context

The customer gets the invoice as a PDF. The layout is simple and fixed: a
header, who and which vehicle, a table of positions that may run over
several pages, totals, payments. The API runs in a slim Linux container.

## Decision

- **A port in the application layer.** `IInvoiceDocumentRenderer.Render`
  takes the `InvoiceDetails` read model and the workshop's
  `InvoicingSettings` and returns an `InvoiceDocument` (file name, content
  type, bytes). `ExportInvoiceHandler` loads the invoice and calls it. What
  is on an invoice stays an application concern; format and layout are
  infrastructure, and use-case tests run with a stub renderer.
- **The implementation writes PDF 1.4 directly** (`PdfDocument`,
  `PdfText`, `PdfInvoiceRenderer` in `Infrastructure/Documents`, about 400
  lines): pages with text in the standard fonts Helvetica and
  Helvetica-Bold and straight lines. Standard fonts are never embedded, so
  an invoice is a few kilobytes; their published metrics are enough to
  right-align amounts and wrap descriptions.
- **Why not a PDF library?** The popular .NET options either wrap native
  rendering libraries (extra packages in the container image, another thing
  that differs between a developer's machine and production) or come with
  licence terms that need watching. For one fixed layout neither is worth
  it. If documents with images, embedded fonts or user-defined templates are
  ever needed, a library replaces the three classes behind the port without
  touching a use case.
- **Text is WinAnsi (Windows-1252).** Characters the standard fonts lack
  are replaced by their base letter (`ğ → g`, `ı → i`), or `?` when there is
  none, rather than failing the export. Supporting other scripts means
  embedding a font, which is the point at which a library earns its place.
- **Content streams are plain ASCII** (non-ASCII bytes as octal escapes),
  which makes byte offsets trivially correct and lets tests assert on the
  printed text. Tests also check what readers depend on: the
  cross-reference offsets and every stream's declared length.
- **Culture-independent output.** Amounts are printed as `1,234.56`, dates
  as ISO dates in UTC, labels in English, wherever the server runs.

## Consequences

- No new package and nothing native in the image.
- The exported PDF shows the payments and balance at the time of export;
  the positions and totals never change (ADR 0018).
- Layout changes are made in code; setting `AUTOFLOW_PDF_SAMPLE_DIR` when
  running the tests writes sample invoices to look at.

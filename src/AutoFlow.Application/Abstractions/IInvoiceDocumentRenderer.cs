using AutoFlow.Application.Invoices;

namespace AutoFlow.Application.Abstractions;

/// <summary>
/// Turns an invoice into the file the customer receives. The application
/// layer decides what is on an invoice (<see cref="InvoiceDetails"/>); how it
/// is laid out and in which format is up to the implementation.
/// </summary>
public interface IInvoiceDocumentRenderer
{
    InvoiceDocument Render(InvoiceDetails invoice, InvoicingSettings settings);
}

/// <summary>A rendered invoice, ready to be downloaded.</summary>
/// <param name="FileName">Suggested file name, e.g. <c>invoice-2026-00042.pdf</c>.</param>
/// <param name="ContentType">Media type of <paramref name="Content"/>.</param>
/// <param name="Content">The file's bytes.</param>
public sealed record InvoiceDocument(string FileName, string ContentType, ReadOnlyMemory<byte> Content);

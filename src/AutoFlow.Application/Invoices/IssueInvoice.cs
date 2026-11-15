using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Application.Customers;
using AutoFlow.Application.Vehicles;
using AutoFlow.Application.WorkOrders;
using AutoFlow.Domain.Invoices;
using AutoFlow.Domain.Vehicles;

namespace AutoFlow.Application.Invoices;

public sealed record IssueInvoiceCommand(Guid WorkOrderId);

/// <summary>
/// Bills a completed work order: issues the invoice with the next number of
/// the year, the configured VAT rate and payment term, and marks the work
/// order as invoiced, all in one commit (ADR 0018, ADR 0019). A work order
/// that is not completed, or was invoiced before, breaks a rule (422). Two
/// invoices issued at the same moment may pick the same number; the database
/// lets one through and the other is a conflict (409) that can be repeated.
/// </summary>
public sealed class IssueInvoiceHandler(
    IWorkOrderRepository workOrders,
    ICustomerRepository customers,
    IVehicleRepository vehicles,
    IInvoiceRepository invoices,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    InvoicingSettings settings)
{
    public async Task<Result<InvoiceDetails>> HandleAsync(
        IssueInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var workOrder = await workOrders.GetByIdAsync(command.WorkOrderId, cancellationToken);
        if (workOrder is null)
        {
            return Result.Failure<InvoiceDetails>(WorkOrderErrors.NotFound(command.WorkOrderId));
        }

        var customer = await customers.GetByIdAsync(workOrder.CustomerId, cancellationToken);
        if (customer is null)
        {
            return Result.Failure<InvoiceDetails>(CustomerErrors.NotFound(workOrder.CustomerId));
        }

        var vehicle = await vehicles.GetByIdAsync(workOrder.VehicleId, cancellationToken);
        if (vehicle is null)
        {
            return Result.Failure<InvoiceDetails>(VehicleErrors.NotFound(workOrder.VehicleId));
        }

        var now = timeProvider.GetUtcNow();
        var lastSequence = await invoices.GetLastSequenceAsync(InvoiceNumber.YearOf(now), cancellationToken);

        var invoice = Invoice.IssueFor(
            workOrder,
            lastSequence + 1,
            customer.FullName,
            Describe(vehicle),
            settings.VatRatePercent,
            settings.PaymentTermDays,
            now);
        workOrder.MarkInvoiced(now);

        invoices.Add(invoice);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(InvoiceDetails.From(invoice, now));
    }

    /// <summary>The vehicle as printed on the invoice: <c>Volkswagen Golf (2019), M-AB 123, VIN WVW…</c>.</summary>
    internal static string Describe(Vehicle vehicle) =>
        $"{vehicle.Make} {vehicle.Model} ({vehicle.ModelYear}), {vehicle.LicensePlate.Value}, VIN {vehicle.Vin.Value}";
}

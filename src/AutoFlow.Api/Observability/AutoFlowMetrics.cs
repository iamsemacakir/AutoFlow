using System.Diagnostics.Metrics;

namespace AutoFlow.Api.Observability;

/// <summary>
/// Business metrics next to the technical ones (requests, latency, database
/// connections): how much work comes in and how much is billed. Exported
/// with everything else through OpenTelemetry under the meter
/// <see cref="MeterName"/>.
/// </summary>
public sealed class AutoFlowMetrics
{
    public const string MeterName = "AutoFlow";

    private readonly Counter<long> _workOrdersOpened;
    private readonly Counter<long> _appointmentsBooked;
    private readonly Counter<long> _invoicesIssued;
    private readonly Histogram<double> _invoicedAmount;
    private readonly Counter<long> _paymentsRecorded;

    public AutoFlowMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(MeterName);
        _workOrdersOpened = meter.CreateCounter<long>("autoflow.work_orders.opened", "{work_order}", "Work orders opened at intake or check-in.");
        _appointmentsBooked = meter.CreateCounter<long>("autoflow.appointments.booked", "{appointment}", "Appointments booked.");
        _invoicesIssued = meter.CreateCounter<long>("autoflow.invoices.issued", "{invoice}", "Invoices issued.");
        _invoicedAmount = meter.CreateHistogram<double>("autoflow.invoices.gross_amount", "{currency}", "Gross total of each issued invoice.");
        _paymentsRecorded = meter.CreateCounter<long>("autoflow.payments.recorded", "{payment}", "Payments recorded, by method.");
    }

    public void WorkOrderOpened(string source) =>
        _workOrdersOpened.Add(1, new KeyValuePair<string, object?>("autoflow.source", source));

    public void AppointmentBooked() => _appointmentsBooked.Add(1);

    public void InvoiceIssued(decimal grossTotal)
    {
        _invoicesIssued.Add(1);
        _invoicedAmount.Record((double)grossTotal);
    }

    public void PaymentRecorded(string method) =>
        _paymentsRecorded.Add(1, new KeyValuePair<string, object?>("autoflow.payment_method", method));
}

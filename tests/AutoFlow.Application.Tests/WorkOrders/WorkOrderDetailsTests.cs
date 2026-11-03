using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Application.WorkOrders;

namespace AutoFlow.Application.Tests.WorkOrders;

public class WorkOrderDetailsTests
{
    [Fact]
    public void From_maps_lines_in_number_order_with_kind_names_and_totals()
    {
        var workOrder = TestData.WorkOrder(TestData.Vehicle(Guid.NewGuid()));
        workOrder.AddLabour("Replace front brake pads", 1.5m, 95m);
        workOrder.AddPart("bp-12", "Brake pads, front", 1m, 64m);
        workOrder.Approve(FixedTimeProvider.Default.AddHours(1));

        var details = WorkOrderDetails.From(workOrder);

        Assert.Equal("Approved", details.Status);
        Assert.Collection(
            details.Lines,
            line =>
            {
                Assert.Equal((1, "Labour", (string?)null, 142.50m), (line.Number, line.Kind, line.PartNumber, line.Total));
            },
            line =>
            {
                Assert.Equal((2, "Part", (string?)"BP-12", 64m), (line.Number, line.Kind, line.PartNumber, line.Total));
            });
        Assert.Equal((142.50m, 64m, 206.50m), (details.LabourTotal, details.PartsTotal, details.Total));
        Assert.Equal<decimal?>(206.50m, details.ApprovedTotal);
        Assert.Equal<DateTimeOffset?>(FixedTimeProvider.Default.AddHours(1), details.ApprovedAt);
    }
}

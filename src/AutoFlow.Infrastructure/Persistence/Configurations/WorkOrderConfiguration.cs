using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Vehicles;
using AutoFlow.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the work order aggregate to two tables: <c>work_orders</c> for the
/// root and <c>work_order_lines</c> for its lines. Lines are an owned
/// collection, so they are always loaded, saved and deleted together with
/// their work order and can never be queried on their own.
/// </summary>
internal sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    /// <summary>Name of the shadow foreign key from a line to its work order.</summary>
    public const string LineWorkOrderIdProperty = "WorkOrderId";

    /// <summary>Longest status or line kind name; stored as text (see ADR 0010).</summary>
    private const int EnumNameMaxLength = 20;

    /// <summary>
    /// Digits for a work order total: 100 lines × 10,000 × 1,000,000 needs 13
    /// before the decimal point.
    /// </summary>
    private const int MoneyTotalPrecision = 15;

    /// <summary>Digits for a unit price: at most 1,000,000.00.</summary>
    private const int UnitPricePrecision = 9;

    /// <summary>Digits for a quantity: at most 10,000.00.</summary>
    private const int QuantityPrecision = 7;

    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.ToTable("work_orders", table =>
        {
            table.HasCheckConstraint(
                "ck_work_orders_mileage_km_at_intake_non_negative",
                "mileage_km_at_intake >= 0");
            table.HasCheckConstraint(
                "ck_work_orders_status",
                $"status IN ({SqlList(Enum.GetNames<WorkOrderStatus>())})");
        });

        builder.HasKey(w => w.Id).HasName("pk_work_orders");
        builder.Property(w => w.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(w => w.VehicleId).HasColumnName("vehicle_id");
        builder.HasOne<Vehicle>()
            .WithMany()
            .HasForeignKey(w => w.VehicleId)
            .HasConstraintName("fk_work_orders_vehicles_vehicle_id")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(w => w.VehicleId).HasDatabaseName("ix_work_orders_vehicle_id");

        builder.Property(w => w.CustomerId).HasColumnName("customer_id");
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(w => w.CustomerId)
            .HasConstraintName("fk_work_orders_customers_customer_id")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(w => w.CustomerId).HasDatabaseName("ix_work_orders_customer_id");

        builder.Property(w => w.Complaint)
            .HasColumnName("complaint")
            .HasMaxLength(WorkOrder.ComplaintMaxLength)
            .IsRequired();

        builder.Property(w => w.MileageKmAtIntake).HasColumnName("mileage_km_at_intake");

        builder.Property(w => w.Status)
            .HasColumnName("status")
            .HasMaxLength(EnumNameMaxLength)
            .HasConversion<string>();

        builder.Property(w => w.CreatedAt).HasColumnName("created_at");
        builder.Property(w => w.ApprovedAt).HasColumnName("approved_at");
        builder.Property(w => w.ApprovedTotal)
            .HasColumnName("approved_total")
            .HasPrecision(MoneyTotalPrecision, WorkOrderLine.MoneyDecimals);
        builder.Property(w => w.StartedAt).HasColumnName("started_at");
        builder.Property(w => w.CompletedAt).HasColumnName("completed_at");
        builder.Property(w => w.InvoicedAt).HasColumnName("invoiced_at");
        builder.Property(w => w.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(w => w.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(WorkOrder.CancellationReasonMaxLength);

        builder.ConfigureRowVersion();

        builder.OwnsMany(w => w.Lines, ConfigureLines);
        builder.Navigation(w => w.Lines)
            .HasField("_lines")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureLines(OwnedNavigationBuilder<WorkOrder, WorkOrderLine> lines)
    {
        lines.ToTable("work_order_lines", table =>
        {
            table.HasCheckConstraint(
                "ck_work_order_lines_kind",
                $"kind IN ({SqlList(Enum.GetNames<WorkOrderLineKind>())})");
            table.HasCheckConstraint("ck_work_order_lines_number_positive", "number > 0");
            table.HasCheckConstraint("ck_work_order_lines_quantity_positive", "quantity > 0");
            table.HasCheckConstraint("ck_work_order_lines_unit_price_non_negative", "unit_price >= 0");
            table.HasCheckConstraint(
                "ck_work_order_lines_part_number_only_for_parts",
                $"kind = '{nameof(WorkOrderLineKind.Part)}' OR part_number IS NULL");
        });

        lines.WithOwner()
            .HasForeignKey(LineWorkOrderIdProperty)
            .HasConstraintName("fk_work_order_lines_work_orders_work_order_id");
        lines.Property<Guid>(LineWorkOrderIdProperty).HasColumnName("work_order_id");

        lines.HasKey(l => l.Id).HasName("pk_work_order_lines");
        lines.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();

        // Also serves as the foreign-key index; a number is never used twice on one work order.
        lines.HasIndex(LineWorkOrderIdProperty, nameof(WorkOrderLine.Number))
            .IsUnique()
            .HasDatabaseName("ux_work_order_lines_work_order_id_number");

        lines.Property(l => l.Number).HasColumnName("number");

        lines.Property(l => l.Kind)
            .HasColumnName("kind")
            .HasMaxLength(EnumNameMaxLength)
            .HasConversion<string>();

        lines.Property(l => l.Description)
            .HasColumnName("description")
            .HasMaxLength(WorkOrderLine.DescriptionMaxLength)
            .IsRequired();

        lines.Property(l => l.PartNumber)
            .HasColumnName("part_number")
            .HasMaxLength(WorkOrderLine.PartNumberMaxLength);

        lines.Property(l => l.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(QuantityPrecision, WorkOrderLine.QuantityDecimals);

        lines.Property(l => l.UnitPrice)
            .HasColumnName("unit_price")
            .HasPrecision(UnitPricePrecision, WorkOrderLine.MoneyDecimals);

        lines.ConfigureRowVersion();
    }

    private static string SqlList(IEnumerable<string> names) =>
        string.Join(", ", names.Select(name => $"'{name}'"));
}

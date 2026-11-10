using AutoFlow.Domain.Appointments;
using AutoFlow.Domain.Bays;
using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Vehicles;
using AutoFlow.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps appointments to the <c>appointments</c> table. The slot is a complex
/// property in two columns, <c>slot_start</c> and <c>slot_end</c>, which the
/// exclusion constraints against double booking read as the half-open range
/// <c>[slot_start, slot_end)</c> (ADR 0015).
/// </summary>
internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    /// <summary>One clashing appointment per bay and slot: two vehicles on one lift.</summary>
    public const string BayExclusionConstraint = "ex_appointments_bay_id_slot";

    /// <summary>One clashing appointment per vehicle and slot: one car in two bays.</summary>
    public const string VehicleExclusionConstraint = "ex_appointments_vehicle_id_slot";

    /// <summary>Longest status name; stored as text like work order statuses.</summary>
    private const int EnumNameMaxLength = 20;

    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments", table =>
        {
            table.HasCheckConstraint(
                "ck_appointments_status",
                $"status IN ({SqlList(Enum.GetNames<AppointmentStatus>())})");
            table.HasCheckConstraint("ck_appointments_slot_ends_after_start", "slot_end > slot_start");
            table.HasCheckConstraint(
                "ck_appointments_checked_in_has_work_order",
                $"status <> '{nameof(AppointmentStatus.CheckedIn)}' OR work_order_id IS NOT NULL");
        });

        builder.HasKey(a => a.Id).HasName("pk_appointments");
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(a => a.BayId).HasColumnName("bay_id");
        builder.HasOne<Bay>()
            .WithMany()
            .HasForeignKey(a => a.BayId)
            .HasConstraintName("fk_appointments_bays_bay_id")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.BayId).HasDatabaseName("ix_appointments_bay_id");

        builder.Property(a => a.VehicleId).HasColumnName("vehicle_id");
        builder.HasOne<Vehicle>()
            .WithMany()
            .HasForeignKey(a => a.VehicleId)
            .HasConstraintName("fk_appointments_vehicles_vehicle_id")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.VehicleId).HasDatabaseName("ix_appointments_vehicle_id");

        builder.Property(a => a.CustomerId).HasColumnName("customer_id");
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(a => a.CustomerId)
            .HasConstraintName("fk_appointments_customers_customer_id")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.CustomerId).HasDatabaseName("ix_appointments_customer_id");

        builder.ComplexProperty(a => a.Slot, slot =>
        {
            slot.Property(s => s.Start).HasColumnName("slot_start");
            slot.Property(s => s.End).HasColumnName("slot_end");
        });

        builder.Property(a => a.Purpose)
            .HasColumnName("purpose")
            .HasMaxLength(Appointment.PurposeMaxLength)
            .IsRequired();

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasMaxLength(EnumNameMaxLength)
            .HasConversion<string>();

        builder.Property(a => a.BookedAt).HasColumnName("booked_at");
        builder.Property(a => a.RescheduledAt).HasColumnName("rescheduled_at");

        // A work order is opened for at most one appointment; the index also serves the foreign key.
        builder.Property(a => a.WorkOrderId).HasColumnName("work_order_id");
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(a => a.WorkOrderId)
            .HasConstraintName("fk_appointments_work_orders_work_order_id")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.WorkOrderId).IsUnique().HasDatabaseName("ux_appointments_work_order_id");

        builder.Property(a => a.CheckedInAt).HasColumnName("checked_in_at");
        builder.Property(a => a.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(a => a.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(Appointment.CancellationReasonMaxLength);
        builder.Property(a => a.NoShowAt).HasColumnName("no_show_at");

        builder.ConfigureRowVersion();
    }

    private static string SqlList(IEnumerable<string> names) =>
        string.Join(", ", names.Select(name => $"'{name}'"));
}

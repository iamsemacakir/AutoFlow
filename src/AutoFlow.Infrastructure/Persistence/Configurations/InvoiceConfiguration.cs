using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Invoices;
using AutoFlow.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the invoice aggregate to three tables: <c>invoices</c> for the root,
/// <c>invoice_lines</c> and <c>invoice_payments</c> for its owned
/// collections. Two unique indexes carry the rules that only the database
/// can guarantee under concurrency: an invoice number is used once, and a
/// work order is invoiced once (ADR 0019).
/// </summary>
internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    /// <summary>Unique index over year and sequence: the invoice number.</summary>
    public const string NumberIndex = "ux_invoices_year_sequence";

    /// <summary>Unique index that allows one invoice per work order.</summary>
    public const string WorkOrderIndex = "ux_invoices_work_order_id";

    /// <summary>Name of the shadow foreign key from a line or payment to its invoice.</summary>
    public const string InvoiceIdProperty = "InvoiceId";

    private const int EnumNameMaxLength = 20;

    /// <summary>Digits for a gross total: a work order total (13 before the point) plus VAT.</summary>
    private const int MoneyTotalPrecision = 16;

    private const int UnitPricePrecision = 9;
    private const int QuantityPrecision = 7;
    private const int VatRatePrecision = 5;

    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", table =>
        {
            table.HasCheckConstraint(
                "ck_invoices_status",
                $"status IN ({SqlList(Enum.GetNames<InvoiceStatus>())})");
            table.HasCheckConstraint("ck_invoices_sequence_positive", "sequence > 0");
            table.HasCheckConstraint(
                "ck_invoices_vat_rate_percent_range",
                "vat_rate_percent >= 0 AND vat_rate_percent <= 100");
            table.HasCheckConstraint("ck_invoices_amount_paid_non_negative", "amount_paid >= 0");
        });

        builder.HasKey(i => i.Id).HasName("pk_invoices");
        builder.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(i => i.Year).HasColumnName("year");
        builder.Property(i => i.Sequence).HasColumnName("sequence");
        builder.HasIndex(i => new { i.Year, i.Sequence }).IsUnique().HasDatabaseName(NumberIndex);

        builder.Property(i => i.WorkOrderId).HasColumnName("work_order_id");
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(i => i.WorkOrderId)
            .HasConstraintName("fk_invoices_work_orders_work_order_id")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.WorkOrderId).IsUnique().HasDatabaseName(WorkOrderIndex);

        builder.Property(i => i.CustomerId).HasColumnName("customer_id");
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(i => i.CustomerId)
            .HasConstraintName("fk_invoices_customers_customer_id")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.CustomerId).HasDatabaseName("ix_invoices_customer_id");

        builder.Property(i => i.CustomerName)
            .HasColumnName("customer_name")
            .HasMaxLength(Invoice.CustomerNameMaxLength)
            .IsRequired();

        builder.Property(i => i.VehicleDescription)
            .HasColumnName("vehicle_description")
            .HasMaxLength(Invoice.VehicleDescriptionMaxLength)
            .IsRequired();

        builder.Property(i => i.VatRatePercent)
            .HasColumnName("vat_rate_percent")
            .HasPrecision(VatRatePrecision, Invoice.VatRateDecimals);

        builder.Property(i => i.Status)
            .HasColumnName("status")
            .HasMaxLength(EnumNameMaxLength)
            .HasConversion<string>();
        builder.HasIndex(i => i.Status).HasDatabaseName("ix_invoices_status");

        builder.Property(i => i.IssuedAt).HasColumnName("issued_at");
        builder.Property(i => i.DueDate).HasColumnName("due_date");
        builder.Property(i => i.PaidAt).HasColumnName("paid_at");
        builder.Property(i => i.AmountPaid)
            .HasColumnName("amount_paid")
            .HasPrecision(MoneyTotalPrecision, WorkOrderLine.MoneyDecimals);

        builder.ConfigureRowVersion();

        builder.OwnsMany(i => i.Lines, ConfigureLines);
        builder.Navigation(i => i.Lines)
            .HasField("_lines")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(i => i.Payments, ConfigurePayments);
        builder.Navigation(i => i.Payments)
            .HasField("_payments")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureLines(OwnedNavigationBuilder<Invoice, InvoiceLine> lines)
    {
        lines.ToTable("invoice_lines", table =>
        {
            table.HasCheckConstraint(
                "ck_invoice_lines_kind",
                $"kind IN ({SqlList(Enum.GetNames<WorkOrderLineKind>())})");
            table.HasCheckConstraint("ck_invoice_lines_number_positive", "number > 0");
            table.HasCheckConstraint("ck_invoice_lines_quantity_positive", "quantity > 0");
            table.HasCheckConstraint("ck_invoice_lines_unit_price_non_negative", "unit_price >= 0");
        });

        lines.WithOwner()
            .HasForeignKey(InvoiceIdProperty)
            .HasConstraintName("fk_invoice_lines_invoices_invoice_id");
        lines.Property<Guid>(InvoiceIdProperty).HasColumnName("invoice_id");

        lines.HasKey(l => l.Id).HasName("pk_invoice_lines");
        lines.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();

        // Also serves as the foreign-key index.
        lines.HasIndex(InvoiceIdProperty, nameof(InvoiceLine.Number))
            .IsUnique()
            .HasDatabaseName("ux_invoice_lines_invoice_id_number");

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

    private static void ConfigurePayments(OwnedNavigationBuilder<Invoice, InvoicePayment> payments)
    {
        payments.ToTable("invoice_payments", table =>
        {
            table.HasCheckConstraint(
                "ck_invoice_payments_method",
                $"method IN ({SqlList(Enum.GetNames<PaymentMethod>())})");
            table.HasCheckConstraint("ck_invoice_payments_amount_positive", "amount > 0");
        });

        payments.WithOwner()
            .HasForeignKey(InvoiceIdProperty)
            .HasConstraintName("fk_invoice_payments_invoices_invoice_id");
        payments.Property<Guid>(InvoiceIdProperty).HasColumnName("invoice_id");
        payments.HasIndex(InvoiceIdProperty).HasDatabaseName("ix_invoice_payments_invoice_id");

        payments.HasKey(p => p.Id).HasName("pk_invoice_payments");
        payments.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();

        payments.Property(p => p.Amount)
            .HasColumnName("amount")
            .HasPrecision(MoneyTotalPrecision, WorkOrderLine.MoneyDecimals);

        payments.Property(p => p.Method)
            .HasColumnName("method")
            .HasMaxLength(EnumNameMaxLength)
            .HasConversion<string>();

        payments.Property(p => p.ReceivedAt).HasColumnName("received_at");

        payments.ConfigureRowVersion();
    }

    private static string SqlList(IEnumerable<string> names) =>
        string.Join(", ", names.Select(name => $"'{name}'"));
}

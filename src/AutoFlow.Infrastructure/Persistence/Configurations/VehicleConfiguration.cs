using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Vehicles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoFlow.Infrastructure.Persistence.Configurations;

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("vehicles", table =>
        {
            table.HasCheckConstraint("ck_vehicles_mileage_km_non_negative", "mileage_km >= 0");
            table.HasCheckConstraint(
                "ck_vehicles_model_year",
                $"model_year >= {Vehicle.EarliestModelYear}");
        });

        builder.HasKey(v => v.Id).HasName("pk_vehicles");
        builder.Property(v => v.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(v => v.CustomerId).HasColumnName("customer_id");

        // A vehicle always belongs to a customer, but the domain references it by
        // id only (no navigation), so aggregates stay independent.
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(v => v.CustomerId)
            .HasConstraintName("fk_vehicles_customers_customer_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(v => v.CustomerId).HasDatabaseName("ix_vehicles_customer_id");

        builder.Property(v => v.Vin)
            .HasColumnName("vin")
            .HasMaxLength(Vin.Length)
            .IsFixedLength()
            .HasConversion(ValueConverters.Vin)
            .IsRequired();

        // The application checks for duplicates first; the index is the safety
        // net for two registrations racing each other.
        builder.HasIndex(v => v.Vin).IsUnique().HasDatabaseName("ux_vehicles_vin");

        builder.Property(v => v.LicensePlate)
            .HasColumnName("license_plate")
            .HasMaxLength(LicensePlate.MaxLength)
            .HasConversion(ValueConverters.LicensePlate)
            .IsRequired();

        builder.HasIndex(v => v.LicensePlate).HasDatabaseName("ix_vehicles_license_plate");

        builder.Property(v => v.Make)
            .HasColumnName("make")
            .HasMaxLength(Vehicle.MakeModelMaxLength)
            .IsRequired();

        builder.Property(v => v.Model)
            .HasColumnName("model")
            .HasMaxLength(Vehicle.MakeModelMaxLength)
            .IsRequired();

        builder.Property(v => v.ModelYear).HasColumnName("model_year");
        builder.Property(v => v.MileageKm).HasColumnName("mileage_km");
        builder.Property(v => v.RegisteredAt).HasColumnName("registered_at");

        builder.ConfigureRowVersion();
    }
}

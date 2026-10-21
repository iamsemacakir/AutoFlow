using AutoFlow.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoFlow.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

        builder.HasKey(c => c.Id).HasName("pk_customers");
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(c => c.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(Customer.NameMaxLength)
            .IsRequired();

        builder.Property(c => c.LastName)
            .HasColumnName("last_name")
            .HasMaxLength(Customer.NameMaxLength)
            .IsRequired();

        builder.Property(c => c.Email)
            .HasColumnName("email")
            .HasMaxLength(EmailAddress.MaxLength)
            .HasConversion(ValueConverters.EmailAddress)
            .IsRequired();

        builder.Property(c => c.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(Customer.PhoneMaxLength);

        builder.Property(c => c.CreatedAt).HasColumnName("created_at");

        builder.ConfigureRowVersion();

        builder.HasIndex(c => c.Email).HasDatabaseName("ix_customers_email");
    }
}

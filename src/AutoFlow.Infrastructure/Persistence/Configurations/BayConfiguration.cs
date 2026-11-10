using AutoFlow.Domain.Bays;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoFlow.Infrastructure.Persistence.Configurations;

internal sealed class BayConfiguration : IEntityTypeConfiguration<Bay>
{
    public void Configure(EntityTypeBuilder<Bay> builder)
    {
        builder.ToTable("bays");

        builder.HasKey(b => b.Id).HasName("pk_bays");
        builder.Property(b => b.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(b => b.Name)
            .HasColumnName("name")
            .HasMaxLength(Bay.NameMaxLength)
            .IsRequired();

        // The day schedule shows bays by name, so two bays called "Lift 1" would be ambiguous.
        builder.HasIndex(b => b.Name).IsUnique().HasDatabaseName("ux_bays_name");

        builder.Property(b => b.IsInService).HasColumnName("is_in_service");
        builder.Property(b => b.CreatedAt).HasColumnName("created_at");

        builder.ConfigureRowVersion();
    }
}

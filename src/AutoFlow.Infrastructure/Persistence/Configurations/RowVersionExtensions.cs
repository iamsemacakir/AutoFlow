using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoFlow.Infrastructure.Persistence.Configurations;

internal static class RowVersionExtensions
{
    /// <summary>Name of the shadow property that holds PostgreSQL's <c>xmin</c>.</summary>
    public const string PropertyName = "Version";

    /// <summary>
    /// Enables optimistic concurrency through PostgreSQL's <c>xmin</c> system
    /// column: an UPDATE only succeeds if the row is unchanged since it was read,
    /// otherwise EF Core throws <see cref="DbUpdateConcurrencyException"/>.
    /// The column exists on every table, so no schema change is needed, and
    /// the domain model stays free of persistence concerns (shadow property).
    /// </summary>
    public static void ConfigureRowVersion<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class =>
        builder.Property<uint>(PropertyName).IsRowVersion();
}

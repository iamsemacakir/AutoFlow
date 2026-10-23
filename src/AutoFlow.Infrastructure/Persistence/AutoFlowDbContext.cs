using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Vehicles;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoFlow.Infrastructure.Persistence;

/// <summary>
/// EF Core session over the AutoFlow PostgreSQL database. It is also the
/// <see cref="IUnitOfWork"/>: everything the repositories add or change in one
/// request scope is committed in a single transaction.
/// </summary>
public sealed class AutoFlowDbContext(DbContextOptions<AutoFlowDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    /// <summary>
    /// Commits like <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>,
    /// but translates the two conflicts a lost race can cause into the
    /// application's exceptions, so no layer above needs to know EF Core or
    /// PostgreSQL error codes. Other failures (e.g. a foreign-key violation,
    /// which a use case should have prevented) stay as they are: they are bugs.
    /// </summary>
    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException(
                "The data was changed by another request after it was loaded.",
                exception);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgresError)
        {
            throw new UniqueConstraintViolationException(
                "A record with the same unique value already exists.",
                postgresError.ConstraintName,
                exception);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AutoFlowDbContext).Assembly);
    }
}

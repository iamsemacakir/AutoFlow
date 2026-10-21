using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Vehicles;
using Microsoft.EntityFrameworkCore;

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

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        await SaveChangesAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AutoFlowDbContext).Assembly);
    }
}

using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Appointments;
using AutoFlow.Domain.Bays;
using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Invoices;
using AutoFlow.Domain.Users;
using AutoFlow.Domain.Vehicles;
using AutoFlow.Domain.WorkOrders;
using AutoFlow.Infrastructure.Persistence.Configurations;
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

    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();

    public DbSet<Bay> Bays => Set<Bay>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<User> Users => Set<User>();

    private readonly Dictionary<(Type Type, Guid Id), uint> _expectedVersions = [];

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        TouchWorkOrdersWithChangedLines();
        ApplyExpectedVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        TouchWorkOrdersWithChangedLines();
        ApplyExpectedVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Records the version a client's <c>If-Match</c> named for an aggregate
    /// (see <see cref="RowVersions"/>). The next save compares against it
    /// instead of the version the request happened to load.
    /// </summary>
    internal void ExpectVersion(Type aggregateType, Guid id, uint version) =>
        _expectedVersions[(aggregateType, id)] = version;

    /// <summary>
    /// Makes EF Core issue <c>UPDATE … WHERE xmin = @expected</c> for every
    /// aggregate with an expected version, so a change committed by another
    /// request between the <c>If-Match</c> check and this save is a concurrency
    /// conflict rather than a silently overwritten edit (ADR 0024).
    /// </summary>
    private void ApplyExpectedVersions()
    {
        if (_expectedVersions.Count == 0)
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is Domain.Common.Entity aggregate
                && entry.State is EntityState.Modified or EntityState.Deleted
                && _expectedVersions.TryGetValue((entry.Entity.GetType(), aggregate.Id), out var expected))
            {
                entry.Property(RowVersionExtensions.PropertyName).OriginalValue = expected;
            }
        }
    }

    /// <summary>
    /// Commits like <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>,
    /// but translates the two conflicts a lost race can cause into the
    /// application's exceptions, so no layer above needs to know EF Core or
    /// PostgreSQL error codes. An exclusion constraint (no two overlapping
    /// bookings) is uniqueness over a time range and is reported the same way
    /// as a unique index (ADR 0015). A deadlock or serialization failure between
    /// two simultaneous saves is a lost race too and becomes a concurrency
    /// conflict. Other failures (e.g. a foreign-key violation, which a use case
    /// should have prevented) stay as they are: they are bugs.
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
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ExclusionViolation,
            } postgresError)
        {
            throw new UniqueConstraintViolationException(
                "A record with the same unique value already exists.",
                postgresError.ConstraintName,
                exception);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure,
            })
        {
            // Simultaneous inserts that conflict on an exclusion constraint wait for each
            // other; PostgreSQL breaks the cycle by aborting one of them. That request lost
            // the race like any other, so it is a conflict to retry, not a server error.
            throw new ConcurrencyConflictException(
                "The request collided with another one that was being saved at the same moment.",
                exception);
        }
    }

    /// <summary>
    /// The <c>xmin</c> concurrency token sits on the <c>work_orders</c> row, so
    /// adding or removing a line alone would not change it and two concurrent
    /// edits of the same work order would both succeed. Marking the root as
    /// modified makes EF Core issue an <c>UPDATE … WHERE xmin = @version</c> on
    /// the work order in the same transaction: the edit that loses the race
    /// fails with a concurrency conflict, and every saved line change bumps
    /// the work order's version (ADR 0011).
    /// </summary>
    private void TouchWorkOrdersWithChangedLines()
    {
        var workOrderIds = ChangeTracker.Entries<WorkOrderLine>()
            .Where(line => line.State is EntityState.Added or EntityState.Deleted or EntityState.Modified)
            .Select(line =>
            {
                var workOrderId = line.Property(WorkOrderConfiguration.LineWorkOrderIdProperty);
                return (Guid)(line.State == EntityState.Deleted ? workOrderId.OriginalValue : workOrderId.CurrentValue)!;
            })
            .ToHashSet();

        if (workOrderIds.Count == 0)
        {
            return;
        }

        foreach (var workOrder in ChangeTracker.Entries<WorkOrder>())
        {
            if (workOrder.State == EntityState.Unchanged && workOrderIds.Contains(workOrder.Entity.Id))
            {
                workOrder.Property(w => w.Complaint).IsModified = true;
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // GiST support for plain equality (bay_id, vehicle_id) next to the range
        // overlap operator in the appointments' exclusion constraints.
        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AutoFlowDbContext).Assembly);
    }
}

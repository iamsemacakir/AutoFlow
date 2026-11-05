using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Vehicles;
using AutoFlow.Domain.WorkOrders;

namespace AutoFlow.Application.Tests.Fakes;

/// <summary>
/// In-memory stand-in for the database. Like EF Core, added entities become
/// visible only after <see cref="SaveChangesAsync"/>; loaded entities are live
/// references, so mutations are "tracked" automatically.
/// </summary>
public sealed class InMemoryStore : IUnitOfWork, ICustomerRepository, IVehicleRepository, IWorkOrderRepository
{
    private readonly Dictionary<Guid, Customer> _customers = [];
    private readonly Dictionary<Guid, Vehicle> _vehicles = [];
    private readonly List<Customer> _pendingCustomers = [];
    private readonly List<Vehicle> _pendingVehicles = [];
    private readonly List<Customer> _removedCustomers = [];
    private readonly Dictionary<Guid, WorkOrder> _workOrders = [];
    private readonly List<WorkOrder> _pendingWorkOrders = [];

    public int SaveCount { get; private set; }

    public IReadOnlyCollection<Customer> Customers => _customers.Values;

    public IReadOnlyCollection<Vehicle> Vehicles => _vehicles.Values;

    public IReadOnlyCollection<WorkOrder> WorkOrders => _workOrders.Values;

    /// <summary>Puts an entity straight into the "database", bypassing the unit of work.</summary>
    public Customer Seed(Customer customer)
    {
        _customers.Add(customer.Id, customer);
        return customer;
    }

    /// <inheritdoc cref="Seed(Customer)"/>
    public Vehicle Seed(Vehicle vehicle)
    {
        _vehicles.Add(vehicle.Id, vehicle);
        return vehicle;
    }

    /// <inheritdoc cref="Seed(Customer)"/>
    public WorkOrder Seed(WorkOrder workOrder)
    {
        _workOrders.Add(workOrder.Id, workOrder);
        return workOrder;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        _pendingCustomers.ForEach(c => _customers.Add(c.Id, c));
        _pendingVehicles.ForEach(v => _vehicles.Add(v.Id, v));
        _pendingWorkOrders.ForEach(w => _workOrders.Add(w.Id, w));
        _removedCustomers.ForEach(c => _customers.Remove(c.Id));
        _pendingCustomers.Clear();
        _pendingVehicles.Clear();
        _pendingWorkOrders.Clear();
        _removedCustomers.Clear();
        SaveCount++;
        return Task.CompletedTask;
    }

    Task<Customer?> ICustomerRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_customers.GetValueOrDefault(id));

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_customers.ContainsKey(id));

    /// <summary>Same matching and ordering as the PostgreSQL repository (ordinal instead of collation order).</summary>
    public Task<PagedList<Customer>> SearchAsync(
        IReadOnlyCollection<string> terms,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var matching = _customers.Values
            .Where(c => terms.All(term =>
                new[] { c.FirstName, c.LastName, c.Email.Value, c.PhoneNumber ?? string.Empty }
                    .Any(field => field.Contains(term, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(c => c.LastName, StringComparer.Ordinal)
            .ThenBy(c => c.FirstName, StringComparer.Ordinal)
            .ThenBy(c => c.Id)
            .ToList();

        return Task.FromResult(new PagedList<Customer>(
            [.. matching.Skip(Paging.Offset(page, pageSize)).Take(pageSize)],
            page,
            pageSize,
            matching.Count));
    }

    public void Add(Customer customer) => _pendingCustomers.Add(customer);

    public void Remove(Customer customer) => _removedCustomers.Add(customer);

    Task<Vehicle?> IVehicleRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_vehicles.GetValueOrDefault(id));

    public Task<bool> ExistsWithVinAsync(Vin vin, CancellationToken cancellationToken = default) =>
        Task.FromResult(_vehicles.Values.Any(v => v.Vin == vin));

    public Task<bool> ExistsForCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_vehicles.Values.Any(v => v.CustomerId == customerId));

    public Task<IReadOnlyList<Vehicle>> ListByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Vehicle>>(
        [.. _vehicles.Values.Where(v => v.CustomerId == customerId).OrderBy(v => v.RegisteredAt).ThenBy(v => v.Id)]);

    public void Add(Vehicle vehicle) => _pendingVehicles.Add(vehicle);

    Task<WorkOrder?> IWorkOrderRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_workOrders.GetValueOrDefault(id));

    Task<bool> IWorkOrderRepository.ExistsForCustomerAsync(Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(_workOrders.Values.Any(w => w.CustomerId == customerId));

    public Task<PagedList<WorkOrder>> ListByVehicleAsync(
        Guid vehicleId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var ofVehicle = _workOrders.Values
            .Where(w => w.VehicleId == vehicleId)
            .OrderByDescending(w => w.CreatedAt)
            .ThenBy(w => w.Id)
            .ToList();

        return Task.FromResult(new PagedList<WorkOrder>(
            [.. ofVehicle.Skip(Paging.Offset(page, pageSize)).Take(pageSize)],
            page,
            pageSize,
            ofVehicle.Count));
    }

    public void Add(WorkOrder workOrder) => _pendingWorkOrders.Add(workOrder);
}

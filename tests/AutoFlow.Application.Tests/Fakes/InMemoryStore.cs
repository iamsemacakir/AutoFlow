using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Vehicles;

namespace AutoFlow.Application.Tests.Fakes;

/// <summary>
/// In-memory stand-in for the database. Like EF Core, added entities become
/// visible only after <see cref="SaveChangesAsync"/>; loaded entities are live
/// references, so mutations are "tracked" automatically.
/// </summary>
public sealed class InMemoryStore : IUnitOfWork, ICustomerRepository, IVehicleRepository
{
    private readonly Dictionary<Guid, Customer> _customers = [];
    private readonly Dictionary<Guid, Vehicle> _vehicles = [];
    private readonly List<Customer> _pendingCustomers = [];
    private readonly List<Vehicle> _pendingVehicles = [];

    public int SaveCount { get; private set; }

    public IReadOnlyCollection<Customer> Customers => _customers.Values;

    public IReadOnlyCollection<Vehicle> Vehicles => _vehicles.Values;

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

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        _pendingCustomers.ForEach(c => _customers.Add(c.Id, c));
        _pendingVehicles.ForEach(v => _vehicles.Add(v.Id, v));
        _pendingCustomers.Clear();
        _pendingVehicles.Clear();
        SaveCount++;
        return Task.CompletedTask;
    }

    Task<Customer?> ICustomerRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_customers.GetValueOrDefault(id));

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_customers.ContainsKey(id));

    public void Add(Customer customer) => _pendingCustomers.Add(customer);

    Task<Vehicle?> IVehicleRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_vehicles.GetValueOrDefault(id));

    public Task<bool> ExistsWithVinAsync(Vin vin, CancellationToken cancellationToken = default) =>
        Task.FromResult(_vehicles.Values.Any(v => v.Vin == vin));

    public Task<IReadOnlyList<Vehicle>> ListByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Vehicle>>([.. _vehicles.Values.Where(v => v.CustomerId == customerId)]);

    public void Add(Vehicle vehicle) => _pendingVehicles.Add(vehicle);
}

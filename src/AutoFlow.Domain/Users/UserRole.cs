namespace AutoFlow.Domain.Users;

/// <summary>What a member of staff may do in AutoFlow. Stored by name.</summary>
public enum UserRole
{
    /// <summary>Runs the workshop: everything, including bays and staff accounts.</summary>
    Owner = 0,

    /// <summary>Front desk: customers, vehicles, appointments, work orders and invoices.</summary>
    ServiceAdvisor = 1,

    /// <summary>Workshop floor: reads the plan and works on work orders.</summary>
    Mechanic = 2,
}

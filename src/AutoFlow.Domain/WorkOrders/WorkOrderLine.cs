using AutoFlow.Domain.Common;

namespace AutoFlow.Domain.WorkOrders;

/// <summary>
/// One charged item on a work order: a block of labour or a part. Lines only
/// exist inside a <see cref="WorkOrder"/>, which creates and removes them.
/// Amounts are net prices in the workshop's currency; VAT is applied when the
/// invoice is issued.
/// </summary>
public sealed class WorkOrderLine : Entity
{
    public const int DescriptionMaxLength = 200;
    public const int PartNumberMaxLength = 40;

    /// <summary>Upper bound for a single line's quantity (hours or units).</summary>
    public const decimal MaxQuantity = 10_000m;

    /// <summary>Upper bound for a unit price or hourly rate.</summary>
    public const decimal MaxUnitPrice = 1_000_000m;

    /// <summary>Quantities are recorded to at most this many decimal places (e.g. 1.25 h, 4.5 l).</summary>
    public const int QuantityDecimals = 2;

    /// <summary>Prices and totals are in whole cents.</summary>
    public const int MoneyDecimals = 2;

    private WorkOrderLine(
        Guid id,
        WorkOrderLineKind kind,
        string description,
        string? partNumber,
        decimal quantity,
        decimal unitPrice)
        : base(id)
    {
        Kind = kind;
        Description = description;
        PartNumber = partNumber;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public WorkOrderLineKind Kind { get; }

    public string Description { get; }

    /// <summary>Manufacturer or supplier part number; always null for labour.</summary>
    public string? PartNumber { get; }

    /// <summary>Hours for labour, units for parts.</summary>
    public decimal Quantity { get; }

    /// <summary>Hourly rate for labour, price per unit for parts (net).</summary>
    public decimal UnitPrice { get; }

    /// <summary>Quantity × unit price, rounded half away from zero to whole cents.</summary>
    public decimal Total => Math.Round(Quantity * UnitPrice, MoneyDecimals, MidpointRounding.AwayFromZero);

    internal static WorkOrderLine Labour(string description, decimal hours, decimal hourlyRate) =>
        new(
            Guid.NewGuid(),
            WorkOrderLineKind.Labour,
            Guard.RequiredText(description, "Description", DescriptionMaxLength),
            partNumber: null,
            EnsureQuantity(hours, "Hours"),
            EnsureUnitPrice(hourlyRate, "Hourly rate"));

    internal static WorkOrderLine Part(string? partNumber, string description, decimal quantity, decimal unitPrice) =>
        new(
            Guid.NewGuid(),
            WorkOrderLineKind.Part,
            Guard.RequiredText(description, "Description", DescriptionMaxLength),
            Guard.OptionalText(partNumber, "Part number", PartNumberMaxLength)?.ToUpperInvariant(),
            EnsureQuantity(quantity, "Quantity"),
            EnsureUnitPrice(unitPrice, "Unit price"));

    private static decimal EnsureQuantity(decimal value, string fieldName)
    {
        if (value <= 0m || value > MaxQuantity)
        {
            throw new DomainException($"{fieldName} must be greater than 0 and at most {MaxQuantity}.");
        }

        if (value != Math.Round(value, QuantityDecimals))
        {
            throw new DomainException($"{fieldName} must have at most {QuantityDecimals} decimal places.");
        }

        return value;
    }

    private static decimal EnsureUnitPrice(decimal value, string fieldName)
    {
        // Zero is allowed: goodwill work and free inspections are still documented.
        if (value < 0m || value > MaxUnitPrice)
        {
            throw new DomainException($"{fieldName} must be between 0 and {MaxUnitPrice}.");
        }

        if (value != Math.Round(value, MoneyDecimals))
        {
            throw new DomainException($"{fieldName} must be in whole cents.");
        }

        return value;
    }
}

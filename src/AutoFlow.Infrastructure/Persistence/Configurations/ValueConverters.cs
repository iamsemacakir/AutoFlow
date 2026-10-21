using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Vehicles;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AutoFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// Store value objects as their normalized string. Reading goes through the
/// domain factory again, so a row edited by hand into an invalid state fails
/// loudly instead of producing an invalid object.
/// </summary>
internal static class ValueConverters
{
    public static readonly ValueConverter<EmailAddress, string> EmailAddress =
        new(email => email.Value, value => Domain.Customers.EmailAddress.Create(value));

    public static readonly ValueConverter<Vin, string> Vin =
        new(vin => vin.Value, value => Domain.Vehicles.Vin.Create(value));

    public static readonly ValueConverter<LicensePlate, string> LicensePlate =
        new(plate => plate.Value, value => Domain.Vehicles.LicensePlate.Create(value));
}

using AutoFlow.Api.Customers;
using AutoFlow.Api.Validation;
using AutoFlow.Api.Vehicles;

namespace AutoFlow.Api.Tests.Validation;

public sealed class ValidationErrorsTests
{
    [Fact]
    public void RequireText_accepts_a_value_whose_trimmed_length_fits()
    {
        var errors = new ValidationErrors();

        errors.RequireText("make", "  VW  ", maxLength: 2);

        Assert.True(errors.IsEmpty);
    }

    [Fact]
    public void Errors_for_the_same_field_are_grouped()
    {
        var errors = new ValidationErrors();

        errors.Add("vin", "first");
        errors.Add("vin", "second");

        Assert.Collection(
            Assert.Single(errors.ToDictionary()).Value,
            message => Assert.Equal("first", message),
            message => Assert.Equal("second", message));
    }

    [Fact]
    public void OptionalText_ignores_missing_and_blank_values()
    {
        var errors = new ValidationErrors();

        errors.OptionalText("phoneNumber", null, 5);
        errors.OptionalText("phoneNumber", "   ", 5);

        Assert.True(errors.IsEmpty);
    }

    [Fact]
    public void Valid_customer_request_has_no_errors()
    {
        var errors = new ValidationErrors();

        new CustomerRequest("Anna", "Schmidt", "anna@example.com", null).Validate(errors);

        Assert.True(errors.IsEmpty);
    }

    [Fact]
    public void Vehicle_request_leaves_vin_format_to_the_domain()
    {
        var errors = new ValidationErrors();

        new VehicleRequest("TOO-SHORT", "M-AB 123", "VW", "Golf", 2019, 0).Validate(errors);

        Assert.True(errors.IsEmpty);
    }

    [Fact]
    public void Mileage_of_zero_is_a_valid_reading()
    {
        var errors = new ValidationErrors();

        new MileageRequest(0).Validate(errors);

        Assert.True(errors.IsEmpty);
    }
}

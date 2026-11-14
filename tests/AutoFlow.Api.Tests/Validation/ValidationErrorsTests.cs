using AutoFlow.Api.Appointments;
using AutoFlow.Api.Customers;
using AutoFlow.Api.Validation;
using AutoFlow.Api.Vehicles;
using AutoFlow.Api.WorkOrders;

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

    [Fact]
    public void OptionalAtLeast_ignores_a_missing_value_and_rejects_one_below_the_minimum()
    {
        var errors = new ValidationErrors();

        errors.OptionalAtLeast("mileageKm", null, 0);
        errors.OptionalAtLeast("mileageKm", 0, 0);
        Assert.True(errors.IsEmpty);

        errors.OptionalAtLeast("mileageKm", -1, 0);
        Assert.Equal("'mileageKm' must be 0 or greater.", Assert.Single(errors.ToDictionary()["mileageKm"]));
    }

    [Fact]
    public void Open_work_order_request_needs_a_complaint_but_no_mileage()
    {
        var withoutMileage = new ValidationErrors();
        new OpenWorkOrderRequest("Brakes squeal", null).Validate(withoutMileage);
        Assert.True(withoutMileage.IsEmpty);

        var withoutComplaint = new ValidationErrors();
        new OpenWorkOrderRequest("  ", -5).Validate(withoutComplaint);
        Assert.Equal(
            "complaint mileageKm",
            string.Join(' ', withoutComplaint.ToDictionary().Keys.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void Line_requests_report_missing_numbers_but_leave_their_range_to_the_domain()
    {
        var missing = new ValidationErrors();
        new LabourLineRequest("Brake pads", null, null).Validate(missing);
        Assert.Equal("hourlyRate hours", string.Join(' ', missing.ToDictionary().Keys.Order(StringComparer.Ordinal)));

        var outOfRange = new ValidationErrors();
        new PartLineRequest(null, "Brake pad set", -1m, 0.001m).Validate(outOfRange);
        Assert.True(outOfRange.IsEmpty);
    }

    [Fact]
    public void Cancel_request_needs_a_reason()
    {
        var errors = new ValidationErrors();

        new CancelWorkOrderRequest("   ").Validate(errors);

        Assert.Equal("reason", Assert.Single(errors.ToDictionary()).Key);
    }

    [Fact]
    public void Booking_request_leaves_the_slot_rules_to_the_domain()
    {
        var errors = new ValidationErrors();
        var start = new DateTimeOffset(2026, 11, 20, 9, 7, 0, TimeSpan.Zero);

        // Off the grid and ending before it starts: both are 422 from the domain.
        new BookAppointmentRequest(Guid.NewGuid(), start, start.AddHours(-1), "Oil change").Validate(errors);

        Assert.True(errors.IsEmpty);
    }

    [Fact]
    public void Reschedule_request_needs_a_bay_and_both_ends_of_the_slot()
    {
        var errors = new ValidationErrors();

        new RescheduleAppointmentRequest(Guid.Empty, null, null).Validate(errors);

        Assert.Equal("bayId end start", string.Join(' ', errors.ToDictionary().Keys.Order(StringComparer.Ordinal)));
    }
}

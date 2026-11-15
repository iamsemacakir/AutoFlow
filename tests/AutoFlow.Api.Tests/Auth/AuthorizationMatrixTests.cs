using AutoFlow.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AutoFlow.Api.Tests.Auth;

/// <summary>
/// Who may call what, read from the endpoints' metadata: the whole access
/// matrix in one table, checked without a database. A new endpoint fails
/// <see cref="Every_named_endpoint_is_in_the_matrix"/> until its policy is
/// decided here.
/// </summary>
public sealed class AuthorizationMatrixTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Anonymous = "Anonymous";
    private const string SignedIn = "SignedIn";

    /// <summary>operationId → required policy.</summary>
    public static TheoryData<string, string> Matrix => new()
    {
        { "GetApiInfo", Anonymous },
        { "SignIn", Anonymous },
        { "GetCurrentUser", SignedIn },
        { "ChangePassword", SignedIn },

        { "ListCustomers", Policies.Staff },
        { "GetCustomer", Policies.Staff },
        { "CreateCustomer", Policies.FrontDesk },
        { "UpdateCustomer", Policies.FrontDesk },
        { "DeleteCustomer", Policies.FrontDesk },

        { "ListCustomerVehicles", Policies.Staff },
        { "GetVehicle", Policies.Staff },
        { "RecordMileage", Policies.Staff },
        { "RegisterVehicle", Policies.FrontDesk },
        { "TransferVehicle", Policies.FrontDesk },

        { "ListVehicleWorkOrders", Policies.Staff },
        { "GetWorkOrder", Policies.Staff },
        { "AddLabourLine", Policies.Staff },
        { "AddPartLine", Policies.Staff },
        { "RemoveWorkOrderLine", Policies.Staff },
        { "StartWorkOrder", Policies.Staff },
        { "CompleteWorkOrder", Policies.Staff },
        { "OpenWorkOrder", Policies.FrontDesk },
        { "ApproveWorkOrder", Policies.FrontDesk },
        { "CancelWorkOrder", Policies.FrontDesk },

        { "ListBays", Policies.Staff },
        { "GetBay", Policies.Staff },
        { "CreateBay", Policies.ManageWorkshop },
        { "RenameBay", Policies.ManageWorkshop },
        { "TakeBayOutOfService", Policies.ManageWorkshop },
        { "ReturnBayToService", Policies.ManageWorkshop },

        { "GetDaySchedule", Policies.Staff },
        { "GetAppointment", Policies.Staff },
        { "BookAppointment", Policies.FrontDesk },
        { "RescheduleAppointment", Policies.FrontDesk },
        { "CheckInAppointment", Policies.FrontDesk },
        { "CancelAppointment", Policies.FrontDesk },
        { "MarkAppointmentNoShow", Policies.FrontDesk },

        { "IssueInvoice", Policies.FrontDesk },
        { "GetWorkOrderInvoice", Policies.FrontDesk },
        { "ListInvoices", Policies.FrontDesk },
        { "GetInvoice", Policies.FrontDesk },
        { "ExportInvoicePdf", Policies.FrontDesk },
        { "RecordPayment", Policies.FrontDesk },

        { "ListUsers", Policies.ManageWorkshop },
        { "GetUser", Policies.ManageWorkshop },
        { "CreateUser", Policies.ManageWorkshop },
        { "ChangeUserRole", Policies.ManageWorkshop },
        { "DeactivateUser", Policies.ManageWorkshop },
        { "ReactivateUser", Policies.ManageWorkshop },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Endpoint_requires_its_policy(string operationId, string policy)
    {
        var endpoints = NamedEndpoints();

        Assert.True(endpoints.TryGetValue(operationId, out var endpoint), $"No endpoint named '{operationId}'.");
        Assert.Equal(policy, PolicyOf(endpoint));
    }

    [Fact]
    public void Every_named_endpoint_is_in_the_matrix()
    {
        var expected = Matrix.Select(row => (string)row[0]!).Order(StringComparer.Ordinal);

        Assert.Equal(expected, NamedEndpoints().Keys.Order(StringComparer.Ordinal));
    }

    private Dictionary<string, Endpoint> NamedEndpoints() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>() is not null)
            .ToDictionary(
                endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()!.EndpointName,
                StringComparer.Ordinal);

    private static string PolicyOf(Endpoint endpoint)
    {
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return Anonymous;
        }

        var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(data => data.Policy ?? SignedIn)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // No metadata at all would fall back to "signed in"; the matrix wants every endpoint explicit.
        return policies.Count == 0 ? "(none)" : string.Join('+', policies);
    }
}

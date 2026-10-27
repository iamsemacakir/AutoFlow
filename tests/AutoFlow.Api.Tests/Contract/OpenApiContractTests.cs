using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Contract;

/// <summary>
/// Pins the published HTTP contract: the OpenAPI document (served in
/// Development) must list exactly these operations with exactly these
/// response statuses. The React client will be generated from this document,
/// so a new endpoint or a changed status must show up here and in the README
/// tables deliberately. Generating the document needs no database.
/// </summary>
public sealed class OpenApiContractTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly HashSet<string> HttpMethods =
        new(["get", "put", "post", "delete", "patch", "head", "options", "trace"], StringComparer.Ordinal);

    /// <summary>operationId → method, path and documented response statuses.</summary>
    public static TheoryData<string, string, string, string> Operations => new()
    {
        { "ListCustomers", "GET", "/api/customers", "200 400" },
        { "GetCustomer", "GET", "/api/customers/{id}", "200 404" },
        { "CreateCustomer", "POST", "/api/customers", "201 400 422" },
        { "UpdateCustomer", "PUT", "/api/customers/{id}", "200 400 404 409 422" },
        { "DeleteCustomer", "DELETE", "/api/customers/{id}", "204 404 409" },
        { "ListCustomerVehicles", "GET", "/api/customers/{customerId}/vehicles", "200 404" },
        { "RegisterVehicle", "POST", "/api/customers/{customerId}/vehicles", "201 400 404 409 422" },
        { "GetVehicle", "GET", "/api/vehicles/{id}", "200 404" },
        { "RecordMileage", "PUT", "/api/vehicles/{id}/mileage", "200 400 404 409 422" },
        { "TransferVehicle", "POST", "/api/vehicles/{id}/transfer", "200 400 404 409 422" },
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Operation_is_documented_with_its_route_and_every_status(
        string operationId, string method, string path, string statuses)
    {
        var operations = await LoadOperationsAsync();

        Assert.True(operations.TryGetValue(operationId, out var operation), $"'{operationId}' is not documented.");
        Assert.Equal((method, path), (operation.Method, operation.Path));
        Assert.Equal(statuses, string.Join(' ', operation.Statuses));
    }

    [Fact]
    public async Task Document_lists_no_operation_missing_from_the_contract()
    {
        var operations = await LoadOperationsAsync();

        var expected = Operations.Select(row => (string)row[0]!).Order(StringComparer.Ordinal);
        Assert.Equal(expected, operations.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Every_operation_has_a_summary_and_a_tag()
    {
        var operations = await LoadOperationsAsync();

        Assert.All(operations.Values, operation =>
        {
            Assert.False(
                string.IsNullOrWhiteSpace(operation.Element.GetProperty("summary").GetString()),
                $"'{operation.Id}' has no summary.");
            Assert.NotEqual(0, operation.Element.GetProperty("tags").GetArrayLength());
        });
    }

    [Fact]
    public async Task Every_error_response_is_documented_as_a_problem()
    {
        var operations = await LoadOperationsAsync();

        var errorResponses = operations.Values
            .SelectMany(operation => operation.Element.GetProperty("responses").EnumerateObject()
                .Where(response => int.Parse(response.Name, System.Globalization.CultureInfo.InvariantCulture) >= 400)
                .Select(response => (Name: $"{operation.Id} {response.Name}", response.Value)))
            .ToList();

        Assert.NotEmpty(errorResponses);
        Assert.All(errorResponses, response => Assert.True(
            response.Value.TryGetProperty("content", out var content)
                && content.TryGetProperty("application/problem+json", out _),
            $"{response.Name} is not documented as application/problem+json."));
    }

    [Theory]
    [InlineData("CustomerDetails", "createdAt email firstName id lastName phoneNumber")]
    [InlineData("VehicleDetails", "customerId id licensePlate make mileageKm model modelYear registeredAt vin")]
    public async Task Read_models_are_documented_with_camel_case_properties(string schema, string properties)
    {
        using var document = await LoadDocumentAsync();

        var documented = document.RootElement
            .GetProperty("components").GetProperty("schemas").GetProperty(schema).GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(properties, string.Join(' ', documented));
    }

    private async Task<JsonDocument> LoadDocumentAsync()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(Url("/openapi/v1.json"));
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private async Task<Dictionary<string, Operation>> LoadOperationsAsync()
    {
        using var document = await LoadDocumentAsync();
        var operations = new Dictionary<string, Operation>(StringComparer.Ordinal);

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var entry in path.Value.EnumerateObject().Where(entry => HttpMethods.Contains(entry.Name)))
            {
                var element = entry.Value.Clone();
                var id = element.TryGetProperty("operationId", out var operationId)
                    ? operationId.GetString() ?? string.Empty
                    : $"<unnamed {entry.Name} {path.Name}>";
                var statuses = element.GetProperty("responses").EnumerateObject()
                    .Select(response => response.Name)
                    .Order(StringComparer.Ordinal)
                    .ToList();

                // A group mapped at "/" yields "/api/customers/"; clients call it without the slash.
                var normalizedPath = path.Name.Length > 1 ? path.Name.TrimEnd('/') : path.Name;

                operations.Add(id, new Operation(id, entry.Name.ToUpperInvariant(), normalizedPath, statuses, element));
            }
        }

        return operations;
    }

    private sealed record Operation(string Id, string Method, string Path, IReadOnlyList<string> Statuses, JsonElement Element);
}

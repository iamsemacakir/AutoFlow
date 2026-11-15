using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using static AutoFlow.Api.Tests.ApiTestHelpers;

namespace AutoFlow.Api.Tests.Contract;

/// <summary>
/// The web client's types are generated from <c>web/openapi.json</c>, a
/// committed copy of the API's OpenAPI document. This test fails when the
/// API's contract changes without the copy, so the client can never drift
/// from the server unnoticed. Set <c>AUTOFLOW_UPDATE_OPENAPI=1</c> to
/// rewrite the copy, then run <c>npm run generate:api</c> in <c>web/</c>.
/// </summary>
public sealed class OpenApiSnapshotTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string UpdateVariable = "AUTOFLOW_UPDATE_OPENAPI";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [Fact]
    public async Task Committed_openapi_document_matches_the_api()
    {
        using var client = factory.CreateClient();
        var live = JsonNode.Parse(await client.GetStringAsync(Url("/openapi/v1.json")))!;
        var path = Path.Combine(RepositoryRoot(), "web", "openapi.json");

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            await File.WriteAllTextAsync(path, live.ToJsonString(Indented) + "\n");
        }

        Assert.True(File.Exists(path), $"{path} is missing; run the tests with {UpdateVariable}=1 to create it.");
        var committed = JsonNode.Parse(await File.ReadAllTextAsync(path));
        Assert.True(
            JsonNode.DeepEquals(committed, live),
            $"web/openapi.json is out of date. Run the API tests with {UpdateVariable}=1, then `npm run generate:api` in web/.");
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AutoFlow.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("AutoFlow.slnx not found above the test output directory.");
    }
}

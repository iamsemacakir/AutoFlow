using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AutoFlow.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build an <see cref="AutoFlowDbContext"/> without
/// starting the API. The connection string comes from the
/// <c>AUTOFLOW_CONNECTION_STRING</c> environment variable and defaults to the
/// local database started by <c>docker-compose.yml</c>. Adding a migration
/// never connects; only <c>dotnet ef database update</c> does.
/// </summary>
internal sealed class AutoFlowDbContextFactory : IDesignTimeDbContextFactory<AutoFlowDbContext>
{
    public const string ConnectionStringVariable = "AUTOFLOW_CONNECTION_STRING";

    /// <summary>Matches the credentials in <c>docker-compose.yml</c>; local development only.</summary>
    public const string LocalConnectionString =
        "Host=localhost;Port=5432;Database=autoflow;Username=autoflow;Password=autoflow";

    public AutoFlowDbContext CreateDbContext(string[] args)
    {
        var connectionString = ResolveConnectionString(
            Environment.GetEnvironmentVariable(ConnectionStringVariable));

        var options = new DbContextOptionsBuilder<AutoFlowDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AutoFlowDbContext(options);
    }

    internal static string ResolveConnectionString(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? LocalConnectionString : configured;
}

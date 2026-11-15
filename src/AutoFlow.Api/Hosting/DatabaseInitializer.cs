using AutoFlow.Api.Auth;
using AutoFlow.Application.Users;
using AutoFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoFlow.Api.Hosting;

/// <summary>
/// Prepares the database when the API starts, if configured to: applies
/// pending migrations (<c>Database:MigrateOnStartup</c>, used by the
/// container setup) and creates the first owner from <c>Auth:InitialOwner</c>
/// while no account exists. With neither configured it does not touch the
/// database, so the API can start without one (e.g. to serve its OpenAPI
/// document).
/// </summary>
internal sealed partial class DatabaseInitializer(
    IServiceProvider services,
    IConfiguration configuration,
    AuthOptions auth,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public const string MigrateOnStartupKey = "Database:MigrateOnStartup";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var migrate = configuration.GetValue<bool>(MigrateOnStartupKey);
        var owner = auth.InitialOwner is { Email.Length: > 0 } configured ? configured : null;
        if (!migrate && owner is null)
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();

        if (migrate)
        {
            var db = scope.ServiceProvider.GetRequiredService<AutoFlowDbContext>();
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
            await db.Database.MigrateAsync(cancellationToken);
            LogMigrated(logger, pending);
        }

        if (owner is not null)
        {
            var created = await scope.ServiceProvider.GetRequiredService<EnsureInitialOwnerHandler>().HandleAsync(
                new EnsureInitialOwnerCommand(owner.Email, owner.DisplayName, owner.Password),
                cancellationToken);
            if (created)
            {
                LogOwnerCreated(logger, owner.Email);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Database is up to date ({PendingCount} migrations applied at startup)")]
    private static partial void LogMigrated(ILogger logger, int pendingCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the initial owner account {Email}")]
    private static partial void LogOwnerCreated(ILogger logger, string email);
}

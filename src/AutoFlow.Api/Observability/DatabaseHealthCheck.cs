using AutoFlow.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AutoFlow.Api.Observability;

/// <summary>Ready means the database answers; a load balancer stops sending requests otherwise.</summary>
internal sealed class DatabaseHealthCheck(AutoFlowDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("The database answers.")
            : HealthCheckResult.Unhealthy("The database cannot be reached.");
}

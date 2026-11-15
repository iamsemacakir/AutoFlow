using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AutoFlow.Api.Observability;

/// <summary>
/// Health checks, metrics and traces. Logs are structured by configuration
/// alone (<c>Logging:Console:FormatterName</c> is <c>json</c> outside
/// development). See ADR 0023.
/// </summary>
internal static class ObservabilitySetup
{
    private const string ReadyTag = "ready";

    public static IServiceCollection AddAutoFlowObservability(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<AutoFlowMetrics>();

        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag]);

        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var prometheus = configuration.GetValue<bool>("Metrics:Prometheus");

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: configuration["OTEL_SERVICE_NAME"] ?? "autoflow-api",
                serviceVersion: typeof(ObservabilitySetup).Assembly.GetName().Version?.ToString()))
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("Npgsql", AutoFlowMetrics.MeterName);

                if (prometheus)
                {
                    metrics.AddPrometheusExporter();
                }

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    metrics.AddOtlpExporter();
                }
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options => options.Filter = context => !IsInfrastructure(context.Request.Path))
                    .AddHttpClientInstrumentation()
                    .AddSource("Npgsql");

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter();
                }
            });

        return services;
    }

    /// <summary>
    /// <c>/health/live</c> answers while the process runs; <c>/health/ready</c>
    /// also checks the database; <c>/health</c> is kept as the liveness check
    /// it always was. <c>/metrics</c> serves Prometheus when enabled.
    /// </summary>
    public static WebApplication MapAutoFlowObservability(this WebApplication app)
    {
        var live = new HealthCheckOptions { Predicate = _ => false };
        app.MapHealthChecks("/health", live).AllowAnonymous();
        app.MapHealthChecks("/health/live", live).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) })
            .AllowAnonymous();

        if (app.Configuration.GetValue<bool>("Metrics:Prometheus"))
        {
            app.MapPrometheusScrapingEndpoint().AllowAnonymous();
        }

        return app;
    }

    private static bool IsInfrastructure(PathString path) =>
        path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/metrics", StringComparison.OrdinalIgnoreCase);
}

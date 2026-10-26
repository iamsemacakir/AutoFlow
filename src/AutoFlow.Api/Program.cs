using AutoFlow.Api.Customers;
using AutoFlow.Api.Errors;
using AutoFlow.Api.Vehicles;
using AutoFlow.Application;
using AutoFlow.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("AutoFlow");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'AutoFlow' is not configured. Set ConnectionStrings__AutoFlow " +
        "(appsettings.Development.json points at the docker-compose database).");
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsConventions.Apply);
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<ConflictExceptionHandler>();
builder.Services.AddHealthChecks();

// Unbindable requests (malformed JSON, wrong content type) throw in every
// environment, so BadRequestExceptionHandler answers them the same way everywhere.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

var app = builder.Build();

app.UseExceptionHandler();

// Bodiless 4xx/5xx answers (unmatched route, wrong method) become problem responses too.
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");

app.MapCustomerEndpoints();
app.MapVehicleEndpoints();

app.MapGet("/", () => Results.Ok(new { name = "AutoFlow API", status = "running" }))
    .WithName("GetApiInfo")
    .ExcludeFromDescription();

app.Run();

/// <summary>Exposed so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program;

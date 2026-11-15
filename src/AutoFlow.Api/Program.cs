using System.Text.Json.Serialization;
using AutoFlow.Api.Appointments;
using AutoFlow.Api.Auth;
using AutoFlow.Api.Bays;
using AutoFlow.Api.Customers;
using AutoFlow.Api.Errors;
using AutoFlow.Api.Hosting;
using AutoFlow.Api.Invoices;
using AutoFlow.Api.Users;
using AutoFlow.Api.Vehicles;
using AutoFlow.Api.WorkOrders;
using AutoFlow.Application;
using AutoFlow.Application.Invoices;
using AutoFlow.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("AutoFlow");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'AutoFlow' is not configured. Set ConnectionStrings__AutoFlow " +
        "(appsettings.Development.json points at the docker-compose database).");
}

// Invalid invoicing settings stop the host here rather than failing the first invoice.
var invoicing = builder.Configuration.GetSection(InvoicingSettings.SectionName).Get<InvoicingSettings>()
    ?? InvoicingSettings.Default;
builder.Services.AddSingleton(invoicing.Validated());

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);

// A missing or short signing key stops the host here, like a missing connection string.
builder.Services.AddAutoFlowAuthentication(builder.Configuration);
builder.Services.AddAuthorizationBuilder().AddAutoFlowPolicies();
builder.Services.AddHostedService<DatabaseInitializer>();

// Numbers are numbers: no "42" strings in requests, and the OpenAPI document
// (and the client generated from it) says `number`, not `number | string`.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
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

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapHealthChecks("/health").AllowAnonymous();

app.MapCustomerEndpoints();
app.MapVehicleEndpoints();
app.MapWorkOrderEndpoints();
app.MapBayEndpoints();
app.MapAppointmentEndpoints();
app.MapInvoiceEndpoints();
app.MapAuthEndpoints();
app.MapUserEndpoints();

app.MapGet("/", () => Results.Ok(new { name = "AutoFlow API", status = "running" }))
    .AllowAnonymous()
    .WithName("GetApiInfo")
    .ExcludeFromDescription();

app.Run();

/// <summary>Exposed so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program;

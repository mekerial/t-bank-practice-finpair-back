using FinPair.Common;
using FinPair.CoupleService;
using FinPair.CoupleService.Couple;
using FinPair.CoupleService.Stores;
using FinPair.Infrastructure;
using FinPair.Infrastructure.Auth;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
const string CorsPolicyName = "FinPairCors";

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        policy.WithOrigins(GetAllowedOrigins(builder.Configuration))
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddFinPairSwagger("FinPair.CoupleService");
builder.Services.AddFinPairPersistence(builder.Configuration);
builder.Services.AddFinPairAccessTokenAuth(builder.Configuration);
builder.Services.AddSingleton<CoupleRepository>();
builder.Services.AddSingleton<HouseholdStore>();

var app = builder.Build();

app.UseFinPairOperationalLogging("FinPair.CoupleService");
await app.EnsureFinPairDatabaseReadyAsync<CoupleRepository>(
    "FinPair.CoupleService",
    (repository, cancellationToken) => repository.EnsureSchemaAsync(cancellationToken));

if (app.Environment.IsDevelopment())
{
    app.UseFinPairSwaggerUi("FinPair.CoupleService v1");
    app.MapGet("/dev/db-ping", async (NpgsqlDataSource dataSource) =>
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        var one = await command.ExecuteScalarAsync();
        return Results.Ok(new { ok = true, scalar = one });
    })
        .WithName("CoupleDbPing")
        .WithTags("Diagnostics")
        .WithSummary("Check couple database connectivity")
        .WithDescription("Development-only endpoint that verifies the couple service can execute a simple PostgreSQL query.")
        .Produces(StatusCodes.Status200OK);
}

app.UseHttpsRedirection();
app.UseCors(CorsPolicyName);
app.UseFinPairBearerAuth();

app.MapGet("/", () => Results.Ok(new { service = "FinPair.CoupleService", product = "FinPair" }))
    .WithName("Root")
    .WithTags("Service")
    .WithSummary("Get couple service info")
    .WithDescription("Returns basic service identity information for the couple service.")
    .Produces(StatusCodes.Status200OK);
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health")
    .WithTags("Diagnostics")
    .WithSummary("Get couple service health")
    .WithDescription("Returns the current health status of the couple service.")
    .Produces(StatusCodes.Status200OK);
app.MapHouseholdEndpoints();
app.MapCoupleEndpoints();

await app.RunAsync();

static string[] GetAllowedOrigins(IConfiguration configuration)
{
    var configuredOrigins = configuration.GetSection("Cors:AllowedOrigins")
        .GetChildren()
        .Select(origin => origin.Value)
        .Where(origin => !string.IsNullOrWhiteSpace(origin))
        .Cast<string>()
        .ToArray();

    return configuredOrigins.Length > 0
        ? configuredOrigins
        : ["http://localhost:5173", "http://localhost:3000", "http://localhost:4200"];
}

using FinPair.Common;
using FinPair.AnalyticsService.Analytics;
using FinPair.Infrastructure;
using FinPair.Infrastructure.Auth;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFinPairSwagger("FinPair.AnalyticsService");
builder.Services.AddFinPairPersistence(builder.Configuration);
builder.Services.AddFinPairAccessTokenAuth(builder.Configuration);
builder.Services.AddSingleton<AnalyticsRepository>();

var app = builder.Build();

app.UseFinPairOperationalLogging("FinPair.AnalyticsService");
await app.EnsureFinPairDatabaseReadyAsync<AnalyticsRepository>(
    "FinPair.AnalyticsService",
    (repository, cancellationToken) => repository.EnsureSchemaAsync(cancellationToken));

if (app.Environment.IsDevelopment())
{
    app.UseFinPairSwaggerUi("FinPair.AnalyticsService v1");
    app.MapGet("/dev/db-ping", async (NpgsqlDataSource dataSource) =>
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        var one = await command.ExecuteScalarAsync();
        return Results.Ok(new { ok = true, scalar = one });
    })
        .WithName("AnalyticsDbPing")
        .WithTags("Diagnostics")
        .WithSummary("Check analytics database connectivity")
        .WithDescription("Development-only endpoint that verifies the analytics service can execute a simple PostgreSQL query.")
        .Produces(StatusCodes.Status200OK);
}

app.UseHttpsRedirection();
app.UseFinPairBearerAuth();

app.MapGet("/", () => Results.Ok(new { service = "FinPair.AnalyticsService", product = "FinPair" }))
    .WithName("Root")
    .WithTags("Service")
    .WithSummary("Get analytics service info")
    .WithDescription("Returns basic service identity information for the analytics service.")
    .Produces(StatusCodes.Status200OK);
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health")
    .WithTags("Diagnostics")
    .WithSummary("Get analytics service health")
    .WithDescription("Returns the current health status of the analytics service.")
    .Produces(StatusCodes.Status200OK);
app.MapAnalyticsEndpoints();

await app.RunAsync();

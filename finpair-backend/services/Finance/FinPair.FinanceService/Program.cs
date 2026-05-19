using FinPair.Common;
using FinPair.FinanceService;
using FinPair.FinanceService.Finance;
using FinPair.FinanceService.Stores;
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
builder.Services.AddFinPairSwagger("FinPair.FinanceService");
builder.Services.AddFinPairPersistence(builder.Configuration);
builder.Services.AddFinPairAccessTokenAuth(builder.Configuration);
builder.Services.AddSingleton<FinanceRepository>();
builder.Services.AddSingleton<TransactionStore>();

var app = builder.Build();

app.UseFinPairOperationalLogging("FinPair.FinanceService");
await app.EnsureFinPairDatabaseReadyAsync<FinanceRepository>(
    "FinPair.FinanceService",
    (repository, cancellationToken) => repository.EnsureSchemaAsync(cancellationToken));

if (app.Environment.IsDevelopment())
{
    app.UseFinPairSwaggerUi("FinPair.FinanceService v1");
    app.MapGet("/dev/db-ping", async (NpgsqlDataSource dataSource) =>
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        var one = await command.ExecuteScalarAsync();
        return Results.Ok(new { ok = true, scalar = one });
    })
        .WithName("FinanceDbPing")
        .WithTags("Diagnostics")
        .WithSummary("Check finance database connectivity")
        .WithDescription("Development-only endpoint that verifies the finance service can execute a simple PostgreSQL query.")
        .Produces(StatusCodes.Status200OK);
}

app.UseHttpsRedirection();
app.UseCors(CorsPolicyName);
app.UseFinPairBearerAuth();

app.MapGet("/", () => Results.Ok(new { service = "FinPair.FinanceService", product = "FinPair" }))
    .WithName("Root")
    .WithTags("Service")
    .WithSummary("Get finance service info")
    .WithDescription("Returns basic service identity information for the finance service.")
    .Produces(StatusCodes.Status200OK);
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health")
    .WithTags("Diagnostics")
    .WithSummary("Get finance service health")
    .WithDescription("Returns the current health status of the finance service.")
    .Produces(StatusCodes.Status200OK);
app.MapTransactionEndpoints();
app.MapFinanceEndpoints();

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

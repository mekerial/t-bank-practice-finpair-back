using FinPair.Common;
using FinPair.Infrastructure;
using FinPair.Infrastructure.Auth;
using FinPair.SupportService.Support;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFinPairSwagger("FinPair.SupportService");
builder.Services.AddFinPairPersistence(builder.Configuration);
builder.Services.AddFinPairAccessTokenAuth(builder.Configuration);
builder.Services.AddSingleton<SupportRepository>();

var app = builder.Build();

app.UseFinPairOperationalLogging("FinPair.SupportService");
await app.EnsureFinPairDatabaseReadyAsync<SupportRepository>(
    "FinPair.SupportService",
    (repository, cancellationToken) => repository.EnsureSchemaAsync(cancellationToken));

if (app.Environment.IsDevelopment())
{
    app.UseFinPairSwaggerUi("FinPair.SupportService v1");
    app.MapGet("/dev/db-ping", async (NpgsqlDataSource dataSource) =>
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        var one = await command.ExecuteScalarAsync();
        return Results.Ok(new { ok = true, scalar = one });
    });
}

app.UseHttpsRedirection();
app.UseFinPairBearerAuth();

app.MapGet("/", () => Results.Ok(new { service = "FinPair.SupportService", product = "FinPair" }))
    .WithName("Root");
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health");
app.MapSupportEndpoints();

await app.RunAsync();

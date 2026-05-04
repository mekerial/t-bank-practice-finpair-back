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

await using (var scope = app.Services.CreateAsyncScope())
{
    var repository = scope.ServiceProvider.GetRequiredService<AnalyticsRepository>();
    await repository.EnsureSchemaAsync();
}

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
    });
}

app.UseHttpsRedirection();
app.UseFinPairBearerAuth();

app.MapGet("/", () => Results.Ok(new { service = "FinPair.AnalyticsService", product = "FinPair" }))
    .WithName("Root");
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health");
app.MapAnalyticsEndpoints();

await app.RunAsync();

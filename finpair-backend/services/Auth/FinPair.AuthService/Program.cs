using FinPair.Common;
using FinPair.Infrastructure;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFinPairSwagger("FinPair.AuthService");
builder.Services.AddFinPairPersistence(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseFinPairSwaggerUi("FinPair.AuthService v1");
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

app.MapGet("/", () => Results.Ok(new { service = "FinPair.AuthService", product = "FinPair" }))
    .WithName("Root");
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health");

app.Run();

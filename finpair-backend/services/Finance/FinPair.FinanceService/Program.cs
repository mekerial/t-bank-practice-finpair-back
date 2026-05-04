using FinPair.Common;
using FinPair.FinanceService.Finance;
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

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var repository = scope.ServiceProvider.GetRequiredService<FinanceRepository>();
    await repository.EnsureSchemaAsync();
}

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
    });
}

app.UseHttpsRedirection();
app.UseFinPairBearerAuth();

app.MapGet("/", () => Results.Ok(new { service = "FinPair.FinanceService", product = "FinPair" }))
    .WithName("Root");
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health");
app.MapFinanceEndpoints();

await app.RunAsync();

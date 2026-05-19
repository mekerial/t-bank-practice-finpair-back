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
builder.Services.Configure<SupportAiOptions>(builder.Configuration.GetSection("SupportAi"));
builder.Services.AddHttpClient<ISupportChatClient, OllamaChatClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SupportAiOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 180));
});

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
    })
        .WithName("SupportDbPing")
        .WithTags("Diagnostics")
        .WithSummary("Check support database connectivity")
        .WithDescription("Development-only endpoint that verifies the support service can execute a simple PostgreSQL query.")
        .Produces(StatusCodes.Status200OK);
}

app.UseHttpsRedirection();
app.UseFinPairBearerAuth();

app.MapGet("/", () => Results.Ok(new { service = "FinPair.SupportService", product = "FinPair" }))
    .WithName("Root")
    .WithTags("Service")
    .WithSummary("Get support service info")
    .WithDescription("Returns basic service identity information for the support service.")
    .Produces(StatusCodes.Status200OK);
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health")
    .WithTags("Diagnostics")
    .WithSummary("Get support service health")
    .WithDescription("Returns the current health status of the support service.")
    .Produces(StatusCodes.Status200OK);
app.MapSupportEndpoints();

await app.RunAsync();

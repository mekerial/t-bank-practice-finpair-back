using FinPair.AuthService.Auth;
using FinPair.Common;
using FinPair.Infrastructure;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
const string CorsPolicyName = "FinPairCors";

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        policy.WithOrigins(GetAllowedOrigins(builder.Configuration))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddFinPairSwagger("FinPair.AuthService");
builder.Services.AddFinPairPersistence(builder.Configuration);
builder.Services.AddSingleton<AuthRepository>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<JwtTokenService>();

var app = builder.Build();

app.UseFinPairOperationalLogging("FinPair.AuthService");
await app.EnsureFinPairDatabaseReadyAsync<AuthRepository>(
    "FinPair.AuthService",
    (repository, cancellationToken) => repository.EnsureSchemaAsync(cancellationToken));

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
    })
        .WithName("AuthDbPing")
        .WithTags("Diagnostics")
        .WithSummary("Check auth database connectivity")
        .WithDescription("Development-only endpoint that verifies the auth service can execute a simple PostgreSQL query.")
        .Produces(StatusCodes.Status200OK);
}

app.UseHttpsRedirection();
app.UseCors(CorsPolicyName);

app.Use(async (context, next) =>
{
    var authorization = context.Request.Headers.Authorization.ToString();
    if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        var token = authorization["Bearer ".Length..].Trim();
        var tokenService = context.RequestServices.GetRequiredService<JwtTokenService>();
        if (tokenService.TryValidateAccessToken(token, out var principal))
        {
            context.User = principal;
        }
    }

    await next();
});

app.MapGet("/", () => Results.Ok(new { service = "FinPair.AuthService", product = "FinPair" }))
    .WithName("Root")
    .WithTags("Service")
    .WithSummary("Get auth service info")
    .WithDescription("Returns basic service identity information for the auth service.")
    .Produces(StatusCodes.Status200OK);
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health")
    .WithTags("Diagnostics")
    .WithSummary("Get auth service health")
    .WithDescription("Returns the current health status of the auth service.")
    .Produces(StatusCodes.Status200OK);
app.MapAuthEndpoints();

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

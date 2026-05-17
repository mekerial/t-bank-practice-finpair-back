using FinPair.ApiGateway.Proxy;
using FinPair.Common;

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
builder.Services.AddFinPairSwagger("FinPair.ApiGateway");
builder.Services.AddSingleton(new HttpClient(new HttpClientHandler { UseCookies = false })
{
    Timeout = TimeSpan.FromSeconds(120)
});

var app = builder.Build();

app.UseFinPairOperationalLogging("FinPair.ApiGateway");

if (app.Environment.IsDevelopment())
{
    app.UseFinPairSwaggerUi("FinPair.ApiGateway v1");
}

app.UseHttpsRedirection();
app.UseCors(CorsPolicyName);

app.MapGet("/", () => Results.Ok(new { service = "FinPair.ApiGateway", product = "FinPair" }))
    .WithName("Root");
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health");

MapServiceProxy(app, "auth", "Auth", "auth");
MapServiceProxy(app, "couple", "Couple", "couple");
MapHouseholdTransactionsProxy(app);
MapServiceProxy(app, "households", "Couple", "households");
MapServiceProxy(app, "users", "Finance", "users");
MapServiceProxy(app, "settings", "Finance", "settings");
MapServiceProxy(app, "finance", "Finance", "finance");
MapServiceProxy(app, "transactions", "Finance", "transactions");
MapServiceProxy(app, "categories", "Finance", "categories");
MapServiceProxy(app, "goals", "Goals", "goals");
MapServiceProxy(app, "analytics", "Analytics", "analytics");
MapServiceProxy(app, "support", "Support", "support");

app.Run();

static void MapHouseholdTransactionsProxy(WebApplication app)
{
    string[] methods = ["GET", "POST", "PATCH", "PUT", "DELETE", "OPTIONS"];

    app.MapMethods(
            "/api/v1/households/{householdId:guid}/transactions",
            methods,
            (HttpContext context, Guid householdId, IConfiguration configuration, HttpClient httpClient, CancellationToken cancellationToken) =>
                ServiceProxy.ForwardAsync(
                    context,
                    "Finance",
                    $"households/{householdId}/transactions",
                    null,
                    configuration,
                    httpClient,
                    cancellationToken))
        .WithName("FinanceHouseholdTransactionsRootProxy")
        .WithTags("Finance");

    app.MapMethods(
            "/api/v1/households/{householdId:guid}/transactions/{**path}",
            methods,
            (HttpContext context, Guid householdId, string? path, IConfiguration configuration, HttpClient httpClient, CancellationToken cancellationToken) =>
                ServiceProxy.ForwardAsync(
                    context,
                    "Finance",
                    $"households/{householdId}/transactions",
                    path,
                    configuration,
                    httpClient,
                    cancellationToken))
        .WithName("FinanceHouseholdTransactionsProxy")
        .WithTags("Finance");
}

static void MapServiceProxy(WebApplication app, string routePrefix, string serviceKey, string targetSegment)
{
    string[] methods = ["GET", "POST", "PATCH", "PUT", "DELETE", "OPTIONS"];

    app.MapMethods(
            $"/api/v1/{routePrefix}",
            methods,
            (HttpContext context, IConfiguration configuration, HttpClient httpClient, CancellationToken cancellationToken) =>
                ServiceProxy.ForwardAsync(context, serviceKey, targetSegment, null, configuration, httpClient, cancellationToken))
        .WithName($"{serviceKey}{routePrefix}RootProxy")
        .WithTags(serviceKey);

    app.MapMethods(
            $"/api/v1/{routePrefix}/{{**path}}",
            methods,
            (HttpContext context, string? path, IConfiguration configuration, HttpClient httpClient, CancellationToken cancellationToken) =>
                ServiceProxy.ForwardAsync(context, serviceKey, targetSegment, path, configuration, httpClient, cancellationToken))
        .WithName($"{serviceKey}{routePrefix}Proxy")
        .WithTags(serviceKey);
}

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

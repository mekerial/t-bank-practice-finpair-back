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
    .WithName("Root")
    .WithTags("Service")
    .WithSummary("Get API gateway info")
    .WithDescription("Returns basic service identity information for the API gateway.")
    .Produces(StatusCodes.Status200OK);
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health")
    .WithTags("Diagnostics")
    .WithSummary("Get API gateway health")
    .WithDescription("Returns the current health status of the API gateway.")
    .Produces(StatusCodes.Status200OK);

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
        .WithTags("Finance")
        .WithSummary("Proxy household transactions")
        .WithDescription("Forwards household transaction requests to the finance service while preserving method, headers, query string and body.");

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
        .WithTags("Finance")
        .WithSummary("Proxy household transaction subpath")
        .WithDescription("Forwards nested household transaction requests to the finance service while preserving method, headers, query string and body.");
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
        .WithTags(serviceKey)
        .WithSummary($"Proxy {routePrefix} requests to {serviceKey}")
        .WithDescription($"Forwards /api/v1/{routePrefix} requests to the {serviceKey} service target segment '{targetSegment}'.");

    app.MapMethods(
            $"/api/v1/{routePrefix}/{{**path}}",
            methods,
            (HttpContext context, string? path, IConfiguration configuration, HttpClient httpClient, CancellationToken cancellationToken) =>
                ServiceProxy.ForwardAsync(context, serviceKey, targetSegment, path, configuration, httpClient, cancellationToken))
        .WithName($"{serviceKey}{routePrefix}Proxy")
        .WithTags(serviceKey)
        .WithSummary($"Proxy {routePrefix} subpath requests to {serviceKey}")
        .WithDescription($"Forwards /api/v1/{routePrefix}/{{path}} requests to the {serviceKey} service target segment '{targetSegment}' while preserving the remaining path.");
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

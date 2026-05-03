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
builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });

var app = builder.Build();

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
app.MapMethods(
    "/api/v1/auth/{**path}",
    ["GET", "POST", "PATCH", "PUT", "DELETE", "OPTIONS"],
    AuthProxy.ForwardAsync)
    .WithName("AuthProxy")
    .WithTags("Auth");

app.Run();

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

using FinPair.Common;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFinPairSwagger("FinPair.ApiGateway");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseFinPairSwaggerUi("FinPair.ApiGateway v1");
}

app.UseHttpsRedirection();

app.MapGet("/", () => Results.Ok(new { service = "FinPair.ApiGateway", product = "FinPair" }))
    .WithName("Root");
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health");

app.Run();

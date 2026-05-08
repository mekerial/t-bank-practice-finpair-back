namespace FinPair.AnalyticsService.Tests;

using FinPair.AnalyticsService.Analytics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

public class AnalyticsEndpointTests
{
    [Fact]
    public void MapAnalyticsEndpoints_RegistersAllAnalyticsContractRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<AnalyticsRepository>(_ => null!);
        var app = builder.Build();

        app.MapAnalyticsEndpoints();

        var routes = GetRouteTexts(app);

        Assert.Contains("/api/v1/analytics/summary", routes);
        Assert.Contains("/api/v1/analytics/categories", routes);
        Assert.Contains("/api/v1/analytics/dynamics", routes);
        Assert.Contains("/api/v1/analytics/insights", routes);
        Assert.Contains("/api/v1/analytics/overview", routes);
    }

    [Fact]
    public void SummaryResult_PreservesFinancialLoadSnapshot()
    {
        var summary = new SummaryResult(1420000m, 845000m, 575000m, 59.5m);

        Assert.Equal(1420000m, summary.Income);
        Assert.Equal(845000m, summary.Expenses);
        Assert.Equal(575000m, summary.Balance);
        Assert.Equal(59.5m, summary.LoadPercent);
    }

    [Fact]
    public void OverviewResult_CarriesTrendAndCategoryDistribution()
    {
        var userId = Guid.NewGuid();
        var date = new DateOnly(2026, 4, 10);
        var overview = new OverviewResult(
            12000m,
            new CategoryAmount("products", 95000m),
            40.5m,
            59.5m,
            [new UserAmount(userId, 500000m)],
            [new DateAmount(date, 12000m)],
            [new CategoryAmount("products", 95000m)]);

        Assert.Equal("products", overview.TopCategory?.Category);
        Assert.Equal(userId, overview.PartnerComparison.Single().UserId);
        Assert.Equal(date, overview.ExpenseTrend.Single().Date);
        Assert.Equal(95000m, overview.CategoryDistribution.Single().Amount);
    }

    private static IReadOnlySet<string?> GetRouteTexts(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToHashSet();
}

using FinPair.Contracts;
using FinPair.Infrastructure.Auth;

namespace FinPair.AnalyticsService.Analytics;

public static class AnalyticsEndpoints
{
    public static RouteGroupBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/analytics").WithTags("Analytics");

        group.MapGet("/summary", GetSummaryAsync)
            .WithName("GetAnalyticsSummary")
            .WithSummary("Get analytics summary")
            .WithDescription("Returns aggregate income, expense, balance and load metrics for the authenticated user's household.")
            .Produces<ApiResponse<SummaryResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        group.MapGet("/categories", GetCategoriesAsync)
            .WithName("GetAnalyticsCategories")
            .WithSummary("Get category analytics")
            .WithDescription("Returns spending or income amounts grouped by category for the authenticated user's household.")
            .Produces<ApiResponse<ItemsResponse<CategoryAmount>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        group.MapGet("/dynamics", GetDynamicsAsync)
            .WithName("GetAnalyticsDynamics")
            .WithSummary("Get monthly dynamics")
            .WithDescription("Returns monthly income and expense dynamics for the authenticated user's household.")
            .Produces<ApiResponse<ItemsResponse<MonthlyDynamicsPoint>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        group.MapGet("/insights", GetInsightsAsync)
            .WithName("GetAnalyticsInsights")
            .WithSummary("Get analytics insights")
            .WithDescription("Returns generated financial insights for the authenticated user's household.")
            .Produces<ApiResponse<ItemsResponse<InsightResult>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        group.MapGet("/overview", GetOverviewAsync)
            .WithName("GetAnalyticsOverview")
            .WithSummary("Get analytics overview")
            .WithDescription("Returns the combined analytics overview used by the dashboard.")
            .Produces<ApiResponse<OverviewResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> GetSummaryAsync(
        HttpContext httpContext,
        AnalyticsRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var summary = await repository.GetSummaryAsync(userId, cancellationToken);
        return summary is null
            ? Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound)
            : Results.Json(ApiResponse<SummaryResult>.Ok(summary));
    }

    private static async Task<IResult> GetCategoriesAsync(
        HttpContext httpContext,
        AnalyticsRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var categories = await repository.GetCategoriesAsync(userId, cancellationToken);
        return categories is null
            ? Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound)
            : Results.Json(ApiResponse<ItemsResponse<CategoryAmount>>.Ok(new ItemsResponse<CategoryAmount>(categories)));
    }

    private static async Task<IResult> GetDynamicsAsync(
        HttpContext httpContext,
        AnalyticsRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var dynamics = await repository.GetDynamicsAsync(userId, cancellationToken);
        return dynamics is null
            ? Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound)
            : Results.Json(ApiResponse<ItemsResponse<MonthlyDynamicsPoint>>.Ok(new ItemsResponse<MonthlyDynamicsPoint>(dynamics)));
    }

    private static async Task<IResult> GetInsightsAsync(
        HttpContext httpContext,
        AnalyticsRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var insights = await repository.GetInsightsAsync(userId, cancellationToken);
        return insights is null
            ? Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound)
            : Results.Json(ApiResponse<ItemsResponse<InsightResult>>.Ok(new ItemsResponse<InsightResult>(insights)));
    }

    private static async Task<IResult> GetOverviewAsync(
        HttpContext httpContext,
        AnalyticsRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var overview = await repository.GetOverviewAsync(userId, cancellationToken);
        return overview is null
            ? Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound)
            : Results.Json(ApiResponse<OverviewResult>.Ok(overview));
    }

    private static IResult Unauthorized() =>
        Error("UNAUTHORIZED", "Bearer access token is required.", StatusCodes.Status401Unauthorized);

    private static IResult Error(
        string code,
        string message,
        int statusCode,
        IReadOnlyDictionary<string, string[]>? details = null) =>
        Results.Json(ApiResponse<object>.Fail(code, message, details), statusCode: statusCode);
}

namespace FinPair.GoalService.Tests;

using System.Reflection;
using FinPair.GoalService.Goals;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

public class GoalEndpointTests
{
    [Fact]
    public void MapGoalEndpoints_RegistersGoalCrudAndContributionRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<GoalRepository>(_ => null!);
        var app = builder.Build();

        app.MapGoalEndpoints();

        var routes = GetRouteTexts(app);

        Assert.Contains("/api/v1/goals/", routes);
        Assert.Contains("/api/v1/goals/{goalId:guid}", routes);
        Assert.Contains("/api/v1/goals/{goalId:guid}/contributions", routes);
        Assert.Contains("/api/v1/goals/{goalId:guid}/contribute", routes);
    }

    [Fact]
    public void ValidateCreateGoal_RejectsMissingTitleAndInvalidAmounts()
    {
        var request = new CreateGoalRequest(null, 100m, 150m, -1m, null, true);

        var errors = InvokeEndpointValidation("ValidateCreateGoal", request);

        Assert.Contains("title", errors.Keys);
        Assert.Contains("monthlyContribution", errors.Keys);
        Assert.Contains("currentAmount", errors.Keys);
    }

    [Fact]
    public void ValidateUpdateGoal_RejectsEmptyPatch()
    {
        var request = new UpdateGoalRequest(null, null, null, null, null, null);

        var errors = InvokeEndpointValidation("ValidateUpdateGoal", request);

        Assert.Contains("request", errors.Keys);
    }

    [Fact]
    public void ValidateContribution_RequiresPositiveAmount()
    {
        var request = new AddGoalContributionRequest(0m, new DateOnly(2026, 4, 20));

        var errors = InvokeEndpointValidation("ValidateContribution", request);

        Assert.Contains("amount", errors.Keys);
    }

    [Fact]
    public void ToDto_ClampsProgressAndRemainingAmount()
    {
        var goal = new GoalRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Vacation",
            100m,
            150m,
            10m,
            null,
            true);

        var dto = InvokeToDto(goal);

        Assert.Equal(100m, dto.ProgressPercent);
        Assert.Equal(0m, dto.RemainingAmount);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), dto.ForecastDate);
    }

    [Fact]
    public void ToDto_ReturnsNoForecastWhenContributionIsZero()
    {
        var goal = new GoalRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "House",
            1000m,
            250m,
            0m,
            null,
            true);

        var dto = InvokeToDto(goal);

        Assert.Equal(25m, dto.ProgressPercent);
        Assert.Equal(750m, dto.RemainingAmount);
        Assert.Null(dto.ForecastDate);
    }

    private static IReadOnlyDictionary<string, string[]> InvokeEndpointValidation(string methodName, object request)
    {
        var method = typeof(GoalEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(GoalEndpoints), methodName);

        return (IReadOnlyDictionary<string, string[]>)method.Invoke(null, [request])!;
    }

    private static GoalDto InvokeToDto(GoalRecord goal)
    {
        var method = typeof(GoalRepository).GetMethod("ToDto", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(GoalRepository), "ToDto");

        return (GoalDto)method.Invoke(null, [goal])!;
    }

    private static IReadOnlySet<string?> GetRouteTexts(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToHashSet();
}

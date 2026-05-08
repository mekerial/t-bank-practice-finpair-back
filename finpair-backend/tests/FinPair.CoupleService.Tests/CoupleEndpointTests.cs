namespace FinPair.CoupleService.Tests;

using System.Reflection;
using FinPair.Contracts.Households;
using FinPair.CoupleService.Couple;
using FinPair.CoupleService.Stores;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

public class CoupleEndpointTests
{
    [Fact]
    public void MapCoupleEndpoints_RegistersContractRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<CoupleRepository>(_ => null!);
        builder.Services.AddSingleton<HouseholdStore>(_ => null!);
        var app = builder.Build();

        app.MapCoupleEndpoints();
        app.MapHouseholdEndpoints();

        var routeTexts = GetRouteTexts(app);

        Assert.Contains("/api/v1/couple/create", routeTexts);
        Assert.Contains("/api/v1/couple/join", routeTexts);
        Assert.Contains("/api/v1/couple/", routeTexts);
        Assert.Contains("/api/v1/couple/split", routeTexts);
        Assert.Contains("/api/v1/couple/settings", routeTexts);
        Assert.Contains("/api/v1/couple/invite-code/regenerate", routeTexts);
        Assert.Contains("/api/v1/households/", routeTexts);
        Assert.Contains("/api/v1/households/{id:guid}", routeTexts);
        Assert.Contains("/api/v1/households/join", routeTexts);
    }

    [Fact]
    public void ValidateJoin_RequiresInviteCode()
    {
        var errors = InvokeCoupleValidation("ValidateJoin", new JoinCoupleRequest(" "));

        Assert.Contains("inviteCode", errors.Keys);
    }

    [Fact]
    public void ValidateSettings_RequiresAtLeastOneSettingForSettingsEndpoint()
    {
        var errors = InvokeCoupleValidation(
            "ValidateSettings",
            new UpdateCoupleSettingsRequest(null, null, null),
            false);

        Assert.Contains("request", errors.Keys);
    }

    [Fact]
    public void ValidateSettings_RequiresSplitTypeForSplitEndpoint()
    {
        var errors = InvokeCoupleValidation(
            "ValidateSettings",
            new UpdateCoupleSettingsRequest(null, null, null),
            true);

        Assert.Contains("splitType", errors.Keys);
    }

    [Fact]
    public void ValidateHouseholdJoin_RejectsMissingBody()
    {
        var errors = InvokeHouseholdValidation("ValidateJoin", (object?)null);

        Assert.Contains("request", errors.Keys);
    }

    [Theory]
    [InlineData("equal")]
    [InlineData("income")]
    [InlineData("income_ratio")]
    [InlineData("custom")]
    public void ValidateSettings_AcceptsSupportedSplitTypes(string splitType)
    {
        var errors = InvokeCoupleValidation(
            "ValidateSettings",
            new UpdateCoupleSettingsRequest(splitType, null, null),
            true);

        Assert.Empty(errors);
    }

    private static IReadOnlyDictionary<string, string[]> InvokeCoupleValidation(string methodName, params object?[] args) =>
        InvokeValidation(typeof(CoupleEndpoints), methodName, args);

    private static IReadOnlyDictionary<string, string[]> InvokeHouseholdValidation(string methodName, params object?[] args) =>
        InvokeValidation(typeof(HouseholdEndpoints), methodName, args);

    private static IReadOnlyDictionary<string, string[]> InvokeValidation(Type type, string methodName, object?[] args)
    {
        var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(type.FullName, methodName);

        return (IReadOnlyDictionary<string, string[]>)method.Invoke(null, args)!;
    }

    private static IReadOnlySet<string?> GetRouteTexts(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToHashSet();
}

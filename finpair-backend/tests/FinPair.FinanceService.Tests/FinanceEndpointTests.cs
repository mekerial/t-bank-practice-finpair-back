namespace FinPair.FinanceService.Tests;

using System.Reflection;
using FinPair.FinanceService;
using FinPair.FinanceService.Finance;
using FinPair.FinanceService.Stores;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

public class FinanceEndpointTests
{
    [Fact]
    public void MapFinanceEndpoints_RegistersCrudRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<FinanceRepository>(_ => null!);
        builder.Services.AddSingleton<TransactionStore>(_ => null!);
        var app = builder.Build();

        app.MapFinanceEndpoints();
        app.MapTransactionEndpoints();

        var routes = GetRouteTexts(app);

        Assert.Contains("/api/v1/users/me", routes);
        Assert.Contains("/api/v1/settings", routes);
        Assert.Contains("/api/v1/finance/profile", routes);
        Assert.Contains("/api/v1/finance/dashboard", routes);
        Assert.Contains("/api/v1/transactions/", routes);
        Assert.Contains("/api/v1/transactions/{transactionId:guid}", routes);
        Assert.Contains("/api/v1/categories/", routes);
        Assert.Contains("/api/v1/categories/{categoryId:guid}", routes);
        Assert.Contains("/api/v1/households/{householdId:guid}/transactions/", routes);
    }

    [Fact]
    public void ValidateCreateTransaction_RequiresCategoryAmountDateAndType()
    {
        var request = new CreateTransactionRequest(null, null, null, null, null, null, null);

        var errors = InvokeValidation("ValidateCreateTransaction", request);

        Assert.Contains("type", errors.Keys);
        Assert.Contains("amount", errors.Keys);
        Assert.Contains("date", errors.Keys);
        Assert.Contains("category", errors.Keys);
    }

    [Fact]
    public void ValidateCreateTransaction_AcceptsCategoryNameWhenCategoryIdIsMissing()
    {
        var request = new CreateTransactionRequest(
            "expense",
            null,
            "products",
            1200.50m,
            "Store",
            null,
            new DateOnly(2026, 4, 10));

        var errors = InvokeValidation("ValidateCreateTransaction", request);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateUpdateTransaction_RejectsEmptyPatch()
    {
        var request = new UpdateTransactionRequest(null, null, null, null, null, null, null);

        var errors = InvokeValidation("ValidateUpdateTransaction", request);

        Assert.Contains("request", errors.Keys);
    }

    [Fact]
    public void ParseTransactionQuery_AcceptsContractAliasesAndDefaults()
    {
        var context = new DefaultHttpContext();
        var userId = Guid.NewGuid();
        context.Request.QueryString = new QueryString(
            $"?type=expense&category=products&user_id={userId}&from=2026-04-01&to=2026-04-30&page=2&pageSize=10&sortBy=amount&sortOrder=asc");

        var (query, errors) = InvokeParseTransactionQuery(context.Request);

        Assert.Empty(errors);
        Assert.NotNull(query);
        Assert.Equal("expense", query!.Type);
        Assert.Equal("products", query.Category);
        Assert.Equal(userId, query.UserId);
        Assert.Equal(new DateOnly(2026, 4, 1), query.From);
        Assert.Equal(new DateOnly(2026, 4, 30), query.To);
        Assert.Equal(2, query.Page);
        Assert.Equal(10, query.PageSize);
        Assert.Equal("amount", query.SortBy);
        Assert.Equal("asc", query.SortOrder);
    }

    [Fact]
    public void ParseTransactionQuery_RejectsInvertedDateRangeAndBadSort()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?from=2026-05-01&to=2026-04-01&sortBy=bad&sortOrder=sideways");

        var (query, errors) = InvokeParseTransactionQuery(context.Request);

        Assert.Null(query);
        Assert.Contains("dateRange", errors.Keys);
        Assert.Contains("sortBy", errors.Keys);
        Assert.Contains("sortOrder", errors.Keys);
    }

    private static IReadOnlyDictionary<string, string[]> InvokeValidation(string methodName, object request)
    {
        var method = typeof(FinPair.FinanceService.Finance.FinanceEndpoints)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(FinPair.FinanceService.Finance.FinanceEndpoints), methodName);

        return (IReadOnlyDictionary<string, string[]>)method.Invoke(null, [request])!;
    }

    private static (TransactionQuery? Query, IReadOnlyDictionary<string, string[]> Errors) InvokeParseTransactionQuery(HttpRequest request)
    {
        var method = typeof(FinPair.FinanceService.Finance.FinanceEndpoints)
            .GetMethod("ParseTransactionQuery", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(FinPair.FinanceService.Finance.FinanceEndpoints), "ParseTransactionQuery");

        return ((TransactionQuery? Query, IReadOnlyDictionary<string, string[]> Errors))method.Invoke(null, [request])!;
    }

    private static IReadOnlySet<string?> GetRouteTexts(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToHashSet();
}

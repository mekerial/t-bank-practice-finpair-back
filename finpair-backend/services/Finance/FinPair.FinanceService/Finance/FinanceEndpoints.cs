using FinPair.Contracts;
using FinPair.Infrastructure.Auth;

namespace FinPair.FinanceService.Finance;

public static class FinanceEndpoints
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "income",
        "expense"
    };

    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");

        api.MapGet("/users/me", GetUserProfileAsync)
            .WithName("GetUserProfile")
            .WithTags("Users");

        api.MapPatch("/users/me", UpdateUserProfileAsync)
            .WithName("UpdateUserProfile")
            .WithTags("Users");

        api.MapGet("/settings", GetSettingsAsync)
            .WithName("GetSettings")
            .WithTags("Settings");

        api.MapPatch("/settings", UpdateSettingsAsync)
            .WithName("UpdateSettings")
            .WithTags("Settings");

        api.MapGet("/finance/profile", GetFinanceProfileAsync)
            .WithName("GetFinanceProfile")
            .WithTags("Finance");

        api.MapPatch("/finance/profile", UpdateFinanceProfileAsync)
            .WithName("UpdateFinanceProfile")
            .WithTags("Finance");

        api.MapGet("/finance/dashboard", GetDashboardAsync)
            .WithName("GetFinanceDashboard")
            .WithTags("Finance");

        MapTransactionRoutes(api.MapGroup("/transactions").WithTags("Transactions"));
        MapTransactionRoutes(api.MapGroup("/finance/transactions").WithTags("Transactions"));
        MapCategoryRoutes(api.MapGroup("/categories").WithTags("Categories"));
        MapCategoryRoutes(api.MapGroup("/finance/categories").WithTags("Categories"));

        return endpoints;
    }

    private static void MapTransactionRoutes(RouteGroupBuilder group)
    {
        group.MapPost("", CreateTransactionAsync)
            .Produces<ApiResponse<TransactionDto>>(StatusCodes.Status201Created);
        group.MapGet("", GetTransactionsAsync)
            .Produces<ApiResponse<PagedItemsResponse<TransactionDto>>>();
        group.MapGet("/{transactionId:guid}", GetTransactionAsync)
            .Produces<ApiResponse<TransactionDto>>();
        group.MapPatch("/{transactionId:guid}", UpdateTransactionAsync)
            .Produces<ApiResponse<TransactionDto>>();
        group.MapDelete("/{transactionId:guid}", DeleteTransactionAsync)
            .Produces(StatusCodes.Status204NoContent);
    }

    private static void MapCategoryRoutes(RouteGroupBuilder group)
    {
        group.MapGet("", GetCategoriesAsync)
            .Produces<ApiResponse<ItemsResponse<CategoryDto>>>();
        group.MapPost("", CreateCategoryAsync)
            .Produces<ApiResponse<CategoryDto>>(StatusCodes.Status201Created);
        group.MapPatch("/{categoryId:guid}", UpdateCategoryAsync)
            .Produces<ApiResponse<CategoryDto>>();
        group.MapDelete("/{categoryId:guid}", DeleteCategoryAsync)
            .Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> GetUserProfileAsync(
        HttpContext httpContext,
        FinanceRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var profile = await repository.GetUserProfileAsync(userId, cancellationToken);
        return profile is null
            ? Error("UNAUTHORIZED", "User was not found.", StatusCodes.Status401Unauthorized)
            : Results.Json(ApiResponse<UserProfileResult>.Ok(profile));
    }

    private static async Task<IResult> UpdateUserProfileAsync(
        UpdateUserProfileRequest request,
        HttpContext httpContext,
        FinanceRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (request.Income is null or < 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest,
                new Dictionary<string, string[]> { ["income"] = ["Income must be greater than or equal to 0."] });
        }

        var profile = await repository.UpdateUserProfileAsync(userId, request.Income.Value, cancellationToken);
        return profile is null
            ? Error("UNAUTHORIZED", "User was not found.", StatusCodes.Status401Unauthorized)
            : Results.Json(ApiResponse<UserProfileResult>.Ok(profile));
    }

    private static async Task<IResult> GetFinanceProfileAsync(
        HttpContext httpContext,
        FinanceRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var profile = await repository.GetFinanceProfileAsync(userId, cancellationToken);
        return profile is null
            ? Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound)
            : Results.Json(ApiResponse<FinanceProfileResult>.Ok(profile));
    }

    private static async Task<IResult> UpdateFinanceProfileAsync(
        UpdateFinanceProfileRequest request,
        HttpContext httpContext,
        FinanceRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (request.Income is < 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest,
                new Dictionary<string, string[]> { ["income"] = ["Income must be greater than or equal to 0."] });
        }

        var profile = await repository.UpdateFinanceProfileAsync(
            userId,
            request.Income,
            request.Currency,
            request.Notifications,
            cancellationToken);

        return profile is null
            ? Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound)
            : Results.Json(ApiResponse<FinanceProfileResult>.Ok(profile));
    }

    private static async Task<IResult> GetSettingsAsync(
        HttpContext httpContext,
        FinanceRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var settings = await repository.GetSettingsAsync(userId, cancellationToken);
        return settings is null
            ? Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound)
            : Results.Json(ApiResponse<SettingsResult>.Ok(settings));
    }

    private static async Task<IResult> UpdateSettingsAsync(
        UpdateSettingsRequest request,
        HttpContext httpContext,
        FinanceRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var settings = await repository.UpdateSettingsAsync(
            userId,
            request.Currency,
            request.Notifications,
            cancellationToken);

        return settings is null
            ? Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound)
            : Results.Json(ApiResponse<SettingsResult>.Ok(settings));
    }

    private static async Task<IResult> CreateTransactionAsync(
        CreateTransactionRequest request,
        HttpContext httpContext,
        FinanceRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var validationErrors = ValidateCreateTransaction(request);
        if (validationErrors.Count > 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest, validationErrors);
        }

        var result = await repository.CreateTransactionAsync(userId, request, cancellationToken);
        return result.Status switch
        {
            FinanceMutationStatus.Success => Results.Json(
                ApiResponse<TransactionDto>.Ok(result.Value!),
                statusCode: StatusCodes.Status201Created),
            FinanceMutationStatus.HouseholdNotFound => Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound),
            _ => Error("INTERNAL_ERROR", "Could not create transaction.", StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<IResult> GetTransactionsAsync(
        HttpRequest request,
        HttpContext httpContext,
        FinanceRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var query = ParseTransactionQuery(request);
        if (!string.IsNullOrWhiteSpace(query.Type) && !AllowedTypes.Contains(query.Type))
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest,
                new Dictionary<string, string[]> { ["type"] = ["Type must be income or expense."] });
        }

        var transactions = await repository.GetTransactionsAsync(userId, query, cancellationToken);
        return transactions is null
            ? Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound)
            : Results.Json(ApiResponse<PagedItemsResponse<TransactionDto>>.Ok(transactions));
    }

    private static async Task<IResult> GetTransactionAsync(
        Guid transactionId,
        HttpContext httpContext,
        FinanceRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var transaction = await repository.GetTransactionAsync(userId, transactionId, cancellationToken);
        return transaction is null
            ? Error("TRANSACTION_NOT_ACCESSIBLE", "Transaction was not found.", StatusCodes.Status404NotFound)
            : Results.Json(ApiResponse<TransactionDto>.Ok(transaction));
    }

    private static async Task<IResult> UpdateTransactionAsync(
        Guid transactionId,
        UpdateTransactionRequest request,
        HttpContext httpContext,
        FinanceRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var validationErrors = ValidateUpdateTransaction(request);
        if (validationErrors.Count > 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest, validationErrors);
        }

        var result = await repository.UpdateTransactionAsync(userId, transactionId, request, cancellationToken);
        return result.Status switch
        {
            FinanceMutationStatus.Success => Results.Json(ApiResponse<TransactionDto>.Ok(result.Value!)),
            FinanceMutationStatus.HouseholdNotFound => Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound),
            FinanceMutationStatus.NotFound => Error("TRANSACTION_NOT_ACCESSIBLE", "Transaction was not found.", StatusCodes.Status404NotFound),
            _ => Error("INTERNAL_ERROR", "Could not update transaction.", StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<IResult> DeleteTransactionAsync(
        Guid transactionId,
        HttpContext httpContext,
        FinanceRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var status = await repository.DeleteTransactionAsync(userId, transactionId, cancellationToken);
        return status switch
        {
            FinanceMutationStatus.Success => Results.NoContent(),
            FinanceMutationStatus.HouseholdNotFound => Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound),
            FinanceMutationStatus.NotFound => Error("TRANSACTION_NOT_ACCESSIBLE", "Transaction was not found.", StatusCodes.Status404NotFound),
            _ => Error("INTERNAL_ERROR", "Could not delete transaction.", StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<IResult> GetCategoriesAsync(
        HttpRequest request,
        FinanceRepository repository,
        CancellationToken cancellationToken)
    {
        var categories = await repository.GetCategoriesAsync(QueryValue(request, "type"), cancellationToken);
        return Results.Json(ApiResponse<ItemsResponse<CategoryDto>>.Ok(new ItemsResponse<CategoryDto>(categories)));
    }

    private static async Task<IResult> CreateCategoryAsync(
        CreateCategoryRequest request,
        FinanceRepository repository,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateCategory(request.Name, request.Type);
        if (validationErrors.Count > 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest, validationErrors);
        }

        var result = await repository.CreateCategoryAsync(request.Name!, request.Type!, cancellationToken);
        return result.Status switch
        {
            FinanceMutationStatus.Success => Results.Json(
                ApiResponse<CategoryDto>.Ok(result.Value!),
                statusCode: StatusCodes.Status201Created),
            FinanceMutationStatus.Conflict => Error("CONFLICT", "Category already exists.", StatusCodes.Status409Conflict),
            _ => Error("INTERNAL_ERROR", "Could not create category.", StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<IResult> UpdateCategoryAsync(
        Guid categoryId,
        UpdateCategoryRequest request,
        FinanceRepository repository,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Type) && !AllowedTypes.Contains(request.Type))
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest,
                new Dictionary<string, string[]> { ["type"] = ["Type must be income or expense."] });
        }

        var category = await repository.UpdateCategoryAsync(categoryId, request.Name, request.Type, cancellationToken);
        return category is null
            ? Error("NOT_FOUND", "Category was not found.", StatusCodes.Status404NotFound)
            : Results.Json(ApiResponse<CategoryDto>.Ok(category));
    }

    private static async Task<IResult> DeleteCategoryAsync(
        Guid categoryId,
        FinanceRepository repository,
        CancellationToken cancellationToken)
    {
        var status = await repository.DeleteCategoryAsync(categoryId, cancellationToken);
        return status switch
        {
            FinanceMutationStatus.Success => Results.NoContent(),
            FinanceMutationStatus.NotFound => Error("NOT_FOUND", "Category was not found.", StatusCodes.Status404NotFound),
            FinanceMutationStatus.Conflict => Error("CONFLICT", "Category is used by transactions.", StatusCodes.Status409Conflict),
            _ => Error("INTERNAL_ERROR", "Could not delete category.", StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<IResult> GetDashboardAsync(
        HttpContext httpContext,
        FinanceRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var dashboard = await repository.GetDashboardAsync(userId, cancellationToken);
        return dashboard is null
            ? Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound)
            : Results.Json(ApiResponse<DashboardResult>.Ok(dashboard));
    }

    private static Dictionary<string, string[]> ValidateCreateTransaction(CreateTransactionRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Type) || !AllowedTypes.Contains(request.Type))
        {
            errors["type"] = ["Type must be income or expense."];
        }

        if (request.Amount is null or <= 0)
        {
            errors["amount"] = ["Amount must be greater than 0."];
        }

        if (request.Date is null)
        {
            errors["date"] = ["Date is required."];
        }

        if (request.CategoryId is null && string.IsNullOrWhiteSpace(request.Category))
        {
            errors["category"] = ["Category is required."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateUpdateTransaction(UpdateTransactionRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!string.IsNullOrWhiteSpace(request.Type) && !AllowedTypes.Contains(request.Type))
        {
            errors["type"] = ["Type must be income or expense."];
        }

        if (request.Amount is <= 0)
        {
            errors["amount"] = ["Amount must be greater than 0."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateCategory(string? name, string? type)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["Name is required."];
        }

        if (string.IsNullOrWhiteSpace(type) || !AllowedTypes.Contains(type))
        {
            errors["type"] = ["Type must be income or expense."];
        }

        return errors;
    }

    private static TransactionQuery ParseTransactionQuery(HttpRequest request)
    {
        var page = ParsePositiveInt(QueryValue(request, "page"), 1);
        var pageSize = Math.Clamp(ParsePositiveInt(QueryValue(request, "pageSize"), 20), 1, 100);

        return new TransactionQuery(
            QueryValue(request, "type"),
            QueryValue(request, "category"),
            ParseGuid(QueryValue(request, "user_id") ?? QueryValue(request, "userId")),
            ParseDate(QueryValue(request, "from") ?? QueryValue(request, "dateFrom")),
            ParseDate(QueryValue(request, "to") ?? QueryValue(request, "dateTo")),
            page,
            pageSize,
            NormalizeSortBy(QueryValue(request, "sortBy")),
            string.Equals(QueryValue(request, "sortOrder"), "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc");
    }

    private static string NormalizeSortBy(string? sortBy) =>
        sortBy?.Trim().ToLowerInvariant() switch
        {
            "amount" => "amount",
            "created_at" or "createdat" => "created_at",
            _ => "date"
        };

    private static string? QueryValue(HttpRequest request, string name) =>
        request.Query.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : null;

    private static int ParsePositiveInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    private static Guid? ParseGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed : null;

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, out var parsed) ? parsed : null;

    private static IResult Unauthorized() =>
        Error("UNAUTHORIZED", "Bearer access token is required.", StatusCodes.Status401Unauthorized);

    private static IResult Error(
        string code,
        string message,
        int statusCode,
        IReadOnlyDictionary<string, string[]>? details = null) =>
        Results.Json(ApiResponse<object>.Fail(code, message, details), statusCode: statusCode);
}

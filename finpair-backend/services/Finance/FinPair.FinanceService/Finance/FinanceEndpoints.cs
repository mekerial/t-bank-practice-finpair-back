using FinPair.Contracts;
using FinPair.Contracts.Validation;
using FinPair.Infrastructure.Auth;

namespace FinPair.FinanceService.Finance;

public static class FinanceEndpoints
{
    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");

        api.MapGet("/users/me", GetUserProfileAsync)
            .WithName("GetUserProfile")
            .WithTags("Users")
            .WithSummary("Get current user's finance profile")
            .WithDescription("Returns income and display profile data for the authenticated user.")
            .Produces<ApiResponse<UserProfileResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized);

        api.MapPatch("/users/me", UpdateUserProfileAsync)
            .WithName("UpdateUserProfile")
            .WithTags("Users")
            .WithSummary("Update current user's finance profile")
            .WithDescription("Updates the authenticated user's income and/or display name.")
            .Produces<ApiResponse<UserProfileResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized);

        api.MapGet("/settings", GetSettingsAsync)
            .WithName("GetSettings")
            .WithTags("Settings")
            .WithSummary("Get household finance settings")
            .WithDescription("Returns currency and notification settings for the authenticated user's household.")
            .Produces<ApiResponse<SettingsResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        api.MapPatch("/settings", UpdateSettingsAsync)
            .WithName("UpdateSettings")
            .WithTags("Settings")
            .WithSummary("Update household finance settings")
            .WithDescription("Updates household currency and/or notification preferences.")
            .Produces<ApiResponse<SettingsResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        api.MapGet("/finance/profile", GetFinanceProfileAsync)
            .WithName("GetFinanceProfile")
            .WithTags("Finance")
            .WithSummary("Get finance profile")
            .WithDescription("Returns combined user and household finance profile for the authenticated user.")
            .Produces<ApiResponse<FinanceProfileResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        api.MapPatch("/finance/profile", UpdateFinanceProfileAsync)
            .WithName("UpdateFinanceProfile")
            .WithTags("Finance")
            .WithSummary("Update finance profile")
            .WithDescription("Updates income, currency and/or notification preferences in the finance profile.")
            .Produces<ApiResponse<FinanceProfileResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        api.MapGet("/finance/dashboard", GetDashboardAsync)
            .WithName("GetFinanceDashboard")
            .WithTags("Finance")
            .WithSummary("Get finance dashboard")
            .WithDescription("Returns dashboard totals and recent financial data for the authenticated user's household.")
            .Produces<ApiResponse<DashboardResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        MapTransactionRoutes(api.MapGroup("/transactions").WithTags("Transactions"));
        MapTransactionRoutes(api.MapGroup("/finance/transactions").WithTags("Transactions"));
        MapCategoryRoutes(api.MapGroup("/categories").WithTags("Categories"));
        MapCategoryRoutes(api.MapGroup("/finance/categories").WithTags("Categories"));

        return endpoints;
    }

    private static void MapTransactionRoutes(RouteGroupBuilder group)
    {
        group.MapPost("", CreateTransactionAsync)
            .WithSummary("Create transaction")
            .WithDescription("Creates an income or expense transaction for the authenticated user's household.")
            .Produces<ApiResponse<TransactionDto>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status500InternalServerError);
        group.MapGet("", GetTransactionsAsync)
            .WithSummary("List transactions")
            .WithDescription("Returns a paged list of household transactions with optional filters by type, category, user and date range.")
            .Produces<ApiResponse<PagedItemsResponse<TransactionDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);
        group.MapGet("/{transactionId:guid}", GetTransactionAsync)
            .WithSummary("Get transaction")
            .WithDescription("Returns a transaction if it belongs to the authenticated user's household.")
            .Produces<ApiResponse<TransactionDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);
        group.MapPatch("/{transactionId:guid}", UpdateTransactionAsync)
            .WithSummary("Update transaction")
            .WithDescription("Updates one or more fields of an accessible household transaction.")
            .Produces<ApiResponse<TransactionDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status500InternalServerError);
        group.MapDelete("/{transactionId:guid}", DeleteTransactionAsync)
            .WithSummary("Delete transaction")
            .WithDescription("Deletes an accessible household transaction.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status500InternalServerError);
    }

    private static void MapCategoryRoutes(RouteGroupBuilder group)
    {
        group.MapGet("", GetCategoriesAsync)
            .WithSummary("List categories")
            .WithDescription("Returns transaction categories, optionally filtered by income or expense type.")
            .Produces<ApiResponse<ItemsResponse<CategoryDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest);
        group.MapPost("", CreateCategoryAsync)
            .WithSummary("Create category")
            .WithDescription("Creates a transaction category with a unique name and type.")
            .Produces<ApiResponse<CategoryDto>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .Produces<ApiResponse<object>>(StatusCodes.Status500InternalServerError);
        group.MapPatch("/{categoryId:guid}", UpdateCategoryAsync)
            .WithSummary("Update category")
            .WithDescription("Updates a category name and/or transaction type.")
            .Produces<ApiResponse<CategoryDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);
        group.MapDelete("/{categoryId:guid}", DeleteCategoryAsync)
            .WithSummary("Delete category")
            .WithDescription("Deletes a category when it is not referenced by transactions.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .Produces<ApiResponse<object>>(StatusCodes.Status500InternalServerError);
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

        var validationErrors = ValidateUserProfile(request);
        if (validationErrors.Count > 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest, validationErrors);
        }

        var result = await repository.UpdateUserProfileAsync(
            userId,
            request.Income,
            request.Name,
            cancellationToken);

        return result.Status switch
        {
            FinanceMutationStatus.Success => Results.Json(ApiResponse<UserProfileResult>.Ok(result.Value!)),
            _ => Error("UNAUTHORIZED", "User was not found.", StatusCodes.Status401Unauthorized)
        };
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

        var validationErrors = ValidateFinanceProfile(request);
        if (validationErrors.Count > 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest, validationErrors);
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

        var validationErrors = ValidateSettings(request);
        if (validationErrors.Count > 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest, validationErrors);
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

        var (query, validationErrors) = ParseTransactionQuery(request);
        if (validationErrors.Count > 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest, validationErrors);
        }

        var transactions = await repository.GetTransactionsAsync(userId, query!, cancellationToken);
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
        var type = QueryValue(request, "type");
        var validationErrors = ValidateCategoryTypeFilter(type);
        if (validationErrors.Count > 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest, validationErrors);
        }

        var categories = await repository.GetCategoriesAsync(type, cancellationToken);
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
        var validationErrors = ValidateUpdateCategory(request);
        if (validationErrors.Count > 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest, validationErrors);
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

    private static IReadOnlyDictionary<string, string[]> ValidateUserProfile(UpdateUserProfileRequest request)
    {
        var errors = new ValidationErrors();
        DomainValidation.OptionalMoney(errors, "income", request.Income, allowZero: true);
        DomainValidation.OptionalText(errors, "name", request.Name, maxLength: 100);

        if (request.Income is null &&
            request.Name is null)
        {
            errors.Add("request", "At least one profile field must be provided.");
        }

        return errors.ToDictionary();
    }

    private static IReadOnlyDictionary<string, string[]> ValidateFinanceProfile(UpdateFinanceProfileRequest request)
    {
        var errors = new ValidationErrors();
        DomainValidation.OptionalMoney(errors, "income", request.Income, allowZero: true);
        DomainValidation.OptionalCurrency(errors, "currency", request.Currency);
        DomainValidation.OptionalNotifications(errors, "notifications", request.Notifications);

        if (request.Income is null &&
            string.IsNullOrWhiteSpace(request.Currency) &&
            request.Notifications is null)
        {
            errors.Add("request", "At least one profile field must be provided.");
        }

        return errors.ToDictionary();
    }

    private static IReadOnlyDictionary<string, string[]> ValidateSettings(UpdateSettingsRequest request)
    {
        var errors = new ValidationErrors();
        DomainValidation.OptionalCurrency(errors, "currency", request.Currency);
        DomainValidation.OptionalNotifications(errors, "notifications", request.Notifications);

        if (string.IsNullOrWhiteSpace(request.Currency) && request.Notifications is null)
        {
            errors.Add("request", "At least one setting must be provided.");
        }

        return errors.ToDictionary();
    }

    private static IReadOnlyDictionary<string, string[]> ValidateCreateTransaction(CreateTransactionRequest request)
    {
        var errors = new ValidationErrors();
        DomainValidation.RequireTransactionType(errors, "type", request.Type);
        DomainValidation.RequiredMoney(errors, "amount", request.Amount);
        DomainValidation.RequiredDate(errors, "date", request.Date);
        DomainValidation.OptionalText(errors, "description", request.Description, maxLength: 500);
        DomainValidation.OptionalText(errors, "title", request.Title, maxLength: 200);
        DomainValidation.OptionalText(errors, "category", request.Category, maxLength: 64, allowBlank: false);

        if (request.CategoryId == Guid.Empty)
        {
            errors.Add("categoryId", "CategoryId is invalid.");
        }

        if (request.CategoryId is null && string.IsNullOrWhiteSpace(request.Category))
        {
            errors.Add("category", "Category is required.");
        }

        return errors.ToDictionary();
    }

    private static IReadOnlyDictionary<string, string[]> ValidateUpdateTransaction(UpdateTransactionRequest request)
    {
        var errors = new ValidationErrors();
        DomainValidation.OptionalTransactionType(errors, "type", request.Type);
        DomainValidation.OptionalMoney(errors, "amount", request.Amount);
        DomainValidation.OptionalDate(errors, "date", request.Date);
        DomainValidation.OptionalText(errors, "description", request.Description, maxLength: 500);
        DomainValidation.OptionalText(errors, "title", request.Title, maxLength: 200);
        DomainValidation.OptionalText(errors, "category", request.Category, maxLength: 64, allowBlank: false);

        if (request.CategoryId == Guid.Empty)
        {
            errors.Add("categoryId", "CategoryId is invalid.");
        }

        if (string.IsNullOrWhiteSpace(request.Type) &&
            request.CategoryId is null &&
            request.Category is null &&
            request.Amount is null &&
            request.Description is null &&
            request.Title is null &&
            request.Date is null)
        {
            errors.Add("request", "At least one transaction field must be provided.");
        }

        return errors.ToDictionary();
    }

    private static IReadOnlyDictionary<string, string[]> ValidateCategory(string? name, string? type)
    {
        var errors = new ValidationErrors();
        DomainValidation.RequiredText(errors, "name", name, maxLength: 64);
        DomainValidation.RequireTransactionType(errors, "type", type);

        return errors.ToDictionary();
    }

    private static IReadOnlyDictionary<string, string[]> ValidateUpdateCategory(UpdateCategoryRequest request)
    {
        var errors = new ValidationErrors();
        DomainValidation.OptionalText(errors, "name", request.Name, maxLength: 64, allowBlank: false);
        DomainValidation.OptionalTransactionType(errors, "type", request.Type);

        if (request.Name is null && request.Type is null)
        {
            errors.Add("request", "At least one category field must be provided.");
        }

        return errors.ToDictionary();
    }

    private static IReadOnlyDictionary<string, string[]> ValidateCategoryTypeFilter(string? type)
    {
        var errors = new ValidationErrors();
        DomainValidation.OptionalTransactionType(errors, "type", type);
        return errors.ToDictionary();
    }

    private static (TransactionQuery? Query, IReadOnlyDictionary<string, string[]> Errors) ParseTransactionQuery(HttpRequest request)
    {
        var errors = new ValidationErrors();
        var page = ParsePositiveInt(QueryValue(request, "page"), "page", 1, errors);
        var pageSize = ParsePositiveInt(QueryValue(request, "pageSize"), "pageSize", 20, errors, max: 100);
        var userId = ParseGuid(QueryValue(request, "user_id") ?? QueryValue(request, "userId"), "userId", errors);
        var from = ParseDate(QueryValue(request, "from") ?? QueryValue(request, "dateFrom"), "from", errors);
        var to = ParseDate(QueryValue(request, "to") ?? QueryValue(request, "dateTo"), "to", errors);
        var type = QueryValue(request, "type");
        var category = QueryValue(request, "category");
        var sortBy = QueryValue(request, "sortBy");
        var sortOrder = QueryValue(request, "sortOrder");

        DomainValidation.OptionalTransactionType(errors, "type", type);
        DomainValidation.OptionalText(errors, "category", category, maxLength: 64, allowBlank: false);

        if (!DomainValidation.IsSortField(sortBy))
        {
            errors.Add("sortBy", "Sort field must be date, amount or created_at.");
        }

        if (!DomainValidation.IsSortOrder(sortOrder))
        {
            errors.Add("sortOrder", "Sort order must be asc or desc.");
        }

        if (from is not null && to is not null && from > to)
        {
            errors.Add("dateRange", "From date must be less than or equal to to date.");
        }

        if (errors.HasErrors)
        {
            return (null, errors.ToDictionary());
        }

        var query = new TransactionQuery(
            type,
            category,
            userId,
            from,
            to,
            page,
            pageSize,
            NormalizeSortBy(sortBy),
            string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc");

        return (query, errors.ToDictionary());
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

    private static int ParsePositiveInt(
        string? value,
        string field,
        int fallback,
        ValidationErrors errors,
        int? max = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (!int.TryParse(value, out var parsed) || parsed <= 0)
        {
            errors.Add(field, $"{field} must be a positive integer.");
            return fallback;
        }

        if (max is not null && parsed > max.Value)
        {
            errors.Add(field, $"{field} must be less than or equal to {max.Value}.");
            return max.Value;
        }

        return parsed;
    }

    private static Guid? ParseGuid(string? value, string field, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
        {
            errors.Add(field, $"{field} must be a valid UUID.");
            return null;
        }

        return parsed;
    }

    private static DateOnly? ParseDate(string? value, string field, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateOnly.TryParse(value, out var parsed))
        {
            errors.Add(field, $"{field} must be a valid ISO date.");
            return null;
        }

        return parsed;
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

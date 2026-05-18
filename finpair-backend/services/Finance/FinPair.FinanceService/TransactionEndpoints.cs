using FinPair.Contracts;
using FinPair.Contracts.Finance;
using FinPair.Contracts.Validation;
using FinPair.FinanceService.Stores;
using Microsoft.AspNetCore.Mvc;

namespace FinPair.FinanceService;

public static class TransactionEndpoints
{
    public static RouteGroupBuilder MapTransactionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/households/{householdId:guid}/transactions")
            .WithTags("Transactions");

        group.MapGet("/", ListAsync)
            .WithName("ListTransactionsByHousehold")
            .WithSummary("List household transactions")
            .WithDescription("Returns all transactions stored for the specified household.")
            .Produces<IReadOnlyList<TransactionResponse>>(StatusCodes.Status200OK);

        group.MapPost("/", CreateAsync)
            .WithName("CreateTransaction")
            .WithSummary("Create household transaction")
            .WithDescription("Creates a transaction for the specified household using the legacy household-scoped contract.")
            .Produces<TransactionResponse>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest);

        return group;
    }

    private static async Task<IResult> ListAsync(
        Guid householdId,
        TransactionStore store,
        CancellationToken cancellationToken)
    {
        var items = await store.ListByHouseholdAsync(householdId, cancellationToken);
        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(
        Guid householdId,
        [FromBody] CreateTransactionRequest? request,
        TransactionStore store,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateCreate(request);
        if (validationErrors.Count > 0)
        {
            return ValidationError(validationErrors);
        }

        var (transaction, error) = await store.CreateAsync(householdId, request!, cancellationToken);
        if (error is not null)
        {
            return ValidationError(new Dictionary<string, string[]>
            {
                ["request"] = [error]
            });
        }

        return Results.Created(
            $"/api/v1/households/{householdId}/transactions/{transaction!.Id}",
            transaction);
    }

    private static IReadOnlyDictionary<string, string[]> ValidateCreate(CreateTransactionRequest? request)
    {
        var errors = new ValidationErrors();
        if (request is null)
        {
            errors.Add("request", "Request body is required.");
            return errors.ToDictionary();
        }

        DomainValidation.RequiredGuid(errors, "userId", request.UserId);
        if (request.CategoryId is null || request.CategoryId == Guid.Empty)
        {
            errors.Add("categoryId", "CategoryId is required.");
        }

        DomainValidation.RequireTransactionType(errors, "type", request.Type);
        DomainValidation.RequiredMoney(errors, "amount", request.Amount);
        DomainValidation.OptionalText(errors, "description", request.Description, maxLength: 500);

        if (request.Date == default)
        {
            errors.Add("date", "Date is required.");
        }

        return errors.ToDictionary();
    }

    private static IResult ValidationError(IReadOnlyDictionary<string, string[]> details) =>
        Results.Json(
            ApiResponse<object>.Fail("VALIDATION_ERROR", "Invalid request.", details),
            statusCode: StatusCodes.Status400BadRequest);
}

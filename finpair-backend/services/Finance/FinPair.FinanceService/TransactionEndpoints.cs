using FinPair.Contracts.Finance;
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
            .Produces<IReadOnlyList<TransactionResponse>>(StatusCodes.Status200OK);

        group.MapPost("/", CreateAsync)
            .WithName("CreateTransaction")
            .Produces<TransactionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

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
        [FromBody] CreateTransactionRequest request,
        TransactionStore store,
        CancellationToken cancellationToken)
    {
        var (transaction, error) = await store.CreateAsync(householdId, request, cancellationToken);
        if (error is not null)
            return Results.BadRequest(new { error });

        return Results.Created(
            $"/api/v1/households/{householdId}/transactions/{transaction!.Id}",
            transaction);
    }
}

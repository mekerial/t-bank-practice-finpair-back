using FinPair.Contracts.Households;
using FinPair.CoupleService.Stores;
using Microsoft.AspNetCore.Mvc;

namespace FinPair.CoupleService;

public static class HouseholdEndpoints
{
    public static RouteGroupBuilder MapHouseholdEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/households").WithTags("Households");

        group.MapPost("/", CreateAsync)
            .WithName("CreateHousehold")
            .Produces<HouseholdResponse>(StatusCodes.Status201Created);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetHouseholdById")
            .Produces<HouseholdResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/join", JoinAsync)
            .WithName("JoinHouseholdByInvite")
            .Produces<HouseholdResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateHouseholdRequest? request,
        HouseholdStore store,
        CancellationToken cancellationToken)
    {
        var body = request ?? new CreateHouseholdRequest(null, null);
        var created = await store.CreateAsync(body, cancellationToken);
        return Results.Created($"/api/v1/households/{created.Id}", created);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        HouseholdStore store,
        CancellationToken cancellationToken)
    {
        var household = await store.GetByIdAsync(id, cancellationToken);
        return household is null ? Results.NotFound() : Results.Ok(household);
    }

    private static async Task<IResult> JoinAsync(
        [FromBody] JoinHouseholdRequest request,
        HouseholdStore store,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.InviteCode))
            return Results.BadRequest(new { error = "inviteCode обязателен." });

        var household = await store.JoinByInviteAsync(request, cancellationToken);
        return household is null ? Results.NotFound() : Results.Ok(household);
    }
}

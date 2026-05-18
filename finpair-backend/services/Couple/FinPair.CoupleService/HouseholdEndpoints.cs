using FinPair.Contracts;
using FinPair.Contracts.Households;
using FinPair.Contracts.Validation;
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
            .WithSummary("Create a household")
            .WithDescription("Creates a household with optional currency and split type settings.")
            .Produces<HouseholdResponse>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetHouseholdById")
            .WithSummary("Get household by ID")
            .WithDescription("Returns household details for the specified household identifier.")
            .Produces<HouseholdResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/join", JoinAsync)
            .WithName("JoinHouseholdByInvite")
            .WithSummary("Join household by invite code")
            .WithDescription("Finds and returns a household matching the supplied invite code.")
            .Produces<HouseholdResponse>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateHouseholdRequest? request,
        HouseholdStore store,
        CancellationToken cancellationToken)
    {
        var body = request ?? new CreateHouseholdRequest(null, null);
        var validationErrors = ValidateCreate(body);
        if (validationErrors.Count > 0)
        {
            return ValidationError(validationErrors);
        }

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
        [FromBody] JoinHouseholdRequest? request,
        HouseholdStore store,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateJoin(request);
        if (validationErrors.Count > 0)
        {
            return ValidationError(validationErrors);
        }

        var household = await store.JoinByInviteAsync(request!, cancellationToken);
        return household is null ? Results.NotFound() : Results.Ok(household);
    }

    private static IReadOnlyDictionary<string, string[]> ValidateCreate(CreateHouseholdRequest request)
    {
        var errors = new ValidationErrors();
        DomainValidation.OptionalCurrency(errors, "currency", request.Currency);
        DomainValidation.OptionalSplitType(errors, "splitType", request.SplitType);
        return errors.ToDictionary();
    }

    private static IReadOnlyDictionary<string, string[]> ValidateJoin(JoinHouseholdRequest? request)
    {
        var errors = new ValidationErrors();
        if (request is null)
        {
            errors.Add("request", "Request body is required.");
            return errors.ToDictionary();
        }

        DomainValidation.RequireInviteCode(errors, "inviteCode", request.InviteCode);
        return errors.ToDictionary();
    }

    private static IResult ValidationError(IReadOnlyDictionary<string, string[]> details) =>
        Results.Json(
            ApiResponse<object>.Fail("VALIDATION_ERROR", "Invalid request.", details),
            statusCode: StatusCodes.Status400BadRequest);
}

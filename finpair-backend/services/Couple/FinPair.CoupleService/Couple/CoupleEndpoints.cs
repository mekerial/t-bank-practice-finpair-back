using FinPair.Contracts;
using FinPair.Infrastructure.Auth;

namespace FinPair.CoupleService.Couple;

public static class CoupleEndpoints
{
    private static readonly HashSet<string> AllowedSplitTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "equal",
        "income",
        "income_ratio",
        "custom"
    };

    public static RouteGroupBuilder MapCoupleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/couple").WithTags("Couple");

        group.MapPost("/create", CreateAsync)
            .WithName("CreateCouple")
            .Produces<ApiResponse<CreateCoupleResult>>(StatusCodes.Status201Created);

        group.MapPost("/join", JoinAsync)
            .WithName("JoinCouple")
            .Produces<ApiResponse<JoinCoupleResult>>();

        group.MapGet("/", GetAsync)
            .WithName("GetCouple")
            .Produces<ApiResponse<CoupleDetails>>();

        group.MapPatch("/split", UpdateSplitAsync)
            .WithName("UpdateCoupleSplit")
            .Produces<ApiResponse<CoupleSettingsResult>>();

        group.MapPatch("/settings", UpdateSettingsAsync)
            .WithName("UpdateCoupleSettings")
            .Produces<ApiResponse<CoupleSettingsResult>>();

        group.MapPost("/invite-code/regenerate", RegenerateInviteCodeAsync)
            .WithName("RegenerateCoupleInviteCode")
            .Produces<ApiResponse<InviteCodeResult>>();

        return group;
    }

    private static async Task<IResult> CreateAsync(
        HttpContext httpContext,
        CoupleRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await repository.CreateForUserAsync(userId, cancellationToken);
        return result.Status switch
        {
            CoupleMutationStatus.Success => Results.Json(
                ApiResponse<CreateCoupleResult>.Ok(new CreateCoupleResult(result.Household!.Id, result.Household.InviteCode)),
                statusCode: StatusCodes.Status201Created),
            CoupleMutationStatus.UserNotFound => Error("UNAUTHORIZED", "User was not found.", StatusCodes.Status401Unauthorized),
            CoupleMutationStatus.UserAlreadyLinked => Error("PARTNER_ALREADY_LINKED", "User already belongs to a household.", StatusCodes.Status409Conflict),
            _ => Error("INTERNAL_ERROR", "Could not create couple.", StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<IResult> JoinAsync(
        JoinCoupleRequest request,
        HttpContext httpContext,
        CoupleRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.InviteCode))
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest,
                new Dictionary<string, string[]> { ["inviteCode"] = ["Invite code is required."] });
        }

        var result = await repository.JoinAsync(userId, request.InviteCode, cancellationToken);
        return result.Status switch
        {
            CoupleMutationStatus.Success => Results.Json(
                ApiResponse<JoinCoupleResult>.Ok(new JoinCoupleResult(result.Household!.Id, "linked"))),
            CoupleMutationStatus.UserNotFound => Error("UNAUTHORIZED", "User was not found.", StatusCodes.Status401Unauthorized),
            CoupleMutationStatus.UserAlreadyLinked => Error("PARTNER_ALREADY_LINKED", "User already belongs to a household.", StatusCodes.Status409Conflict),
            CoupleMutationStatus.HouseholdNotFound => Error("INVALID_INVITE_CODE", "Invite code is invalid.", StatusCodes.Status404NotFound),
            CoupleMutationStatus.HouseholdFull => Error("CONFLICT", "Household already has two members.", StatusCodes.Status409Conflict),
            _ => Error("INTERNAL_ERROR", "Could not join couple.", StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<IResult> GetAsync(
        HttpContext httpContext,
        CoupleRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var couple = await repository.GetForUserAsync(userId, cancellationToken);
        return couple is null
            ? Error("NOT_FOUND", "Couple was not found.", StatusCodes.Status404NotFound)
            : Results.Json(ApiResponse<CoupleDetails>.Ok(couple));
    }

    private static Task<IResult> UpdateSplitAsync(
        UpdateSplitRequest request,
        HttpContext httpContext,
        CoupleRepository repository,
        CancellationToken cancellationToken)
    {
        return UpdateSettingsCoreAsync(
            new UpdateCoupleSettingsRequest(request.SplitType, null, null),
            httpContext,
            repository,
            cancellationToken);
    }

    private static Task<IResult> UpdateSettingsAsync(
        UpdateCoupleSettingsRequest request,
        HttpContext httpContext,
        CoupleRepository repository,
        CancellationToken cancellationToken)
    {
        return UpdateSettingsCoreAsync(request, httpContext, repository, cancellationToken);
    }

    private static async Task<IResult> UpdateSettingsCoreAsync(
        UpdateCoupleSettingsRequest request,
        HttpContext httpContext,
        CoupleRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (!string.IsNullOrWhiteSpace(request.SplitType) && !AllowedSplitTypes.Contains(request.SplitType))
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest,
                new Dictionary<string, string[]> { ["splitType"] = ["Split type must be equal, income, income_ratio or custom."] });
        }

        var household = await repository.UpdateSettingsAsync(
            userId,
            request.SplitType,
            request.Currency,
            request.Notifications,
            cancellationToken);

        return household is null
            ? Error("NOT_FOUND", "Couple was not found.", StatusCodes.Status404NotFound)
            : Results.Json(ApiResponse<CoupleSettingsResult>.Ok(new CoupleSettingsResult(
                household.Id,
                household.SplitType,
                household.Currency,
                household.Notifications)));
    }

    private static async Task<IResult> RegenerateInviteCodeAsync(
        HttpContext httpContext,
        CoupleRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var household = await repository.RegenerateInviteCodeAsync(userId, cancellationToken);
        return household is null
            ? Error("NOT_FOUND", "Couple was not found.", StatusCodes.Status404NotFound)
            : Results.Json(ApiResponse<InviteCodeResult>.Ok(new InviteCodeResult(household.InviteCode)));
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

using FinPair.Contracts;
using FinPair.Contracts.Validation;
using FinPair.Infrastructure.Auth;

namespace FinPair.CoupleService.Couple;

public static class CoupleEndpoints
{
    public static RouteGroupBuilder MapCoupleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/couple").WithTags("Couple");

        group.MapPost("/create", CreateAsync)
            .WithName("CreateCouple")
            .WithSummary("Create a couple household")
            .WithDescription("Creates a household for the authenticated user and returns the invite code for a partner.")
            .Produces<ApiResponse<CreateCoupleResult>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .Produces<ApiResponse<object>>(StatusCodes.Status500InternalServerError);

        group.MapPost("/join", JoinAsync)
            .WithName("JoinCouple")
            .WithSummary("Join a couple by invite code")
            .WithDescription("Links the authenticated user to an existing household using a partner invite code.")
            .Produces<ApiResponse<JoinCoupleResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .Produces<ApiResponse<object>>(StatusCodes.Status500InternalServerError);

        group.MapGet("/", GetAsync)
            .WithName("GetCouple")
            .WithSummary("Get couple details")
            .WithDescription("Returns the household, partner and couple settings for the authenticated user.")
            .Produces<ApiResponse<CoupleDetails>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        group.MapPatch("/split", UpdateSplitAsync)
            .WithName("UpdateCoupleSplit")
            .WithSummary("Update couple split type")
            .WithDescription("Updates only the expense split strategy for the authenticated user's household.")
            .Produces<ApiResponse<CoupleSettingsResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        group.MapPatch("/settings", UpdateSettingsAsync)
            .WithName("UpdateCoupleSettings")
            .WithSummary("Update couple settings")
            .WithDescription("Updates one or more household settings such as split type, currency and notifications.")
            .Produces<ApiResponse<CoupleSettingsResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        group.MapPost("/invite-code/regenerate", RegenerateInviteCodeAsync)
            .WithName("RegenerateCoupleInviteCode")
            .WithSummary("Regenerate couple invite code")
            .WithDescription("Creates a new partner invite code for the authenticated user's household.")
            .Produces<ApiResponse<InviteCodeResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

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

        var validationErrors = ValidateJoin(request);
        if (validationErrors.Count > 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest, validationErrors);
        }

        var result = await repository.JoinAsync(userId, request.InviteCode!, cancellationToken);
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
            cancellationToken,
            requireSplitType: true);
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
        CancellationToken cancellationToken,
        bool requireSplitType = false)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var validationErrors = ValidateSettings(request, requireSplitType);
        if (validationErrors.Count > 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest, validationErrors);
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

    private static IReadOnlyDictionary<string, string[]> ValidateJoin(JoinCoupleRequest request)
    {
        var errors = new ValidationErrors();
        DomainValidation.RequireInviteCode(errors, "inviteCode", request.InviteCode);
        return errors.ToDictionary();
    }

    private static IReadOnlyDictionary<string, string[]> ValidateSettings(
        UpdateCoupleSettingsRequest request,
        bool requireSplitType)
    {
        var errors = new ValidationErrors();

        if (requireSplitType)
        {
            DomainValidation.RequireSplitType(errors, "splitType", request.SplitType);
        }
        else
        {
            DomainValidation.OptionalSplitType(errors, "splitType", request.SplitType);
        }

        DomainValidation.OptionalCurrency(errors, "currency", request.Currency);
        DomainValidation.OptionalNotifications(errors, "notifications", request.Notifications);

        if (!requireSplitType &&
            string.IsNullOrWhiteSpace(request.SplitType) &&
            string.IsNullOrWhiteSpace(request.Currency) &&
            request.Notifications is null)
        {
            errors.Add("request", "At least one setting must be provided.");
        }

        return errors.ToDictionary();
    }

    private static IResult Error(
        string code,
        string message,
        int statusCode,
        IReadOnlyDictionary<string, string[]>? details = null) =>
        Results.Json(ApiResponse<object>.Fail(code, message, details), statusCode: statusCode);
}

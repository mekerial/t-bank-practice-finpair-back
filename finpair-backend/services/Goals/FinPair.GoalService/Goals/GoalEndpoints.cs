using FinPair.Contracts;
using FinPair.Contracts.Validation;
using FinPair.Infrastructure.Auth;

namespace FinPair.GoalService.Goals;

public static class GoalEndpoints
{
    public static RouteGroupBuilder MapGoalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/goals").WithTags("Goals");

        group.MapGet("", GetGoalsAsync)
            .WithName("GetGoals")
            .Produces<ApiResponse<ItemsResponse<GoalDto>>>();

        group.MapPost("", CreateGoalAsync)
            .WithName("CreateGoal")
            .Produces<ApiResponse<GoalDto>>(StatusCodes.Status201Created);

        group.MapGet("/{goalId:guid}", GetGoalAsync)
            .WithName("GetGoal")
            .Produces<ApiResponse<GoalDto>>();

        group.MapPatch("/{goalId:guid}", UpdateGoalAsync)
            .WithName("UpdateGoal")
            .Produces<ApiResponse<GoalDto>>();

        group.MapDelete("/{goalId:guid}", DeleteGoalAsync)
            .WithName("DeleteGoal")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/{goalId:guid}/contributions", AddContributionAsync)
            .WithName("AddGoalContribution")
            .Produces<ApiResponse<GoalContributionResult>>(StatusCodes.Status201Created);

        group.MapPost("/{goalId:guid}/contribute", AddContributionAsync)
            .WithName("ContributeGoal")
            .Produces<ApiResponse<GoalContributionResult>>(StatusCodes.Status201Created);

        return group;
    }

    private static async Task<IResult> GetGoalsAsync(
        HttpContext httpContext,
        GoalRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var goals = await repository.GetGoalsAsync(userId, cancellationToken);
        return goals is null
            ? Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound)
            : Results.Json(ApiResponse<ItemsResponse<GoalDto>>.Ok(new ItemsResponse<GoalDto>(goals)));
    }

    private static async Task<IResult> CreateGoalAsync(
        CreateGoalRequest request,
        HttpContext httpContext,
        GoalRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var validationErrors = ValidateCreateGoal(request);
        if (validationErrors.Count > 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest, validationErrors);
        }

        var result = await repository.CreateGoalAsync(userId, request, cancellationToken);
        return result.Status switch
        {
            GoalMutationStatus.Success => Results.Json(
                ApiResponse<GoalDto>.Ok(result.Value!),
                statusCode: StatusCodes.Status201Created),
            GoalMutationStatus.HouseholdNotFound => Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound),
            _ => Error("INTERNAL_ERROR", "Could not create goal.", StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<IResult> GetGoalAsync(
        Guid goalId,
        HttpContext httpContext,
        GoalRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var goal = await repository.GetGoalAsync(userId, goalId, cancellationToken);
        return goal is null
            ? Error("GOAL_NOT_ACCESSIBLE", "Goal was not found.", StatusCodes.Status404NotFound)
            : Results.Json(ApiResponse<GoalDto>.Ok(goal));
    }

    private static async Task<IResult> UpdateGoalAsync(
        Guid goalId,
        UpdateGoalRequest request,
        HttpContext httpContext,
        GoalRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var validationErrors = ValidateUpdateGoal(request);
        if (validationErrors.Count > 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest, validationErrors);
        }

        var result = await repository.UpdateGoalAsync(userId, goalId, request, cancellationToken);
        return result.Status switch
        {
            GoalMutationStatus.Success => Results.Json(ApiResponse<GoalDto>.Ok(result.Value!)),
            GoalMutationStatus.HouseholdNotFound => Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound),
            GoalMutationStatus.GoalNotFound => Error("GOAL_NOT_ACCESSIBLE", "Goal was not found.", StatusCodes.Status404NotFound),
            _ => Error("INTERNAL_ERROR", "Could not update goal.", StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<IResult> DeleteGoalAsync(
        Guid goalId,
        HttpContext httpContext,
        GoalRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var status = await repository.DeleteGoalAsync(userId, goalId, cancellationToken);
        return status switch
        {
            GoalMutationStatus.Success => Results.NoContent(),
            GoalMutationStatus.HouseholdNotFound => Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound),
            GoalMutationStatus.GoalNotFound => Error("GOAL_NOT_ACCESSIBLE", "Goal was not found.", StatusCodes.Status404NotFound),
            _ => Error("INTERNAL_ERROR", "Could not delete goal.", StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<IResult> AddContributionAsync(
        Guid goalId,
        AddGoalContributionRequest request,
        HttpContext httpContext,
        GoalRepository repository,
        CancellationToken cancellationToken)
    {
        if (!httpContext.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var validationErrors = ValidateContribution(request);
        if (validationErrors.Count > 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest, validationErrors);
        }

        var result = await repository.AddContributionAsync(
            userId,
            goalId,
            request.Amount.GetValueOrDefault(),
            request.Date ?? DateOnly.FromDateTime(DateTime.UtcNow),
            cancellationToken);

        return result.Status switch
        {
            GoalMutationStatus.Success => Results.Json(
                ApiResponse<GoalContributionResult>.Ok(result.Value!),
                statusCode: StatusCodes.Status201Created),
            GoalMutationStatus.HouseholdNotFound => Error("NOT_FOUND", "Household was not found.", StatusCodes.Status404NotFound),
            GoalMutationStatus.GoalNotFound => Error("GOAL_NOT_ACCESSIBLE", "Goal was not found.", StatusCodes.Status404NotFound),
            _ => Error("INTERNAL_ERROR", "Could not add contribution.", StatusCodes.Status500InternalServerError)
        };
    }

    private static IReadOnlyDictionary<string, string[]> ValidateCreateGoal(CreateGoalRequest request)
    {
        var errors = new ValidationErrors();
        DomainValidation.RequiredText(errors, "title", request.Title, maxLength: 120);
        DomainValidation.RequiredMoney(errors, "targetAmount", request.TargetAmount);
        DomainValidation.OptionalMoney(errors, "currentAmount", request.CurrentAmount, allowZero: true);
        DomainValidation.OptionalMoney(errors, "monthlyContribution", request.MonthlyContribution, allowZero: true);
        DomainValidation.OptionalDate(errors, "deadline", request.Deadline);
        ValidateGoalAmounts(errors, request.TargetAmount, request.CurrentAmount);
        return errors.ToDictionary();
    }

    private static IReadOnlyDictionary<string, string[]> ValidateUpdateGoal(UpdateGoalRequest request)
    {
        var errors = new ValidationErrors();
        DomainValidation.OptionalText(errors, "title", request.Title, maxLength: 120, allowBlank: false);
        DomainValidation.OptionalMoney(errors, "targetAmount", request.TargetAmount);
        DomainValidation.OptionalMoney(errors, "currentAmount", request.CurrentAmount, allowZero: true);
        DomainValidation.OptionalMoney(errors, "monthlyContribution", request.MonthlyContribution, allowZero: true);
        DomainValidation.OptionalDate(errors, "deadline", request.Deadline);
        ValidateGoalAmounts(errors, request.TargetAmount, request.CurrentAmount);

        if (request.Title is null &&
            request.TargetAmount is null &&
            request.CurrentAmount is null &&
            request.MonthlyContribution is null &&
            request.Deadline is null &&
            request.IsShared is null)
        {
            errors.Add("request", "At least one goal field must be provided.");
        }

        return errors.ToDictionary();
    }

    private static IReadOnlyDictionary<string, string[]> ValidateContribution(AddGoalContributionRequest request)
    {
        var errors = new ValidationErrors();
        DomainValidation.RequiredMoney(errors, "amount", request.Amount);
        DomainValidation.OptionalDate(errors, "date", request.Date);
        return errors.ToDictionary();
    }

    private static void ValidateGoalAmounts(ValidationErrors errors, decimal? targetAmount, decimal? currentAmount)
    {
        if (targetAmount is not null && currentAmount is not null && currentAmount > targetAmount)
        {
            errors.Add("currentAmount", "Current amount must be less than or equal to target amount.");
        }
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

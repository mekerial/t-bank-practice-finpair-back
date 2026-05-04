using FinPair.Contracts;
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

        if (request.Amount is null or <= 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest,
                new Dictionary<string, string[]> { ["amount"] = ["Amount must be greater than 0."] });
        }

        var result = await repository.AddContributionAsync(
            userId,
            goalId,
            request.Amount.Value,
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

    private static Dictionary<string, string[]> ValidateCreateGoal(CreateGoalRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors["title"] = ["Title is required."];
        }

        if (request.TargetAmount is null or <= 0)
        {
            errors["targetAmount"] = ["Target amount must be greater than 0."];
        }

        if (request.CurrentAmount is < 0)
        {
            errors["currentAmount"] = ["Current amount must be greater than or equal to 0."];
        }

        if (request.MonthlyContribution is < 0)
        {
            errors["monthlyContribution"] = ["Monthly contribution must be greater than or equal to 0."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateUpdateGoal(UpdateGoalRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.TargetAmount is <= 0)
        {
            errors["targetAmount"] = ["Target amount must be greater than 0."];
        }

        if (request.CurrentAmount is < 0)
        {
            errors["currentAmount"] = ["Current amount must be greater than or equal to 0."];
        }

        if (request.MonthlyContribution is < 0)
        {
            errors["monthlyContribution"] = ["Monthly contribution must be greater than or equal to 0."];
        }

        return errors;
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

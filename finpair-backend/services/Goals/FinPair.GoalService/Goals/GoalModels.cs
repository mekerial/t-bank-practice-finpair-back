namespace FinPair.GoalService.Goals;

public sealed record GoalDto(
    Guid Id,
    string Title,
    decimal TargetAmount,
    decimal CurrentAmount,
    decimal MonthlyContribution,
    DateOnly? Deadline,
    bool IsShared,
    decimal ProgressPercent,
    decimal RemainingAmount,
    DateOnly? ForecastDate);

public sealed record CreateGoalRequest(
    string? Title,
    decimal? TargetAmount,
    decimal? CurrentAmount,
    decimal? MonthlyContribution,
    DateOnly? Deadline,
    bool? IsShared);

public sealed record UpdateGoalRequest(
    string? Title,
    decimal? TargetAmount,
    decimal? CurrentAmount,
    decimal? MonthlyContribution,
    DateOnly? Deadline,
    bool? IsShared);

public sealed record AddGoalContributionRequest(decimal? Amount, DateOnly? Date);

public sealed record GoalContributionResult(Guid GoalId, decimal CurrentAmount, decimal ProgressPercent);

public sealed record GoalRecord(
    Guid Id,
    Guid HouseholdId,
    string Title,
    decimal TargetAmount,
    decimal CurrentAmount,
    decimal MonthlyContribution,
    DateOnly? Deadline,
    bool IsShared);

public enum GoalMutationStatus
{
    Success,
    HouseholdNotFound,
    GoalNotFound
}

public sealed record GoalMutationResult<T>(GoalMutationStatus Status, T? Value);

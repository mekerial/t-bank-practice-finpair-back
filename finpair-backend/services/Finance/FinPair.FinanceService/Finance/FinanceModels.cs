namespace FinPair.FinanceService.Finance;

public sealed record UserProfileResult(Guid Id, string Email, string Name, decimal Income);

public sealed record UpdateUserProfileRequest(decimal? Income, string? Name);

public sealed record FinanceProfileResult(decimal Income, string Currency, IReadOnlyDictionary<string, bool> Notifications);

public sealed record UpdateFinanceProfileRequest(
    decimal? Income,
    string? Currency,
    IReadOnlyDictionary<string, bool>? Notifications);

public sealed record SettingsResult(string Currency, IReadOnlyDictionary<string, bool> Notifications);

public sealed record UpdateSettingsRequest(string? Currency, IReadOnlyDictionary<string, bool>? Notifications);

public sealed record CategoryDto(Guid Id, string Name, string Type);

public sealed record CreateCategoryRequest(string? Name, string? Type);

public sealed record UpdateCategoryRequest(string? Name, string? Type);

public sealed record TransactionDto(
    Guid Id,
    string Type,
    decimal Amount,
    string Currency,
    Guid? CategoryId,
    string? Category,
    string Description,
    string Title,
    Guid UserId,
    DateOnly Date);

public sealed record CreateTransactionRequest(
    string? Type,
    Guid? CategoryId,
    string? Category,
    decimal? Amount,
    string? Description,
    string? Title,
    DateOnly? Date);

public sealed record UpdateTransactionRequest(
    string? Type,
    Guid? CategoryId,
    string? Category,
    decimal? Amount,
    string? Description,
    string? Title,
    DateOnly? Date);

public sealed record DashboardResult(
    string Currency,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal Balance,
    decimal FinancialLoadPercent,
    string SplitType,
    IReadOnlyList<PartnerSummary> PartnerSummary);

public sealed record PartnerSummary(Guid UserId, decimal Income, decimal Expense, decimal SharePercent);

public sealed record TransactionQuery(
    string? Type,
    string? Category,
    Guid? UserId,
    DateOnly? From,
    DateOnly? To,
    int Page,
    int PageSize,
    string SortBy,
    string SortOrder);

public enum FinanceMutationStatus
{
    Success,
    UserNotFound,
    HouseholdNotFound,
    NotFound,
    Conflict
}

public sealed record FinanceMutationResult<T>(FinanceMutationStatus Status, T? Value);

public sealed record HouseholdContext(Guid Id, string Currency, string SplitType, IReadOnlyDictionary<string, bool> Notifications);

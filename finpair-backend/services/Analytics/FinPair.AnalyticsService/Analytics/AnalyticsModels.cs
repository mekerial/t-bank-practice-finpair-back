namespace FinPair.AnalyticsService.Analytics;

public sealed record SummaryResult(decimal Income, decimal Expenses, decimal Balance, decimal LoadPercent);

public sealed record CategoryAmount(string Category, decimal Amount);

public sealed record MonthlyDynamicsPoint(string Month, IReadOnlyDictionary<Guid, decimal> Users);

public sealed record InsightResult(string Message);

public sealed record OverviewResult(
    decimal AverageExpenses,
    CategoryAmount? TopCategory,
    decimal SavingsRatePercent,
    decimal FinancialLoadPercent,
    IReadOnlyList<UserAmount> PartnerComparison,
    IReadOnlyList<DateAmount> ExpenseTrend,
    IReadOnlyList<CategoryAmount> CategoryDistribution);

public sealed record UserAmount(Guid UserId, decimal Amount);

public sealed record DateAmount(DateOnly Date, decimal Amount);

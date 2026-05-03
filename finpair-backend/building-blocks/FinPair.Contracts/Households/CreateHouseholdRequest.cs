namespace FinPair.Contracts.Households;

public sealed record CreateHouseholdRequest(
    string? Currency,
    string? SplitType);

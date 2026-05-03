namespace FinPair.Contracts.Finance;

public sealed record TransactionResponse(
    Guid Id,
    Guid HouseholdId,
    Guid UserId,
    Guid? CategoryId,
    string Type,
    decimal Amount,
    string? Description,
    DateOnly Date,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

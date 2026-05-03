namespace FinPair.Contracts.Finance;

public sealed record CreateTransactionRequest(
    Guid UserId,
    Guid? CategoryId,
    string Type,
    decimal Amount,
    string? Description,
    DateOnly Date);

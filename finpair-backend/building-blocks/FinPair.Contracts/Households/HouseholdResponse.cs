using System.Text.Json;

namespace FinPair.Contracts.Households;

public sealed record HouseholdResponse(
    Guid Id,
    string InviteCode,
    string Currency,
    string SplitType,
    JsonElement Notifications,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

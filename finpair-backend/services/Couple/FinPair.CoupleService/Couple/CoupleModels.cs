namespace FinPair.CoupleService.Couple;

public sealed record CreateCoupleResult(Guid CoupleId, string InviteCode);

public sealed record JoinCoupleRequest(string? InviteCode);

public sealed record JoinCoupleResult(Guid CoupleId, string Status);

public sealed record UpdateSplitRequest(string? SplitType);

public sealed record UpdateCoupleSettingsRequest(
    string? SplitType,
    string? Currency,
    IReadOnlyDictionary<string, bool>? Notifications);

public sealed record CoupleSettingsResult(Guid Id, string SplitType, string Currency, IReadOnlyDictionary<string, bool> Notifications);

public sealed record InviteCodeResult(string InviteCode);

public sealed record CoupleDetails(
    Guid Id,
    string InviteCode,
    string Currency,
    string SplitType,
    IReadOnlyDictionary<string, bool> Notifications,
    IReadOnlyList<CoupleMember> Users,
    IReadOnlyList<CoupleMember> Members);

public sealed record CoupleMember(Guid UserId, string Role, string Email, string Name);

public sealed record HouseholdRecord(
    Guid Id,
    string InviteCode,
    string Currency,
    string SplitType,
    IReadOnlyDictionary<string, bool> Notifications);

public enum CoupleMutationStatus
{
    Success,
    UserNotFound,
    UserAlreadyLinked,
    HouseholdNotFound,
    HouseholdFull
}

public sealed record CoupleMutationResult(CoupleMutationStatus Status, HouseholdRecord? Household);

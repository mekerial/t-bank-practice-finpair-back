namespace FinPair.AuthService.Auth;

public sealed record ApiResponse<T>(T? Data, ApiError? Error, object Meta)
{
    public static ApiResponse<T> Ok(T data) => new(data, null, new { });

    public static ApiResponse<T> Fail(string code, string message, IReadOnlyDictionary<string, string[]>? details = null) =>
        new(default, new ApiError(code, message, details), new { });
}

public sealed record ApiError(string Code, string Message, IReadOnlyDictionary<string, string[]>? Details = null);

public sealed record RegisterRequest(string? Email, string? Password, string? Name);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record ChangeEmailRequest(string? Email, string? Password);

public sealed record UserSummary(Guid Id, string Email, string Name, bool EmailVerified, bool HasPartner);

public sealed record AuthResult(UserSummary User, string AccessToken, int ExpiresIn);

public sealed record RefreshResult(string AccessToken, int ExpiresIn);

public sealed record ChangeEmailResult(string Email, bool EmailVerified);

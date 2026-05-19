using System.Security.Claims;
using FinPair.Contracts.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FinPair.AuthService.Auth;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/register", RegisterAsync)
            .WithName("Register")
            .WithSummary("Register a new user")
            .WithDescription("Creates a FinPair account, signs the user in, returns an access token and sets the refresh token cookie.")
            .Produces<ApiResponse<AuthResult>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict);

        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .WithSummary("Sign in with email and password")
            .WithDescription("Validates credentials, returns an access token and rotates the refresh token cookie.")
            .Produces<ApiResponse<AuthResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", RefreshAsync)
            .WithName("Refresh")
            .WithSummary("Refresh an access token")
            .WithDescription("Uses the HTTP-only refresh token cookie, rotates it and returns a fresh access token.")
            .Produces<ApiResponse<RefreshResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .WithSummary("Sign out")
            .WithDescription("Revokes the refresh token from the cookie when present and clears the refresh token cookie.")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", MeAsync)
            .WithName("CurrentUser")
            .WithSummary("Get current user")
            .WithDescription("Returns the profile summary for the user identified by the bearer access token.")
            .Produces<ApiResponse<UserSummary>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized);

        group.MapPatch("/email", ChangeEmailAsync)
            .WithName("ChangeEmail")
            .WithSummary("Change current user's email")
            .WithDescription("Updates the authenticated user's email after password confirmation.")
            .Produces<ApiResponse<ChangeEmailResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict);

        return group;
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        AuthRepository repository,
        PasswordHasher passwordHasher,
        JwtTokenService tokenService,
        IOptions<AuthOptions> options,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateEmailAndPassword(request.Email, request.Password, request.Name);
        if (validationErrors.Count > 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest, validationErrors);
        }

        var user = await repository.CreateUserAsync(
            NormalizeEmail(request.Email!),
            passwordHasher.Hash(request.Password!),
            request.Name,
            cancellationToken);

        if (user is null)
        {
            return Error("EMAIL_ALREADY_EXISTS", "Email already exists.", StatusCodes.Status409Conflict);
        }

        var result = await SignInAsync(user, repository, tokenService, options.Value, httpContext, cancellationToken);
        return Results.Json(ApiResponse<AuthResult>.Ok(result), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        AuthRepository repository,
        PasswordHasher passwordHasher,
        JwtTokenService tokenService,
        IOptions<AuthOptions> options,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateEmailAndPassword(request.Email, request.Password);
        if (validationErrors.Count > 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest, validationErrors);
        }

        var user = await repository.FindUserByEmailAsync(NormalizeEmail(request.Email!), cancellationToken);
        if (user is null || !passwordHasher.Verify(request.Password!, user.PasswordHash))
        {
            return Error("INVALID_CREDENTIALS", "Invalid email or password.", StatusCodes.Status401Unauthorized);
        }

        var result = await SignInAsync(user, repository, tokenService, options.Value, httpContext, cancellationToken);
        return Results.Json(ApiResponse<AuthResult>.Ok(result));
    }

    private static async Task<IResult> RefreshAsync(
        AuthRepository repository,
        JwtTokenService tokenService,
        IOptions<AuthOptions> options,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!httpContext.Request.Cookies.TryGetValue(options.Value.RefreshCookieName, out var refreshToken) ||
            string.IsNullOrWhiteSpace(refreshToken))
        {
            DeleteRefreshCookie(httpContext, options.Value);
            return Error("REFRESH_TOKEN_INVALID", "Refresh token is missing.", StatusCodes.Status401Unauthorized);
        }

        var refreshTokenHash = tokenService.HashRefreshToken(refreshToken);
        var persistedToken = await repository.FindRefreshTokenAsync(refreshTokenHash, cancellationToken);
        if (persistedToken is null || persistedToken.RevokedAt is not null)
        {
            DeleteRefreshCookie(httpContext, options.Value);
            return Error("REFRESH_TOKEN_INVALID", "Refresh token is invalid.", StatusCodes.Status401Unauthorized);
        }

        if (persistedToken.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await repository.RevokeRefreshTokenAsync(refreshTokenHash, cancellationToken);
            DeleteRefreshCookie(httpContext, options.Value);
            return Error("REFRESH_TOKEN_EXPIRED", "Refresh token is expired.", StatusCodes.Status401Unauthorized);
        }

        var user = await repository.FindUserByIdAsync(persistedToken.UserId, cancellationToken);
        if (user is null)
        {
            await repository.RevokeRefreshTokenAsync(refreshTokenHash, cancellationToken);
            DeleteRefreshCookie(httpContext, options.Value);
            return Error("UNAUTHORIZED", "User was not found.", StatusCodes.Status401Unauthorized);
        }

        await repository.RevokeRefreshTokenAsync(refreshTokenHash, cancellationToken);

        var newRefreshToken = tokenService.CreateRefreshToken();
        var newRefreshTokenExpiresAt = DateTimeOffset.UtcNow.Add(options.Value.RefreshTokenLifetime);
        await repository.CreateRefreshTokenAsync(
            user.Id,
            tokenService.HashRefreshToken(newRefreshToken),
            newRefreshTokenExpiresAt,
            cancellationToken);

        SetRefreshCookie(httpContext, options.Value, newRefreshToken, newRefreshTokenExpiresAt);

        var accessToken = tokenService.CreateAccessToken(user);
        return Results.Json(ApiResponse<RefreshResult>.Ok(new RefreshResult(accessToken, tokenService.AccessTokenExpiresInSeconds)));
    }

    private static async Task<IResult> LogoutAsync(
        AuthRepository repository,
        JwtTokenService tokenService,
        IOptions<AuthOptions> options,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (httpContext.Request.Cookies.TryGetValue(options.Value.RefreshCookieName, out var refreshToken) &&
            !string.IsNullOrWhiteSpace(refreshToken))
        {
            await repository.RevokeRefreshTokenAsync(tokenService.HashRefreshToken(refreshToken), cancellationToken);
        }

        DeleteRefreshCookie(httpContext, options.Value);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(
        AuthRepository repository,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return Error("UNAUTHORIZED", "Bearer access token is required.", StatusCodes.Status401Unauthorized);
        }

        var user = await repository.FindUserByIdAsync(userId, cancellationToken);
        return user is null
            ? Error("UNAUTHORIZED", "User was not found.", StatusCodes.Status401Unauthorized)
            : Results.Json(ApiResponse<UserSummary>.Ok(ToUserSummary(user)));
    }

    private static async Task<IResult> ChangeEmailAsync(
        [FromBody] ChangeEmailRequest request,
        AuthRepository repository,
        PasswordHasher passwordHasher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext, out var userId))
        {
            return Error("UNAUTHORIZED", "Bearer access token is required.", StatusCodes.Status401Unauthorized);
        }

        var validationErrors = ValidateEmailAndPassword(request.Email, request.Password);
        if (validationErrors.Count > 0)
        {
            return Error("VALIDATION_ERROR", "Invalid request.", StatusCodes.Status400BadRequest, validationErrors);
        }

        var user = await repository.FindUserByIdAsync(userId, cancellationToken);
        if (user is null || !passwordHasher.Verify(request.Password!, user.PasswordHash))
        {
            return Error("INVALID_CREDENTIALS", "Invalid password.", StatusCodes.Status401Unauthorized);
        }

        var updatedUser = await repository.UpdateEmailAsync(user.Id, NormalizeEmail(request.Email!), cancellationToken);
        if (updatedUser is null)
        {
            return Error("EMAIL_ALREADY_EXISTS", "Email already exists.", StatusCodes.Status409Conflict);
        }

        return Results.Json(ApiResponse<ChangeEmailResult>.Ok(new ChangeEmailResult(updatedUser.Email, updatedUser.EmailVerified)));
    }

    private static async Task<AuthResult> SignInAsync(
        UserRecord user,
        AuthRepository repository,
        JwtTokenService tokenService,
        AuthOptions options,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var accessToken = tokenService.CreateAccessToken(user);
        var refreshToken = tokenService.CreateRefreshToken();
        var refreshTokenExpiresAt = DateTimeOffset.UtcNow.Add(options.RefreshTokenLifetime);

        await repository.CreateRefreshTokenAsync(
            user.Id,
            tokenService.HashRefreshToken(refreshToken),
            refreshTokenExpiresAt,
            cancellationToken);

        SetRefreshCookie(httpContext, options, refreshToken, refreshTokenExpiresAt);

        return new AuthResult(ToUserSummary(user), accessToken, tokenService.AccessTokenExpiresInSeconds);
    }

    private static void SetRefreshCookie(HttpContext httpContext, AuthOptions options, string refreshToken, DateTimeOffset expiresAt)
    {
        httpContext.Response.Cookies.Append(options.RefreshCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = options.RefreshCookieSecure,
            SameSite = options.RefreshCookieSameSiteMode,
            Path = options.RefreshCookiePath,
            Expires = expiresAt
        });
    }

    private static void DeleteRefreshCookie(HttpContext httpContext, AuthOptions options)
    {
        httpContext.Response.Cookies.Delete(options.RefreshCookieName, new CookieOptions
        {
            Secure = options.RefreshCookieSecure,
            SameSite = options.RefreshCookieSameSiteMode,
            Path = options.RefreshCookiePath
        });
    }

    private static bool TryGetUserId(HttpContext httpContext, out Guid userId)
    {
        userId = Guid.Empty;
        var subject = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                      httpContext.User.FindFirstValue("sub");
        return Guid.TryParse(subject, out userId);
    }

    private static UserSummary ToUserSummary(UserRecord user) =>
        new(user.Id, user.Email, user.Name ?? string.Empty, user.EmailVerified, user.HouseholdId is not null);

    private static IReadOnlyDictionary<string, string[]> ValidateEmailAndPassword(
        string? email,
        string? password,
        string? name = null)
    {
        var errors = new ValidationErrors();
        DomainValidation.RequireEmail(errors, "email", email);
        DomainValidation.RequirePassword(errors, "password", password);
        DomainValidation.OptionalText(errors, "name", name, maxLength: 100);

        return errors.ToDictionary();
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static IResult Error(
        string code,
        string message,
        int statusCode,
        IReadOnlyDictionary<string, string[]>? details = null) =>
        Results.Json(ApiResponse<object>.Fail(code, message, details), statusCode: statusCode);
}

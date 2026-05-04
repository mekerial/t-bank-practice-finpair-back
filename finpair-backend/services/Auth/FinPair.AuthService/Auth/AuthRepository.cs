using FinPair.Infrastructure;
using Npgsql;

namespace FinPair.AuthService.Auth;

public sealed record UserRecord(Guid Id, string Email, string PasswordHash, string? Name, Guid? HouseholdId, bool EmailVerified);

public sealed record RefreshTokenRecord(Guid Id, Guid UserId, string TokenHash, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt);

public sealed class AuthRepository(NpgsqlDataSource dataSource)
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) =>
        FinPairSchema.EnsureCoreSchemaAsync(dataSource, cancellationToken);

    public async Task<UserRecord?> CreateUserAsync(string email, string passwordHash, string? name, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (id, email, password_hash, name, email_verified, created_at, updated_at)
            VALUES (@id, @email, @password_hash, @name, false, now(), now())
            RETURNING id, email, password_hash, name, household_id, email_verified;
            """;

        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("password_hash", passwordHash);
        command.Parameters.AddWithValue("name", string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim());

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
        }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        {
            return null;
        }
    }

    public async Task<UserRecord?> FindUserByEmailAsync(string email, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, email, password_hash, name, household_id, email_verified
            FROM users
            WHERE lower(email) = lower(@email)
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("email", email);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    public async Task<UserRecord?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, email, password_hash, name, household_id, email_verified
            FROM users
            WHERE id = @id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("id", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    public async Task<UserRecord?> UpdateEmailAsync(Guid userId, string email, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE users
            SET email = @email,
                email_verified = false,
                updated_at = now()
            WHERE id = @id
            RETURNING id, email, password_hash, name, household_id, email_verified;
            """;
        command.Parameters.AddWithValue("id", userId);
        command.Parameters.AddWithValue("email", email);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
        }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        {
            return null;
        }
    }

    public async Task CreateRefreshTokenAsync(Guid userId, string tokenHash, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO refresh_tokens (id, user_id, token_hash, expires_at, created_at)
            VALUES (@id, @user_id, @token_hash, @expires_at, now());
            """;

        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("token_hash", tokenHash);
        command.Parameters.AddWithValue("expires_at", expiresAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<RefreshTokenRecord?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, user_id, token_hash, expires_at, revoked_at
            FROM refresh_tokens
            WHERE token_hash = @token_hash
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("token_hash", tokenHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRefreshToken(reader) : null;
    }

    public async Task RevokeRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE refresh_tokens
            SET revoked_at = COALESCE(revoked_at, now())
            WHERE token_hash = @token_hash;
            """;
        command.Parameters.AddWithValue("token_hash", tokenHash);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static UserRecord ReadUser(NpgsqlDataReader reader)
    {
        return new UserRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            !reader.IsDBNull(5) && reader.GetBoolean(5));
    }

    private static RefreshTokenRecord ReadRefreshToken(NpgsqlDataReader reader)
    {
        return new RefreshTokenRecord(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4));
    }
}

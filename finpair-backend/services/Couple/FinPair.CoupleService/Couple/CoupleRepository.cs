using System.Security.Cryptography;
using System.Text.Json;
using FinPair.Infrastructure;
using Npgsql;

namespace FinPair.CoupleService.Couple;

public sealed class CoupleRepository(NpgsqlDataSource dataSource, PostgresConnectionString postgres)
{
    private const string DefaultNotifications = """{"transactions":true,"goals":true,"reports":true}""";
    private const string InviteChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) =>
        FinPairSchema.EnsureCoreSchemaAsync(postgres, cancellationToken);

    public async Task<CoupleMutationResult> CreateForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var householdId = await FindUserHouseholdIdAsync(connection, transaction, userId, cancellationToken);
        if (householdId.UserMissing)
        {
            return new CoupleMutationResult(CoupleMutationStatus.UserNotFound, null);
        }

        if (householdId.HouseholdId is not null)
        {
            return new CoupleMutationResult(CoupleMutationStatus.UserAlreadyLinked, null);
        }

        var household = await InsertHouseholdAsync(connection, transaction, cancellationToken);

        await using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = """
            UPDATE users
            SET household_id = @household_id,
                updated_at = now()
            WHERE id = @user_id;
            """;
        updateCommand.Parameters.AddWithValue("household_id", household.Id);
        updateCommand.Parameters.AddWithValue("user_id", userId);
        await updateCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new CoupleMutationResult(CoupleMutationStatus.Success, household);
    }

    public async Task<CoupleMutationResult> JoinAsync(Guid userId, string inviteCode, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var userHousehold = await FindUserHouseholdIdAsync(connection, transaction, userId, cancellationToken);
        if (userHousehold.UserMissing)
        {
            return new CoupleMutationResult(CoupleMutationStatus.UserNotFound, null);
        }

        if (userHousehold.HouseholdId is not null)
        {
            return new CoupleMutationResult(CoupleMutationStatus.UserAlreadyLinked, null);
        }

        var household = await FindHouseholdByInviteCodeAsync(connection, transaction, inviteCode, cancellationToken);
        if (household is null)
        {
            return new CoupleMutationResult(CoupleMutationStatus.HouseholdNotFound, null);
        }

        var membersCount = await CountHouseholdMembersAsync(connection, transaction, household.Id, cancellationToken);
        if (membersCount >= 2)
        {
            return new CoupleMutationResult(CoupleMutationStatus.HouseholdFull, null);
        }

        await using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = """
            UPDATE users
            SET household_id = @household_id,
                updated_at = now()
            WHERE id = @user_id;
            """;
        updateCommand.Parameters.AddWithValue("household_id", household.Id);
        updateCommand.Parameters.AddWithValue("user_id", userId);
        await updateCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new CoupleMutationResult(CoupleMutationStatus.Success, household);
    }

    public async Task<CoupleDetails?> GetForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT h.id, h.invite_code, h.currency, h.split_type, h.notifications::text
            FROM users u
            JOIN households h ON h.id = u.household_id
            WHERE u.id = @user_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("user_id", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var household = ReadHousehold(reader);
        await reader.DisposeAsync();

        var members = await GetMembersAsync(connection, household.Id, cancellationToken);
        return new CoupleDetails(
            household.Id,
            household.InviteCode,
            household.Currency,
            household.SplitType,
            household.Notifications,
            members,
            members);
    }

    public async Task<HouseholdRecord?> UpdateSettingsAsync(
        Guid userId,
        string? splitType,
        string? currency,
        IReadOnlyDictionary<string, bool>? notifications,
        CancellationToken cancellationToken)
    {
        var current = await GetHouseholdForUserAsync(userId, cancellationToken);
        if (current is null)
        {
            return null;
        }

        var nextSplitType = string.IsNullOrWhiteSpace(splitType) ? current.SplitType : splitType.Trim();
        var nextCurrency = string.IsNullOrWhiteSpace(currency) ? current.Currency : currency.Trim().ToUpperInvariant();
        var nextNotifications = notifications ?? current.Notifications;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE households
            SET split_type = @split_type,
                currency = @currency,
                notifications = CAST(@notifications AS jsonb),
                updated_at = now()
            WHERE id = @household_id
            RETURNING id, invite_code, currency, split_type, notifications::text;
            """;
        command.Parameters.AddWithValue("household_id", current.Id);
        command.Parameters.AddWithValue("split_type", nextSplitType);
        command.Parameters.AddWithValue("currency", nextCurrency);
        command.Parameters.AddWithValue("notifications", JsonSerializer.Serialize(nextNotifications));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadHousehold(reader) : null;
    }

    public async Task<HouseholdRecord?> RegenerateInviteCodeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var current = await GetHouseholdForUserAsync(userId, cancellationToken);
        if (current is null)
        {
            return null;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var inviteCode = GenerateInviteCode();
            if (await InviteCodeExistsAsync(connection, null, inviteCode, cancellationToken))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE households
                SET invite_code = @invite_code,
                    updated_at = now()
                WHERE id = @household_id
                RETURNING id, invite_code, currency, split_type, notifications::text;
                """;
            command.Parameters.AddWithValue("household_id", current.Id);
            command.Parameters.AddWithValue("invite_code", inviteCode);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadHousehold(reader) : null;
        }

        throw new InvalidOperationException("Could not generate a unique invite code.");
    }

    private async Task<HouseholdRecord?> GetHouseholdForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT h.id, h.invite_code, h.currency, h.split_type, h.notifications::text
            FROM users u
            JOIN households h ON h.id = u.household_id
            WHERE u.id = @user_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("user_id", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadHousehold(reader) : null;
    }

    private static async Task<(bool UserMissing, Guid? HouseholdId)> FindUserHouseholdIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT household_id
            FROM users
            WHERE id = @user_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("user_id", userId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null)
        {
            return (true, null);
        }

        return result is DBNull ? (false, null) : (false, (Guid)result);
    }

    private static async Task<HouseholdRecord> InsertHouseholdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var inviteCode = GenerateInviteCode();
            if (await InviteCodeExistsAsync(connection, transaction, inviteCode, cancellationToken))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO households (id, invite_code, currency, split_type, notifications, created_at, updated_at)
                VALUES (@id, @invite_code, 'RUB', 'equal', CAST(@notifications AS jsonb), now(), now())
                RETURNING id, invite_code, currency, split_type, notifications::text;
                """;
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("invite_code", inviteCode);
            command.Parameters.AddWithValue("notifications", DefaultNotifications);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return ReadHousehold(reader);
            }
        }

        throw new InvalidOperationException("Could not generate a unique invite code.");
    }

    private static async Task<bool> InviteCodeExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string inviteCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM households WHERE invite_code = @invite_code);";
        command.Parameters.AddWithValue("invite_code", inviteCode);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<HouseholdRecord?> FindHouseholdByInviteCodeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string inviteCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, invite_code, currency, split_type, notifications::text
            FROM households
            WHERE upper(invite_code) = upper(@invite_code)
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("invite_code", inviteCode.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadHousehold(reader) : null;
    }

    private static async Task<int> CountHouseholdMembersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid householdId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT count(*) FROM users WHERE household_id = @household_id;";
        command.Parameters.AddWithValue("household_id", householdId);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<IReadOnlyList<CoupleMember>> GetMembersAsync(
        NpgsqlConnection connection,
        Guid householdId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, email, name
            FROM users
            WHERE household_id = @household_id
            ORDER BY created_at, id;
            """;
        command.Parameters.AddWithValue("household_id", householdId);

        var members = new List<CoupleMember>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var role = members.Count == 0 ? "A" : "B";
            members.Add(new CoupleMember(
                reader.GetGuid(0),
                role,
                reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2)));
        }

        return members;
    }

    private static HouseholdRecord ReadHousehold(NpgsqlDataReader reader)
    {
        return new HouseholdRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            DeserializeNotifications(reader.GetString(4)));
    }

    private static IReadOnlyDictionary<string, bool> DeserializeNotifications(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ??
               new Dictionary<string, bool>();
    }

    private static string GenerateInviteCode()
    {
        Span<char> chars = stackalloc char[6];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = InviteChars[RandomNumberGenerator.GetInt32(InviteChars.Length)];
        }

        return $"FINPAIR-{new string(chars)}";
    }
}

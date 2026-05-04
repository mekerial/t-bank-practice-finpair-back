using System.Security.Cryptography;
using System.Text.Json;
using FinPair.Contracts.Households;
using Npgsql;

namespace FinPair.CoupleService.Stores;

public sealed class HouseholdStore(NpgsqlDataSource dataSource)
{
    private static readonly JsonElement EmptyNotifications = JsonDocument.Parse("{}").RootElement;

    public async Task<HouseholdResponse> CreateAsync(CreateHouseholdRequest request, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var inviteCode = GenerateInviteCode();
        var currency = string.IsNullOrWhiteSpace(request.Currency) ? "RUB" : request.Currency.Trim();
        var splitType = NormalizeSplitType(request.SplitType);

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO households (id, invite_code, currency, split_type, notifications, created_at, updated_at)
            VALUES (@id, @invite_code, @currency, @split_type, '{}'::jsonb, NOW(), NOW());
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("invite_code", inviteCode);
        command.Parameters.AddWithValue("currency", currency);
        command.Parameters.AddWithValue("split_type", splitType);
        await command.ExecuteNonQueryAsync(ct);

        return await GetByIdRequiredAsync(id, connection, ct);
    }

    public async Task<HouseholdResponse?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        return await GetByIdInternalAsync(id, connection, ct);
    }

    public async Task<HouseholdResponse?> JoinByInviteAsync(JoinHouseholdRequest request, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, invite_code, currency, split_type, notifications, created_at, updated_at
            FROM households
            WHERE invite_code = @invite_code
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("invite_code", request.InviteCode.Trim());
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return Map(reader);
    }

    private static async Task<HouseholdResponse> GetByIdRequiredAsync(Guid id, NpgsqlConnection connection, CancellationToken ct)
    {
        var row = await GetByIdInternalAsync(id, connection, ct);
        if (row is null)
            throw new InvalidOperationException("Запись household не найдена сразу после INSERT.");

        return row;
    }

    private static async Task<HouseholdResponse?> GetByIdInternalAsync(Guid id, NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, invite_code, currency, split_type, notifications, created_at, updated_at
            FROM households
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return Map(reader);
    }

    private static HouseholdResponse Map(NpgsqlDataReader reader)
    {
        var id = reader.GetGuid(0);
        var inviteCode = reader.GetString(1);
        var currency = reader.GetString(2);
        var splitType = reader.GetString(3);
        var notifications = ReadNotifications(reader, 4);
        var createdAt = reader.GetFieldValue<DateTimeOffset>(5);
        var updatedAt = reader.GetFieldValue<DateTimeOffset>(6);
        return new HouseholdResponse(id, inviteCode, currency, splitType, notifications, createdAt, updatedAt);
    }

    private static JsonElement ReadNotifications(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return EmptyNotifications;

        return reader.GetFieldValue<JsonElement>(ordinal);
    }

    private static string NormalizeSplitType(string? splitType)
    {
        if (string.IsNullOrWhiteSpace(splitType))
            return "equal";

        var v = splitType.Trim().ToLowerInvariant();
        return v is "equal" or "income" ? v : "equal";
    }

    private static string GenerateInviteCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> buffer = stackalloc char[8];
        var bytes = RandomNumberGenerator.GetBytes(buffer.Length);
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = alphabet[bytes[i] % alphabet.Length];

        return new string(buffer);
    }
}

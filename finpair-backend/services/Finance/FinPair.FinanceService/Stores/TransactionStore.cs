using FinPair.Contracts.Finance;
using Npgsql;

namespace FinPair.FinanceService.Stores;

public sealed class TransactionStore(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyList<TransactionResponse>> ListByHouseholdAsync(Guid householdId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, household_id, user_id, category_id, type, amount, description, date, created_at, updated_at
            FROM transactions
            WHERE household_id = @household_id
            ORDER BY date DESC, created_at DESC;
            """;
        command.Parameters.AddWithValue("household_id", householdId);

        var list = new List<TransactionResponse>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(Map(reader));

        return list;
    }

    public async Task<(TransactionResponse? Transaction, string? Error)> CreateAsync(
        Guid householdId,
        CreateTransactionRequest body,
        CancellationToken ct)
    {
        var type = body.Type.Trim().ToLowerInvariant();
        if (type is not ("income" or "expense"))
            return (null, "type должен быть income или expense.");

        var id = Guid.NewGuid();

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO transactions (id, household_id, user_id, category_id, type, amount, description, date, created_at, updated_at)
            VALUES (@id, @household_id, @user_id, @category_id, @type, @amount, @description, @date, NOW(), NOW());
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("household_id", householdId);
        command.Parameters.AddWithValue("user_id", body.UserId);
        command.Parameters.AddWithValue("category_id", body.CategoryId.HasValue ? body.CategoryId.Value : DBNull.Value);
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("amount", body.Amount);
        command.Parameters.AddWithValue("description", string.IsNullOrWhiteSpace(body.Description) ? DBNull.Value : body.Description);
        command.Parameters.AddWithValue("date", body.Date);

        try
        {
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            return (null, "Не найдены связанные записи: household, user или category.");
        }

        var row = await GetByIdAsync(id, connection, ct);
        return row is null
            ? (null, "Не удалось прочитать созданную транзакцию.")
            : (row, null);
    }

    private static async Task<TransactionResponse?> GetByIdAsync(Guid id, NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, household_id, user_id, category_id, type, amount, description, date, created_at, updated_at
            FROM transactions
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    private static TransactionResponse Map(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.GetString(4),
            reader.GetDecimal(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetFieldValue<DateOnly>(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetFieldValue<DateTimeOffset>(9));
}

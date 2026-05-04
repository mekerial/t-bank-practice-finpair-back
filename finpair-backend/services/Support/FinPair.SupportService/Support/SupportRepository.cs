using FinPair.Infrastructure;
using Npgsql;
using NpgsqlTypes;

namespace FinPair.SupportService.Support;

public sealed class SupportRepository(NpgsqlDataSource dataSource, PostgresConnectionString postgres)
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) =>
        FinPairSchema.EnsureCoreSchemaAsync(postgres, cancellationToken);

    public async Task<SupportTicketResult> CreateMessageAsync(
        Guid? userId,
        string subject,
        string message,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO support_messages (id, user_id, subject, message, status, created_at)
            VALUES (@id, @user_id, @subject, @message, 'created', now())
            RETURNING id, status;
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = userId is null ? DBNull.Value : userId.Value;
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("message", message);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new SupportTicketResult(reader.GetGuid(0), reader.GetString(1));
    }
}

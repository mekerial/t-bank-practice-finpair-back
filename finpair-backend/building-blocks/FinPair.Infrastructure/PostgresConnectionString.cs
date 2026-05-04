namespace FinPair.Infrastructure;

/// <summary>
/// Полная строка подключения из конфигурации. Для миграций DbUp нельзя использовать
/// <see cref="Npgsql.NpgsqlDataSource.ConnectionString"/>: в Npgsql она возвращается без пароля.
/// </summary>
public sealed class PostgresConnectionString
{
    public string Value { get; }

    public PostgresConnectionString(string value) =>
        Value = value ?? throw new ArgumentNullException(nameof(value));
}

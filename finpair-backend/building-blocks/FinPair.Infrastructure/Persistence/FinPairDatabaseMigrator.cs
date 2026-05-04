using DbUp;

namespace FinPair.Infrastructure;

public static class FinPairDatabaseMigrator
{
    private const string JournalTableName = "finpair_schema_versions";

    /// <summary>
    /// Применяет все встроенные SQL-миграции из сборки <see cref="FinPair.Infrastructure"/>.
    /// </summary>
    public static void Upgrade(string connectionString)
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(FinPairDatabaseMigrator).Assembly, EmbeddedSqlNameFilter)
            .JournalToPostgresqlTable("public", JournalTableName)
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw result.Error;
    }

    public static Task UpgradeAsync(PostgresConnectionString connectionString, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Upgrade(connectionString.Value);
        return Task.CompletedTask;
    }

    private static bool EmbeddedSqlNameFilter(string resourceName) =>
        resourceName.Contains(".Database.", StringComparison.Ordinal)
        && resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase);
}

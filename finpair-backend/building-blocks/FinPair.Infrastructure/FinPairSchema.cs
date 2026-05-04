namespace FinPair.Infrastructure;

public static class FinPairSchema
{
    public static Task EnsureCoreSchemaAsync(PostgresConnectionString connectionString, CancellationToken cancellationToken = default) =>
        FinPairDatabaseMigrator.UpgradeAsync(connectionString, cancellationToken);
}

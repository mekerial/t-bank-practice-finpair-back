using Npgsql;

namespace FinPair.Infrastructure;

public static class FinPairSchema
{
    public static Task EnsureCoreSchemaAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default) =>
        FinPairDatabaseMigrator.UpgradeAsync(dataSource, cancellationToken);
}

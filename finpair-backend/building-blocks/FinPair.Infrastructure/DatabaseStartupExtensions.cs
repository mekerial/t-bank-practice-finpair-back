using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FinPair.Infrastructure;

public static class DatabaseStartupExtensions
{
    public static async Task<WebApplication> EnsureFinPairDatabaseReadyAsync<TRepository>(
        this WebApplication app,
        string serviceName,
        Func<TRepository, CancellationToken, Task> ensureSchemaAsync,
        CancellationToken cancellationToken = default)
        where TRepository : notnull
    {
        await using var scope = app.Services.CreateAsyncScope();

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("FinPair.DatabaseStartup");
        var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var postgres = scope.ServiceProvider.GetRequiredService<PostgresConnectionString>();

        LogConnectionSettings(logger, serviceName, postgres.Value);

        logger.LogInformation("Checking PostgreSQL connection for {ServiceName}", serviceName);
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
        }

        logger.LogInformation("PostgreSQL connection check succeeded for {ServiceName}", serviceName);

        logger.LogInformation("Ensuring database schema for {ServiceName}", serviceName);
        var repository = scope.ServiceProvider.GetRequiredService<TRepository>();
        await ensureSchemaAsync(repository, cancellationToken);
        logger.LogInformation("Database schema is ready for {ServiceName}", serviceName);

        return app;
    }

    private static void LogConnectionSettings(ILogger logger, string serviceName, string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            logger.LogInformation(
                "PostgreSQL settings for {ServiceName}: Host={Host}; Port={Port}; Database={Database}; Username={Username}; Pooling={Pooling}; MaxPoolSize={MaxPoolSize}",
                serviceName,
                builder.Host,
                builder.Port,
                builder.Database,
                builder.Username,
                builder.Pooling,
                builder.MaxPoolSize);
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(
                exception,
                "Unable to parse PostgreSQL connection string for {ServiceName}; connection string value was not logged",
                serviceName);
        }
    }
}

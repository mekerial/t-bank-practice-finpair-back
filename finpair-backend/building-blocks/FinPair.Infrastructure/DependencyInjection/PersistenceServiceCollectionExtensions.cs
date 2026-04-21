using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FinPair.Infrastructure;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddFinPairPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Задайте строку подключения ConnectionStrings:Postgres (см. appsettings).");

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        return services;
    }
}

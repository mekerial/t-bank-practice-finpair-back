using FinPair.Infrastructure;

var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
         ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");

if (string.IsNullOrWhiteSpace(cs))
{
    Console.Error.WriteLine(
        "Задайте переменную окружения ConnectionStrings__Postgres или POSTGRES_CONNECTION_STRING.");
    Environment.Exit(1);
}

FinPairDatabaseMigrator.Upgrade(cs);
Console.WriteLine("Миграции применены.");

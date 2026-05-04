using FinPair.Infrastructure;

// Использование:
//   dotnet run --project FinPair.Migrations.csproj -- "Host=localhost;Port=5432;Database=finpair;Username=...;Password=..."
// или переменная окружения ConnectionStrings__Postgres (как в appsettings сервисов).

var connectionString = ResolveConnectionString(args);
FinPairDatabaseMigrator.Upgrade(connectionString);
Console.WriteLine("Миграции применены.");
return;

static string ResolveConnectionString(string[] argv)
{
    if (argv.Length > 0 && !string.IsNullOrWhiteSpace(argv[0]))
        return argv[0].Trim();

    var fromEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
    if (!string.IsNullOrWhiteSpace(fromEnv))
        return fromEnv.Trim();

    throw new InvalidOperationException(
        "Укажите строку подключения: первый аргумент командной строки или переменная ConnectionStrings__Postgres.");
}

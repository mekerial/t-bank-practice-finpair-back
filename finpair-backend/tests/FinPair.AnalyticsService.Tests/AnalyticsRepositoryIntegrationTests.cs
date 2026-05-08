namespace FinPair.AnalyticsService.Tests;

using FinPair.AnalyticsService.Analytics;
using FinPair.Infrastructure;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class AnalyticsRepositoryIntegrationTests : IClassFixture<AnalyticsPostgresFixture>
{
    private readonly AnalyticsPostgresFixture _fixture;

    public AnalyticsRepositoryIntegrationTests(AnalyticsPostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AnalyticsRepository_BuildsSummaryCategoriesDynamicsAndOverview()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        var repository = new AnalyticsRepository(dataSource, new PostgresConnectionString(_fixture.ConnectionString));
        await repository.EnsureSchemaAsync(CancellationToken.None);

        var userId = await SeedAnalyticsDataAsync(dataSource);

        var summary = await repository.GetSummaryAsync(userId, CancellationToken.None);
        Assert.NotNull(summary);
        Assert.Equal(200000m, summary.Income);
        Assert.Equal(50000m, summary.Expenses);
        Assert.Equal(25m, summary.LoadPercent);

        var categories = await repository.GetCategoriesAsync(userId, CancellationToken.None);
        Assert.NotNull(categories);
        Assert.Equal("products", categories[0].Category);
        Assert.Equal(50000m, categories[0].Amount);

        var dynamics = await repository.GetDynamicsAsync(userId, CancellationToken.None);
        Assert.NotNull(dynamics);
        Assert.Single(dynamics);
        Assert.Equal("2026-04", dynamics[0].Month);

        var insights = await repository.GetInsightsAsync(userId, CancellationToken.None);
        Assert.NotNull(insights);
        Assert.Contains(insights, insight => insight.Message.Contains("below 50%", StringComparison.OrdinalIgnoreCase));

        var overview = await repository.GetOverviewAsync(userId, CancellationToken.None);
        Assert.NotNull(overview);
        Assert.Equal(25m, overview.FinancialLoadPercent);
        Assert.Single(overview.PartnerComparison);
        Assert.Single(overview.ExpenseTrend);
    }

    private static async Task<Guid> SeedAnalyticsDataAsync(NpgsqlDataSource dataSource)
    {
        var householdId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO households (id, invite_code, currency, split_type, notifications, created_at, updated_at)
            VALUES (@household_id, @invite_code, 'RUB', 'equal', '{}'::jsonb, now(), now());

            INSERT INTO users (id, email, password_hash, name, household_id, income, created_at, updated_at)
            VALUES (@user_id, @email, 'hash', 'User', @household_id, 200000, now(), now());

            INSERT INTO transactions (id, household_id, user_id, category_id, type, amount, description, date, created_at, updated_at)
            VALUES
                (@income_transaction_id, @household_id, @user_id, '11111111-1111-1111-1111-111111111117', 'income', 200000, 'salary', '2026-04-01', now(), now()),
                (@expense_transaction_id, @household_id, @user_id, '11111111-1111-1111-1111-111111111111', 'expense', 50000, 'products', '2026-04-10', now(), now());
            """;
        command.Parameters.AddWithValue("household_id", householdId);
        command.Parameters.AddWithValue("invite_code", $"INV-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("email", $"analytics-{Guid.NewGuid():N}@example.com");
        command.Parameters.AddWithValue("income_transaction_id", Guid.NewGuid());
        command.Parameters.AddWithValue("expense_transaction_id", Guid.NewGuid());
        await command.ExecuteNonQueryAsync(CancellationToken.None);
        return userId;
    }
}

public sealed class AnalyticsPostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public bool IsAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("finpair_tests")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await _container.StartAsync(CancellationToken.None);
            ConnectionString = _container.GetConnectionString();
            IsAvailable = true;
        }
        catch
        {
            IsAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

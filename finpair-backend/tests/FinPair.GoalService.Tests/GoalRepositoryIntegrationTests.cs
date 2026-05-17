namespace FinPair.GoalService.Tests;

using FinPair.GoalService.Goals;
using FinPair.Infrastructure;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class GoalRepositoryIntegrationTests : IClassFixture<GoalPostgresFixture>
{
    private readonly GoalPostgresFixture _fixture;

    public GoalRepositoryIntegrationTests(GoalPostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GoalRepository_CreatesGoalAndAddsContribution()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        var repository = new GoalRepository(dataSource, new PostgresConnectionString(_fixture.ConnectionString));
        await repository.EnsureSchemaAsync(CancellationToken.None);

        var userId = await SeedHouseholdWithUserAsync(dataSource);

        var deadline = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(6);
        var created = await repository.CreateGoalAsync(
            userId,
            new CreateGoalRequest(
                "Vacation",
                200000m,
                50000m,
                15000m,
                deadline,
                true),
            CancellationToken.None);

        Assert.Equal(GoalMutationStatus.Success, created.Status);
        Assert.Equal(25m, created.Value!.ProgressPercent);
        Assert.Equal(25000m, created.Value.MonthlyContribution);

        var contribution = await repository.AddContributionAsync(
            userId,
            created.Value.Id,
            10000m,
            new DateOnly(2026, 4, 20),
            CancellationToken.None);

        Assert.Equal(GoalMutationStatus.Success, contribution.Status);
        Assert.Equal(60000m, contribution.Value!.CurrentAmount);
        Assert.Equal(30m, contribution.Value.ProgressPercent);

        var goals = await repository.GetGoalsAsync(userId, CancellationToken.None);
        Assert.NotNull(goals);
        Assert.Single(goals);
    }

    [Fact]
    public async Task GoalRepository_RecalculatesMonthlyContributionWhenGoalDatesOrAmountsChange()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        var repository = new GoalRepository(dataSource, new PostgresConnectionString(_fixture.ConnectionString));
        await repository.EnsureSchemaAsync(CancellationToken.None);

        var userId = await SeedHouseholdWithUserAsync(dataSource);
        var currentMonth = DateOnly.FromDateTime(DateTime.UtcNow);
        var deadline = currentMonth.AddMonths(4);

        var created = await repository.CreateGoalAsync(
            userId,
            new CreateGoalRequest(
                "Laptop",
                100000m,
                40000m,
                null,
                deadline,
                true),
            CancellationToken.None);

        Assert.Equal(GoalMutationStatus.Success, created.Status);
        Assert.Equal(15000m, created.Value!.MonthlyContribution);

        var updated = await repository.UpdateGoalAsync(
            userId,
            created.Value.Id,
            new UpdateGoalRequest(null, null, 70000m, null, null, null),
            CancellationToken.None);

        Assert.Equal(GoalMutationStatus.Success, updated.Status);
        Assert.Equal(7500m, updated.Value!.MonthlyContribution);
    }

    private static async Task<Guid> SeedHouseholdWithUserAsync(NpgsqlDataSource dataSource)
    {
        var householdId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO households (id, invite_code, currency, split_type, notifications, created_at, updated_at)
            VALUES (@household_id, @invite_code, 'RUB', 'equal', '{}'::jsonb, now(), now());

            INSERT INTO users (id, email, password_hash, name, household_id, created_at, updated_at)
            VALUES (@user_id, @email, 'hash', 'User', @household_id, now(), now());
            """;
        command.Parameters.AddWithValue("household_id", householdId);
        command.Parameters.AddWithValue("invite_code", $"INV-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("email", $"goal-{Guid.NewGuid():N}@example.com");
        await command.ExecuteNonQueryAsync(CancellationToken.None);
        return userId;
    }
}

public sealed class GoalPostgresFixture : IAsyncLifetime
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

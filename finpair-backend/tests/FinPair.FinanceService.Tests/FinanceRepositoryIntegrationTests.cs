namespace FinPair.FinanceService.Tests;

using FinPair.FinanceService.Finance;
using FinPair.Infrastructure;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class FinanceRepositoryIntegrationTests : IClassFixture<FinancePostgresFixture>
{
    private readonly FinancePostgresFixture _fixture;

    public FinanceRepositoryIntegrationTests(FinancePostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FinanceRepository_CreatesTransactionListsItAndBuildsDashboard()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        var repository = new FinanceRepository(dataSource, new PostgresConnectionString(_fixture.ConnectionString));
        await repository.EnsureSchemaAsync(CancellationToken.None);

        var (householdId, userId) = await SeedHouseholdWithUserAsync(dataSource);

        var category = await repository.CreateCategoryAsync(
            $"groceries-{Guid.NewGuid():N}",
            "expense",
            CancellationToken.None);
        Assert.Equal(FinanceMutationStatus.Success, category.Status);

        var created = await repository.CreateTransactionAsync(
            userId,
            new CreateTransactionRequest(
                "expense",
                category.Value!.Id,
                null,
                1200.50m,
                "Store",
                null,
                new DateOnly(2026, 4, 10)),
            CancellationToken.None);

        Assert.Equal(FinanceMutationStatus.Success, created.Status);
        Assert.Equal(householdId, await ReadHouseholdIdForTransactionAsync(dataSource, created.Value!.Id));

        var page = await repository.GetTransactionsAsync(
            userId,
            new TransactionQuery("expense", null, null, null, null, 1, 20, "date", "desc"),
            CancellationToken.None);

        Assert.NotNull(page);
        Assert.Single(page.Items);
        Assert.Equal(1200.50m, page.Items[0].Amount);

        var dashboard = await repository.GetDashboardAsync(userId, CancellationToken.None);
        Assert.NotNull(dashboard);
        Assert.Equal(100000m, dashboard.TotalIncome);
        Assert.Equal(1200.50m, dashboard.TotalExpense);
        Assert.Equal(1.2m, dashboard.FinancialLoadPercent);
    }

    [Fact]
    public async Task FinanceRepository_ReadsAndUpdatesUserProfileName()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        var repository = new FinanceRepository(dataSource, new PostgresConnectionString(_fixture.ConnectionString));
        await repository.EnsureSchemaAsync(CancellationToken.None);

        var (_, userId) = await SeedHouseholdWithUserAsync(dataSource);

        var profile = await repository.GetUserProfileAsync(userId, CancellationToken.None);
        Assert.NotNull(profile);
        Assert.Equal("User", profile.Name);

        var updated = await repository.UpdateUserProfileAsync(
            userId,
            null,
            "Partner A",
            CancellationToken.None);

        Assert.Equal(FinanceMutationStatus.Success, updated.Status);
        Assert.NotNull(updated.Value);
        Assert.Equal("Partner A", updated.Value.Name);
        Assert.Equal(profile.Email, updated.Value.Email);
        Assert.Equal(profile.Income, updated.Value.Income);
    }

    [Fact]
    public async Task FinanceRepository_DashboardUsesConfiguredSplitTypeForShares()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        var repository = new FinanceRepository(dataSource, new PostgresConnectionString(_fixture.ConnectionString));
        await repository.EnsureSchemaAsync(CancellationToken.None);

        var (householdId, userAId, _) = await SeedHouseholdWithTwoUsersAsync(dataSource, "income");

        var incomeDashboard = await repository.GetDashboardAsync(userAId, CancellationToken.None);
        Assert.NotNull(incomeDashboard);
        Assert.Equal(66.67m, incomeDashboard.PartnerSummary[0].SharePercent);
        Assert.Equal(33.33m, incomeDashboard.PartnerSummary[1].SharePercent);

        await UpdateSplitTypeAsync(dataSource, householdId, "equal");

        var equalDashboard = await repository.GetDashboardAsync(userAId, CancellationToken.None);
        Assert.NotNull(equalDashboard);
        Assert.Equal(50m, equalDashboard.PartnerSummary[0].SharePercent);
        Assert.Equal(50m, equalDashboard.PartnerSummary[1].SharePercent);
    }

    private static async Task<(Guid HouseholdId, Guid UserId)> SeedHouseholdWithUserAsync(NpgsqlDataSource dataSource)
    {
        var householdId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO households (id, invite_code, currency, split_type, notifications, created_at, updated_at)
            VALUES (@household_id, @invite_code, 'RUB', 'equal', '{}'::jsonb, now(), now());

            INSERT INTO users (id, email, password_hash, name, household_id, income, created_at, updated_at)
            VALUES (@user_id, @email, 'hash', 'User', @household_id, 100000, now(), now());
            """;
        command.Parameters.AddWithValue("household_id", householdId);
        command.Parameters.AddWithValue("invite_code", $"INV-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("email", $"finance-{Guid.NewGuid():N}@example.com");
        await command.ExecuteNonQueryAsync(CancellationToken.None);
        return (householdId, userId);
    }

    private static async Task<(Guid HouseholdId, Guid UserAId, Guid UserBId)> SeedHouseholdWithTwoUsersAsync(
        NpgsqlDataSource dataSource,
        string splitType)
    {
        var householdId = Guid.NewGuid();
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO households (id, invite_code, currency, split_type, notifications, created_at, updated_at)
            VALUES (@household_id, @invite_code, 'RUB', @split_type, '{}'::jsonb, now(), now());

            INSERT INTO users (id, email, password_hash, name, household_id, income, created_at, updated_at)
            VALUES
                (@user_a_id, @user_a_email, 'hash', 'User A', @household_id, 200000, now(), now()),
                (@user_b_id, @user_b_email, 'hash', 'User B', @household_id, 100000, now(), now());
            """;
        command.Parameters.AddWithValue("household_id", householdId);
        command.Parameters.AddWithValue("invite_code", $"INV-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("split_type", splitType);
        command.Parameters.AddWithValue("user_a_id", userAId);
        command.Parameters.AddWithValue("user_a_email", $"finance-a-{Guid.NewGuid():N}@example.com");
        command.Parameters.AddWithValue("user_b_id", userBId);
        command.Parameters.AddWithValue("user_b_email", $"finance-b-{Guid.NewGuid():N}@example.com");
        await command.ExecuteNonQueryAsync(CancellationToken.None);
        return (householdId, userAId, userBId);
    }

    private static async Task UpdateSplitTypeAsync(NpgsqlDataSource dataSource, Guid householdId, string splitType)
    {
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE households
            SET split_type = @split_type
            WHERE id = @household_id;
            """;
        command.Parameters.AddWithValue("household_id", householdId);
        command.Parameters.AddWithValue("split_type", splitType);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<Guid> ReadHouseholdIdForTransactionAsync(NpgsqlDataSource dataSource, Guid transactionId)
    {
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT household_id FROM transactions WHERE id = @id;";
        command.Parameters.AddWithValue("id", transactionId);
        return (Guid)(await command.ExecuteScalarAsync(CancellationToken.None))!;
    }
}

public sealed class FinancePostgresFixture : IAsyncLifetime
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

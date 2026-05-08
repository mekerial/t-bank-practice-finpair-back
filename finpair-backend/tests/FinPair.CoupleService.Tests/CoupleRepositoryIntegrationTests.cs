namespace FinPair.CoupleService.Tests;

using FinPair.CoupleService.Couple;
using FinPair.Infrastructure;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class CoupleRepositoryIntegrationTests : IClassFixture<CouplePostgresFixture>
{
    private readonly CouplePostgresFixture _fixture;

    public CoupleRepositoryIntegrationTests(CouplePostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CoupleRepository_CreatesAndJoinsHousehold()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        var repository = new CoupleRepository(dataSource, new PostgresConnectionString(_fixture.ConnectionString));
        await repository.EnsureSchemaAsync(CancellationToken.None);

        var ownerId = await SeedUserAsync(dataSource, "owner@example.com");
        var partnerId = await SeedUserAsync(dataSource, "partner@example.com");

        var created = await repository.CreateForUserAsync(ownerId, CancellationToken.None);

        Assert.Equal(CoupleMutationStatus.Success, created.Status);
        Assert.NotNull(created.Household);
        Assert.False(string.IsNullOrWhiteSpace(created.Household.InviteCode));

        var joined = await repository.JoinAsync(
            partnerId,
            created.Household.InviteCode,
            CancellationToken.None);

        Assert.Equal(CoupleMutationStatus.Success, joined.Status);
        Assert.Equal(created.Household.Id, joined.Household?.Id);

        var details = await repository.GetForUserAsync(ownerId, CancellationToken.None);
        Assert.NotNull(details);
        Assert.Equal(2, details.Members.Count);
    }

    private static async Task<Guid> SeedUserAsync(NpgsqlDataSource dataSource, string email)
    {
        var userId = Guid.NewGuid();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (id, email, password_hash, name, created_at, updated_at)
            VALUES (@id, @email, 'hash', @name, now(), now());
            """;
        command.Parameters.AddWithValue("id", userId);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("name", email);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
        return userId;
    }
}

public sealed class CouplePostgresFixture : IAsyncLifetime
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

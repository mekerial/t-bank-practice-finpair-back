namespace FinPair.SupportService.Tests;

using FinPair.Infrastructure;
using FinPair.SupportService.Support;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class SupportRepositoryIntegrationTests : IClassFixture<SupportPostgresFixture>
{
    private readonly SupportPostgresFixture _fixture;

    public SupportRepositoryIntegrationTests(SupportPostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SupportRepository_CreatesSupportMessage()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        var repository = new SupportRepository(dataSource, new PostgresConnectionString(_fixture.ConnectionString));
        await repository.EnsureSchemaAsync(CancellationToken.None);

        var userId = await SeedUserAsync(dataSource);

        var ticket = await repository.CreateMessageAsync(
            userId,
            "Goal issue",
            "Progress is not updated",
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, ticket.TicketId);
        Assert.Equal("created", ticket.Status);

        var persisted = await ReadSupportMessageAsync(dataSource, ticket.TicketId);
        Assert.Equal(userId, persisted.UserId);
        Assert.Equal("Goal issue", persisted.Subject);
        Assert.Equal("Progress is not updated", persisted.Message);
    }

    private static async Task<Guid> SeedUserAsync(NpgsqlDataSource dataSource)
    {
        var userId = Guid.NewGuid();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (id, email, password_hash, name, created_at, updated_at)
            VALUES (@user_id, @email, 'hash', 'User', now(), now());
            """;
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("email", $"support-{Guid.NewGuid():N}@example.com");
        await command.ExecuteNonQueryAsync(CancellationToken.None);
        return userId;
    }

    private static async Task<(Guid? UserId, string Subject, string Message)> ReadSupportMessageAsync(
        NpgsqlDataSource dataSource,
        Guid ticketId)
    {
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT user_id, subject, message FROM support_messages WHERE id = @id;";
        command.Parameters.AddWithValue("id", ticketId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync(CancellationToken.None);
        return (
            reader.IsDBNull(0) ? null : reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2));
    }
}

public sealed class SupportPostgresFixture : IAsyncLifetime
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

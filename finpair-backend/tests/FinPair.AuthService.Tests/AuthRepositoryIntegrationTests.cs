namespace FinPair.AuthService.Tests;

using FinPair.AuthService.Auth;
using FinPair.Infrastructure;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class AuthRepositoryIntegrationTests : IClassFixture<AuthPostgresFixture>
{
    private readonly AuthPostgresFixture _fixture;

    public AuthRepositoryIntegrationTests(AuthPostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AuthRepository_CreatesFindsAndRevokesRefreshToken()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        var repository = new AuthRepository(dataSource, new PostgresConnectionString(_fixture.ConnectionString));
        await repository.EnsureSchemaAsync(CancellationToken.None);

        var email = $"user-{Guid.NewGuid():N}@example.com";
        var user = await repository.CreateUserAsync(
            email,
            "password-hash",
            "User",
            CancellationToken.None);

        Assert.NotNull(user);
        Assert.Equal(email, user.Email);
        Assert.Equal("User", user.Name);

        var duplicate = await repository.CreateUserAsync(
            email,
            "password-hash",
            "User",
            CancellationToken.None);

        Assert.Null(duplicate);

        var found = await repository.FindUserByEmailAsync(email.ToUpperInvariant(), CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(user.Id, found.Id);

        var tokenHash = $"token-{Guid.NewGuid():N}";
        await repository.CreateRefreshTokenAsync(
            user.Id,
            tokenHash,
            DateTimeOffset.UtcNow.AddDays(1),
            CancellationToken.None);

        var refreshToken = await repository.FindRefreshTokenAsync(tokenHash, CancellationToken.None);
        Assert.NotNull(refreshToken);
        Assert.Equal(user.Id, refreshToken.UserId);
        Assert.Null(refreshToken.RevokedAt);

        await repository.RevokeRefreshTokenAsync(tokenHash, CancellationToken.None);

        var revoked = await repository.FindRefreshTokenAsync(tokenHash, CancellationToken.None);
        Assert.NotNull(revoked);
        Assert.NotNull(revoked.RevokedAt);
    }
}

public sealed class AuthPostgresFixture : IAsyncLifetime
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

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinPair.Infrastructure.Auth;

public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddFinPairAccessTokenAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AccessTokenOptions>(configuration.GetSection(AccessTokenOptions.SectionName));
        services.AddSingleton<AccessTokenValidator>();

        return services;
    }
}

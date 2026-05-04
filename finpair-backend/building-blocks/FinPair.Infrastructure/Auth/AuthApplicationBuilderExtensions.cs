using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FinPair.Infrastructure.Auth;

public static class AuthApplicationBuilderExtensions
{
    public static IApplicationBuilder UseFinPairBearerAuth(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authorization["Bearer ".Length..].Trim();
                var validator = context.RequestServices.GetRequiredService<AccessTokenValidator>();
                if (validator.TryValidate(token, out var principal))
                {
                    context.User = principal;
                }
            }

            await next();
        });
    }
}

public static class UserContextExtensions
{
    public static bool TryGetUserId(this HttpContext httpContext, out Guid userId)
    {
        userId = Guid.Empty;
        var subject = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                      httpContext.User.FindFirstValue("sub");

        return Guid.TryParse(subject, out userId);
    }
}

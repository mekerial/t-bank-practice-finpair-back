using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace FinPair.Common;

public static class SwaggerHostExtensions
{
    public static IServiceCollection AddFinPairSwagger(this IServiceCollection services, string apiTitle)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = apiTitle,
                Version = "v1",
                Description = "FinPair — API для совместного управления финансами пар."
            });
        });
        return services;
    }

    public static WebApplication UseFinPairSwaggerUi(this WebApplication app, string swaggerUiDocumentTitle)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", swaggerUiDocumentTitle);
        });
        return app;
    }
}

using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinPair.Common;

public static class ApplicationLoggingExtensions
{
    public static WebApplication UseFinPairOperationalLogging(this WebApplication app, string serviceName)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("FinPair.Operational");

        logger.LogInformation(
            "Configuring {ServiceName}; Environment={EnvironmentName}",
            serviceName,
            app.Environment.EnvironmentName);

        app.Lifetime.ApplicationStarted.Register(() =>
            logger.LogInformation("{ServiceName} started", serviceName));
        app.Lifetime.ApplicationStopping.Register(() =>
            logger.LogInformation("{ServiceName} stopping", serviceName));
        app.Lifetime.ApplicationStopped.Register(() =>
            logger.LogInformation("{ServiceName} stopped", serviceName));

        app.Use(async (context, next) =>
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await next();
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                logger.LogError(
                    exception,
                    "HTTP {Method} {Path} failed after {ElapsedMilliseconds} ms",
                    context.Request.Method,
                    context.Request.Path,
                    stopwatch.ElapsedMilliseconds);
                throw;
            }
            finally
            {
                stopwatch.Stop();

                if (context.Response.StatusCode >= StatusCodes.Status400BadRequest)
                {
                    logger.LogWarning(
                        "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds} ms",
                        context.Request.Method,
                        context.Request.Path,
                        context.Response.StatusCode,
                        stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    logger.LogInformation(
                        "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds} ms",
                        context.Request.Method,
                        context.Request.Path,
                        context.Response.StatusCode,
                        stopwatch.ElapsedMilliseconds);
                }
            }
        });

        return app;
    }
}

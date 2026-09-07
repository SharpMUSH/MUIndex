using Microsoft.AspNetCore.RateLimiting;

namespace MUI.Web;

/// <summary>Bounds simultaneous Razor renders, including time spent sending their output.</summary>
public static class PageRenderingLimit
{
    public const string Policy = "page-rendering";

    public static IServiceCollection AddMuiPageRenderingLimit(
        this IServiceCollection services, IConfiguration configuration)
    {
        var limit = configuration.GetValue("PageRendering:ConcurrencyLimit", 8);
        if (limit <= 0)
        {
            throw new InvalidOperationException("PageRendering:ConcurrencyLimit must be positive.");
        }

        services.AddRateLimiter(options =>
        {
            // One budget per process, shared across URLs, locales and clients. A per-IP limiter
            // cannot bound total memory, and a per-query limiter admits every facet permutation.
            options.AddConcurrencyLimiter(Policy, limiter =>
            {
                limiter.PermitLimit = limit;
                limiter.QueueLimit = 0;
            });
            options.OnRejected = async (rejected, cancellationToken) =>
            {
                var response = rejected.HttpContext.Response;
                response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                response.Headers.CacheControl = "no-store";
                response.ContentType = "text/plain; charset=utf-8";
                await response.WriteAsync("The site is busy. Please try again shortly.", cancellationToken);
            };
        });

        return services;
    }
}

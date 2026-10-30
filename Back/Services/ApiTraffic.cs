using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Inkstone.Web.Services;

public static class ApiTraffic
{
    public static PartitionedRateLimiter<HttpContext> Create(int authenticatedPerMinute, int anonymousPerMinute)
    {
        if (authenticatedPerMinute < 1 || anonymousPerMinute < 1)
            throw new InvalidOperationException("API request budgets must be positive.");
        return PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (!context.Request.Path.StartsWithSegments("/api") || context.Request.Path is { Value: "/api/health" or "/api/ready" })
                return RateLimitPartition.GetNoLimiter("health-or-static");
            var owner = context.User.Identity?.IsAuthenticated == true ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
            var key = owner is null ? "ip:" + context.Connection.RemoteIpAddress : "user:" + owner;
            return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = owner is null ? anonymousPerMinute : authenticatedPerMinute,
                Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
            });
        });
    }
}

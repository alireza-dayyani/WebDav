using System.Security.Claims;
using Inkstone.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Inkstone.Api.Tests;

public sealed class CapacityTests
{
    [Fact]
    public async Task Export_saturation_does_not_block_validation_and_cancelled_waits_do_not_leak()
    {
        using var capacity = new ConverterCapacity(Options.Create(new ConverterCapacityOptions { ValidationSlots = 1, ConversionSlots = 1 }));
        using var export = await capacity.EnterAsync(false, default);
        using var validation = await capacity.EnterAsync(true, default);
        using var cancellation = new CancellationTokenSource();
        var waiting = capacity.EnterAsync(false, cancellation.Token);
        Assert.False(waiting.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        export.Dispose(); export.Dispose();
        using var next = await capacity.EnterAsync(false, default);
        using var secondCancellation = new CancellationTokenSource();
        var secondWait = capacity.EnterAsync(false, secondCancellation.Token);
        Assert.False(secondWait.IsCompleted); // Double disposal did not add a second permit.
        secondCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondWait);
    }

    private static HttpContext Context(string? owner, string path = "/api/documents")
    {
        var context = new DefaultHttpContext(); context.Request.Path = path;
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        if (owner is not null) context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, owner)], "test"));
        return context;
    }

    [Fact]
    public void Request_budgets_are_per_account_across_tokens_and_leave_health_available()
    {
        using var limiter = ApiTraffic.Create(2, 1);
        using var first = limiter.AttemptAcquire(Context("alice")); Assert.True(first.IsAcquired);
        using var second = limiter.AttemptAcquire(Context("alice")); Assert.True(second.IsAcquired);
        using var rejected = limiter.AttemptAcquire(Context("alice")); Assert.False(rejected.IsAcquired);
        using var other = limiter.AttemptAcquire(Context("bob")); Assert.True(other.IsAcquired);
        using var anonymous = limiter.AttemptAcquire(Context(null)); Assert.True(anonymous.IsAcquired);
        using var anonymousRejected = limiter.AttemptAcquire(Context(null)); Assert.False(anonymousRejected.IsAcquired);
        using var health = limiter.AttemptAcquire(Context(null, "/api/health")); Assert.True(health.IsAcquired);
        using var readiness = limiter.AttemptAcquire(Context("alice", "/api/ready")); Assert.True(readiness.IsAcquired);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, -1)]
    public void Invalid_request_budgets_fail_configuration(int authenticated, int anonymous)
        => Assert.Throws<InvalidOperationException>(() => ApiTraffic.Create(authenticated, anonymous));
}

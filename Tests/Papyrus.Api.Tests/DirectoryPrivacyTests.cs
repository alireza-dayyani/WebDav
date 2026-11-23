using System.Net;
using System.Text.Json;

namespace Papyrus.Api.Tests;

public sealed class DirectoryPrivacyTests
{
    [Theory]
    [InlineData("", 1, "owner@example.test")]
    [InlineData("?search=other", 1, "other@example.test")]
    [InlineData("?search=example.test", 2, null)]
    [InlineData("?search=OTHER%40EXAMPLE.TEST", 1, "other@example.test")]
    [InlineData("?page=2", 0, null)]
    public async Task Directory_supports_self_lookup_and_bounded_partial_search(string suffix, int count, string? email)
    {
        await using var host = await EndpointHost.CreateAsync();
        await host.WithDatabase(async db =>
        {
            foreach (var user in db.Users) user.NormalizedEmail = user.Email!.ToUpperInvariant();
            await db.SaveChangesAsync();
        });
        var response = await host.Client.GetAsync("/api/letters/directory" + suffix);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(count, result.GetProperty("items").GetArrayLength());
        if (count == 1 && email is not null)
        {
            Assert.False(result.GetProperty("hasMore").GetBoolean());
            Assert.Equal(email, result.GetProperty("items")[0].GetProperty("email").GetString());
        }
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner");
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/letters/directory")).StatusCode);
    }

    [Fact]
    public async Task Directory_rejects_overly_short_partial_search()
    {
        await using var host = await EndpointHost.CreateAsync();
        var response = await host.Client.GetAsync("/api/letters/directory?search=ab");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
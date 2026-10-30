using System.Net;
using System.Text.Json;

namespace Inkstone.Api.Tests;

public sealed class DirectoryPrivacyTests
{
    [Theory]
    [InlineData("", 1, "owner@example.test")]
    [InlineData("?search=other", 0, null)]
    [InlineData("?search=example.test", 0, null)]
    [InlineData("?search=OTHER%40EXAMPLE.TEST", 1, "other@example.test")]
    [InlineData("?page=2", 0, null)]
    public async Task Directory_requires_exact_email_and_never_lists_other_accounts(string suffix, int count, string? email)
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
        Assert.False(result.GetProperty("hasMore").GetBoolean());
        if (email is not null) Assert.Equal(email, result.GetProperty("items")[0].GetProperty("email").GetString());
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner");
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/letters/directory")).StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Api.Tests;
public sealed class SearchRetentionTests
{
    [Fact]
    public async Task Search_character_limit_does_not_split_unicode_and_block_document_saves()
    {
        await using var host = await EndpointHost.CreateAsync();
        await host.WithDatabase(async db =>
        {
            var doc = await db.Documents.SingleAsync();
            var body = "searchable " + new string('a', Inkstone.Web.Services.LibrarySearch.CharacterLimit - doc.Title.Length - 1 - "searchable ".Length - 1) + "😀 afterlimit";
            doc.Content = JsonSerializer.SerializeToUtf8Bytes(new { body = new[] { new { text = body } } });
            doc.Version++;
            await db.SaveChangesAsync();
        });
        var results = await host.Client.GetFromJsonAsync<JsonElement>("/api/library?q=searchable");
        Assert.Equal(1, results.GetProperty("total").GetInt32());
        results = await host.Client.GetFromJsonAsync<JsonElement>("/api/library?q=afterlimit");
        Assert.Equal(0, results.GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task Search_indexes_body_normalizes_persian_and_hides_other_accounts_and_trash()
    {
        await using var host = await EndpointHost.CreateAsync();
        await host.WithDatabase(async db => { var doc = await db.Documents.SingleAsync(); doc.Content = "{\"body\":[{\"text\":\"كتاب يكم\"}]}"u8.ToArray(); doc.Version++; await db.SaveChangesAsync(); });
        var results = await host.Client.GetFromJsonAsync<JsonElement>("/api/library?q=" + Uri.EscapeDataString("کتاب یکم"));
        Assert.Equal(1, results.GetProperty("total").GetInt32());
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner"); host.Client.DefaultRequestHeaders.Add("X-Test-Owner", EndpointHost.OtherOwner);
        results = await host.Client.GetFromJsonAsync<JsonElement>("/api/library?q=" + Uri.EscapeDataString("کتاب")); Assert.Equal(0, results.GetProperty("total").GetInt32());
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner"); host.Client.DefaultRequestHeaders.Add("X-Test-Owner", EndpointHost.Owner);
        await host.WithDatabase(async db => { db.TrashedDocuments.Add(new TrashedDocument { DocumentId = host.DocumentId }); await db.SaveChangesAsync(); });
        results = await host.Client.GetFromJsonAsync<JsonElement>("/api/library?q=" + Uri.EscapeDataString("کتاب")); Assert.Equal(0, results.GetProperty("total").GetInt32());
        results = await host.Client.GetFromJsonAsync<JsonElement>("/api/library?trash=true&q=" + Uri.EscapeDataString("کتاب")); Assert.Equal(1, results.GetProperty("total").GetInt32());
    }
    [Theory]
    [InlineData("\"")]
    [InlineData("OR NOT")]
    [InlineData("hello); DROP TABLE Documents; --")]
    public async Task Search_treats_query_as_literal_terms(string text)
    {
        await using var host = await EndpointHost.CreateAsync();
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/library?q=" + Uri.EscapeDataString(text))).StatusCode);
        await host.WithDatabase(async db => Assert.Single(await db.Documents.ToListAsync()));
    }
    [Fact]
    public async Task Retention_requires_opt_in_current_policy_and_preserves_first_and_recent_versions()
    {
        await using var host = await EndpointHost.CreateAsync();
        await host.WithDatabase(async db => {
            var doc = await db.Documents.SingleAsync();
            for (var i = 2; i <= 10; i++) { doc.Version = i; doc.Title = "Revision " + i; await db.SaveChangesAsync(); }
            await db.DocumentRevisions.ExecuteUpdateAsync(set => set.SetProperty(r => r.CreatedAt, 0));
        });
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync("/api/retention/apply", new { version = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PutAsJsonAsync("/api/retention/", new { version = 0, enabled = false, keepLatest = 5, days = 7 })).StatusCode);
        var preview = await host.Client.PostAsJsonAsync("/api/retention/preview", new { version = 1 });
        Assert.Equal(4, (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("count").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync("/api/retention/apply", new { version = 1 })).StatusCode);
        await host.Client.PutAsJsonAsync("/api/retention/", new { version = 1, enabled = true, keepLatest = 5, days = 7 });
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync("/api/retention/apply", new { version = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsJsonAsync("/api/retention/apply", new { version = 2 })).StatusCode);
        await host.WithDatabase(async db => Assert.Equal(new long[] { 1, 6, 7, 8, 9, 10 }, await db.DocumentRevisions.OrderBy(r => r.Version).Select(r => r.Version).ToArrayAsync()));
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner"); host.Client.DefaultRequestHeaders.Add("X-Test-Owner", EndpointHost.OtherOwner);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync("/api/retention/apply", new { version = 2 })).StatusCode);
    }
}

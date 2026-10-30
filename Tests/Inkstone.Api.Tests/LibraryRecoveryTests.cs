using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Api.Tests;

public sealed class LibraryRecoveryTests
{
    [Fact]
    public async Task Storage_includes_utf8_draft_data_and_snapshots_without_counting_other_accounts()
    {
        await using var host = await EndpointHost.CreateAsync();
        long expected = 0;
        await host.WithDatabase(async db =>
        {
            var template = await db.WorkspaceItems.SingleAsync();
            expected = template.Content.Length + System.Text.Encoding.UTF8.GetByteCount(template.Data) + System.Text.Encoding.UTF8.GetByteCount(template.Snapshot);
            var letter = new WorkspaceItem { OwnerId = EndpointHost.Owner, Kind = "letter", Title = "Draft", Data = "{\"bodyText\":\"سلام 😀\"}", Snapshot = "{\"number\":\"۱\"}" };
            expected += System.Text.Encoding.UTF8.GetByteCount(letter.Data) + System.Text.Encoding.UTF8.GetByteCount(letter.Snapshot);
            db.WorkspaceItems.Add(letter);
            db.WorkspaceItems.Add(new WorkspaceItem { OwnerId = EndpointHost.OtherOwner, Kind = "letter", Title = "Other draft", Data = "{\"bodyText\":\"private\"}" });
            await db.SaveChangesAsync();
        });
        var result = await host.Client.GetFromJsonAsync<JsonElement>("/api/storage");
        Assert.Equal(expected, result.GetProperty("resourceBytes").GetInt64());
    }
    [Fact]
    public async Task Trash_is_hidden_from_document_operations_and_recovery_preserves_revisions()
    {
        await using var host = await EndpointHost.CreateAsync();
        await host.WithDatabase(async db => { db.TrashedDocuments.Add(new TrashedDocument { DocumentId = host.DocumentId }); await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/documents/{host.DocumentId}/versions?version=1")).StatusCode);
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner"); host.Client.DefaultRequestHeaders.Add("X-Test-Owner", EndpointHost.OtherOwner);
        Assert.Equal("[]", await host.Client.GetStringAsync("/api/documents/trash"));
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.PostAsJsonAsync($"/api/documents/{host.DocumentId}/recover", new { version = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.DeleteAsync($"/api/documents/{host.DocumentId}/purge?version=1")).StatusCode);
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner"); host.Client.DefaultRequestHeaders.Add("X-Test-Owner", EndpointHost.Owner);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync($"/api/documents/{host.DocumentId}/recover", new { version = 99 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsJsonAsync($"/api/documents/{host.DocumentId}/recover", new { version = 1 })).StatusCode);
        Assert.Equal("[]", await host.Client.GetStringAsync("/api/documents/trash"));
        await host.WithDatabase(async db => { Assert.Equal(2, (await db.Documents.SingleAsync(d => d.Id == host.DocumentId)).Version); Assert.Equal(2, await db.DocumentRevisions.CountAsync()); });
    }

    [Fact]
    public async Task Permanent_delete_requires_trash_and_expected_version_and_removes_history()
    {
        await using var host = await EndpointHost.CreateAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.DeleteAsync($"/api/documents/{host.DocumentId}/purge?version=1")).StatusCode);
        await host.WithDatabase(async db => { db.TrashedDocuments.Add(new TrashedDocument { DocumentId = host.DocumentId }); await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.DeleteAsync($"/api/documents/{host.DocumentId}/purge?version=2")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync($"/api/documents/{host.DocumentId}/purge?version=1")).StatusCode);
        await host.WithDatabase(async db => { Assert.False(await db.Documents.IgnoreQueryFilters().AnyAsync(d => d.Id == host.DocumentId)); Assert.Empty(await db.DocumentRevisions.ToListAsync()); });
    }

    [Fact]
    public async Task Storage_counts_only_owned_content_and_history_including_trash()
    {
        await using var host = await EndpointHost.CreateAsync();
        var result = await host.Client.GetFromJsonAsync<JsonElement>("/api/storage");
        Assert.True(result.GetProperty("contentBytes").GetInt64() > 0);
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner"); host.Client.DefaultRequestHeaders.Add("X-Test-Owner", EndpointHost.OtherOwner);
        result = await host.Client.GetFromJsonAsync<JsonElement>("/api/storage");
        Assert.Equal(0, result.GetProperty("totalBytes").GetInt64());
    }
}

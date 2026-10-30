using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Api.Tests;
public sealed class CollaborationTests
{
    private static void Actor(EndpointHost host, string id) { host.Client.DefaultRequestHeaders.Remove("X-Test-Owner"); host.Client.DefaultRequestHeaders.Add("X-Test-Owner", id); }
    private static async Task Grant(EndpointHost host, string permission, long grantVersion = 0) => Assert.Equal(HttpStatusCode.OK, (await host.Client.PutAsJsonAsync($"/api/documents/{host.DocumentId}/shares", new { version = 1, email = "other@example.test", permission, grantVersion })).StatusCode);
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_document_without_history_can_be_shared_and_submitted_for_review(bool submitReview)
    {
        await using var host = await EndpointHost.CreateAsync();
        if (submitReview) await Grant(host, "read");
        await host.WithDatabase(db => db.DocumentRevisions.ExecuteDeleteAsync());
        if (submitReview)
            Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsJsonAsync($"/api/documents/{host.DocumentId}/reviews", new { version = 1, reviewerId = EndpointHost.OtherOwner })).StatusCode);
        else await Grant(host, "read");
        await host.WithDatabase(async db => Assert.Single(await db.DocumentRevisions.ToListAsync()));
    }
    [Fact]
    public async Task Revocation_and_regrant_cannot_resurrect_an_in_flight_edit_or_stale_share_request()
    {
        await using var host = await EndpointHost.CreateAsync(); await Grant(host, "edit"); Actor(host, EndpointHost.OtherOwner);
        host.Processor.BeforeAutomate = async () =>
        {
            Actor(host, EndpointHost.Owner);
            Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync($"/api/documents/{host.DocumentId}/shares/{EndpointHost.OtherOwner}?version=1&grantVersion=1")).StatusCode);
            await Grant(host, "read");
        };
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync($"/api/documents/{host.DocumentId}/batch", new { version = 1, operations = new[] { new { type = "trackChanges", enabled = true } } })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PutAsJsonAsync($"/api/documents/{host.DocumentId}/shares", new { version = 1, email = "other@example.test", permission = "edit", grantVersion = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.DeleteAsync($"/api/documents/{host.DocumentId}/shares/{EndpointHost.OtherOwner}?version=1&grantVersion=1")).StatusCode);
        await host.WithDatabase(async db => { Assert.Equal(1, (await db.Documents.SingleAsync()).Version); Assert.Equal("read", (await db.DocumentGrants.SingleAsync()).Permission); });
    }
    [Fact]
    public async Task Reader_cannot_edit_or_access_private_history_but_editor_can_save_and_revocation_is_immediate()
    {
        await using var host = await EndpointHost.CreateAsync(); await Grant(host, "read"); Actor(host, EndpointHost.OtherOwner);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync($"/api/documents/{host.DocumentId}/structure?version=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/documents/{host.DocumentId}/versions?version=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/documents/{host.DocumentId}/shares?version=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.PostAsJsonAsync($"/api/documents/{host.DocumentId}/batch", new { version = 1, operations = new[] { new { type = "trackChanges", enabled = true } } })).StatusCode);
        Actor(host, EndpointHost.Owner); await Grant(host, "edit", 1); Actor(host, EndpointHost.OtherOwner);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsJsonAsync($"/api/documents/{host.DocumentId}/batch", new { version = 1, operations = new[] { new { type = "trackChanges", enabled = true } } })).StatusCode);
        Actor(host, EndpointHost.Owner);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync($"/api/documents/{host.DocumentId}/shares/{EndpointHost.OtherOwner}?version=2&grantVersion=2")).StatusCode);
        Actor(host, EndpointHost.OtherOwner);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/documents/{host.DocumentId}/structure?version=2")).StatusCode);
        var library = await host.Client.GetFromJsonAsync<JsonElement>("/api/library?q=Changed"); Assert.Equal(0, library.GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task Revocation_during_conversion_rolls_back_document_and_revision()
    {
        await using var host = await EndpointHost.CreateAsync(); await Grant(host, "edit"); Actor(host, EndpointHost.OtherOwner);
        host.Processor.BeforeAutomate = () => host.WithDatabase(db => db.DocumentGrants.ExecuteDeleteAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync($"/api/documents/{host.DocumentId}/batch", new { version = 1, operations = new[] { new { type = "trackChanges", enabled = true } } })).StatusCode);
        await host.WithDatabase(async db => { Assert.Equal(1, (await db.Documents.SingleAsync()).Version); Assert.Single(await db.DocumentRevisions.ToListAsync()); });
        Actor(host, EndpointHost.Owner);
        var results = await host.Client.GetFromJsonAsync<JsonElement>("/api/library?q=Changed"); Assert.Equal(0, results.GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task Sharing_does_not_expand_existing_scoped_token_access()
    {
        await using var host = await EndpointHost.CreateAsync(); await Grant(host, "edit"); Actor(host, EndpointHost.OtherOwner);
        var response = await host.Client.PostAsJsonAsync("/api/automation-tokens/", new { name = "Owned only", scopes = new[] { "documents:read", "documents:write" }, expiresInDays = 1 });
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        host.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/documents/{host.DocumentId}/structure?version=1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync($"/api/documents/{host.DocumentId}/shares?version=1")).StatusCode);
    }
    [Fact]
    public async Task Approval_requires_assigned_reviewer_current_revision_and_current_request_version()
    {
        await using var host = await EndpointHost.CreateAsync(); await Grant(host, "read");
        var created = await host.Client.PostAsJsonAsync($"/api/documents/{host.DocumentId}/reviews", new { version = 1, reviewerId = EndpointHost.OtherOwner });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode); var review = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = review.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.PostAsJsonAsync($"/api/reviews/{id}/decision", new { version = 1, documentVersion = 1, decision = "approved" })).StatusCode);
        Actor(host, EndpointHost.OtherOwner);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync($"/api/reviews/{id}/decision", new { version = 9, documentVersion = 1, decision = "approved" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsJsonAsync($"/api/reviews/{id}/decision", new { version = 1, documentVersion = 1, decision = "approved", note = "Reviewed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync($"/api/reviews/{id}/decision", new { version = 2, documentVersion = 1, decision = "returned" })).StatusCode);
        Actor(host, EndpointHost.Owner);
        await host.WithDatabase(async db => { var doc = await db.Documents.SingleAsync(); doc.Version++; doc.Title = "Updated"; await db.SaveChangesAsync(); });
        Actor(host, EndpointHost.OtherOwner);
        var rows = await host.Client.GetFromJsonAsync<JsonElement>("/api/reviews/"); Assert.Equal(2, rows.GetProperty("items")[0].GetProperty("currentVersion").GetInt64());
        Assert.Equal(1, rows.GetProperty("items")[0].GetProperty("documentVersion").GetInt64());
    }
    [Fact]
    public async Task Review_evidence_is_excluded_from_retention_and_revocation_hides_inbox_and_evidence()
    {
        await using var host = await EndpointHost.CreateAsync(); await Grant(host, "read");
        await host.WithDatabase(async db => { var doc = await db.Documents.SingleAsync(); for (var i = 2; i <= 3; i++) { doc.Version = i; await db.SaveChangesAsync(); } });
        var response = await host.Client.PostAsJsonAsync($"/api/documents/{host.DocumentId}/reviews", new { version = 3, reviewerId = EndpointHost.OtherOwner });
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await host.WithDatabase(async db => { var doc = await db.Documents.SingleAsync(); for (var i = 4; i <= 10; i++) { doc.Version = i; await db.SaveChangesAsync(); } await db.DocumentRevisions.ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedAt, 0)); });
        await host.Client.PutAsJsonAsync("/api/retention/", new { version = 0, enabled = true, keepLatest = 5, days = 7 });
        await host.Client.PostAsJsonAsync("/api/retention/apply", new { version = 1 });
        await host.WithDatabase(async db => Assert.True(await db.DocumentRevisions.AnyAsync(r => r.Version == 3)));
        Actor(host, EndpointHost.OtherOwner);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync($"/api/reviews/{id}/decision", new { version = 1, documentVersion = 10, decision = "approved" })).StatusCode);
        Actor(host, EndpointHost.Owner); await host.Client.DeleteAsync($"/api/documents/{host.DocumentId}/shares/{EndpointHost.OtherOwner}?version=10&grantVersion=1"); Actor(host, EndpointHost.OtherOwner);
        var rows = await host.Client.GetFromJsonAsync<JsonElement>("/api/reviews/"); Assert.Empty(rows.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/reviews/{id}/download?version=1")).StatusCode);
    }
}

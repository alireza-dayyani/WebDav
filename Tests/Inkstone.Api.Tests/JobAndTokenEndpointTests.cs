using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inkstone.Web.Data;
using Inkstone.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Api.Tests;

public sealed class JobAndTokenEndpointTests
{
    [Theory]
    [InlineData("{\"name\":\"\",\"scopes\":[\"documents:read\"]}", "name")]
    [InlineData("{\"name\":\"Token\",\"scopes\":null}", "scopes")]
    [InlineData("{\"name\":\"Token\",\"scopes\":[\"admin:write\"]}", "scopes")]
    [InlineData("{\"name\":\"Token\",\"scopes\":[\"documents:read\"],\"expiresInDays\":0}", "expiresInDays")]
    public async Task Invalid_token_requests_are_rejected_without_storing_secrets(string body, string field)
    {
        await using var host = await EndpointHost.CreateAsync();
        var response = await DocumentEndpointTests.Send(host, "POST", "/api/automation-tokens/", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(error.GetProperty("errors").TryGetProperty(field, out _));
        await host.WithDatabase(async db => Assert.Empty(await db.AutomationTokens.ToArrayAsync()));
    }
    [Theory]
    [InlineData("export", "source")]
    [InlineData("batch", "source")]
    [InlineData("compose", "sources")]
    [InlineData("renderBatch", "template")]
    public async Task Jobs_require_their_corresponding_source_contract(string kind, string expectedField)
    {
        await using var host = await EndpointHost.CreateAsync();
        var response = await host.Client.PostAsJsonAsync("/api/document-jobs/", new { kind });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(json.GetProperty("errors").TryGetProperty(expectedField, out _));
        Assert.Equal(0, host.Processor.Calls); await host.WithDatabase(async db => Assert.Empty(await db.DocumentJobs.ToArrayAsync()));
    }

    [Fact]
    public async Task Queue_captures_source_and_retry_does_not_duplicate_work()
    {
        await using var host = await EndpointHost.CreateAsync();
        var body = new { kind = "export", source = new ResourceRef(host.DocumentId, 1), format = "docx", idempotencyKey = "queue-once" };
        var first = await host.Client.PostAsJsonAsync("/api/document-jobs/", body); var retry = await host.Client.PostAsJsonAsync("/api/document-jobs/", body);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.Equal(await first.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        await host.WithDatabase(async db =>
        {
            var job = Assert.Single(await db.DocumentJobs.ToArrayAsync()); Assert.Equal("queued", job.Status);
            var work = JsonSerializer.Deserialize<JobWork>(job.Work, Studio.Json)!; Assert.Equal(FakeProcessor.Original, work.Input); Assert.Empty(work.Sources);
        });
        Assert.Equal(0, host.Processor.Calls);
    }

    [Fact]
    public async Task Cancel_enforces_owner_version_and_terminal_state_and_clears_snapshot()
    {
        await using var host = await EndpointHost.CreateAsync();
        var created = await host.Client.PostAsJsonAsync("/api/document-jobs/", new { kind = "export", source = new ResourceRef(host.DocumentId, 1) });
        var job = await created.Content.ReadFromJsonAsync<JsonElement>(); var path = "/api/document-jobs/" + job.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync(path + "/cancel", new { version = 2 })).StatusCode);
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner"); host.Client.DefaultRequestHeaders.Add("X-Test-Owner", EndpointHost.OtherOwner);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(path + "?version=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.PostAsJsonAsync(path + "/cancel", new { version = 1 })).StatusCode);
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner"); host.Client.DefaultRequestHeaders.Add("X-Test-Owner", EndpointHost.Owner);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsJsonAsync(path + "/cancel", new { version = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync(path + "/cancel", new { version = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.GetAsync(path + "/download?version=2")).StatusCode);
        await host.WithDatabase(async db => { var saved = await db.DocumentJobs.SingleAsync(); Assert.Equal("{}", saved.Work); Assert.Equal("cancelled", saved.Status); Assert.Empty(saved.Result); });
    }

    [Fact]
    public async Task Job_download_requires_exact_revision_and_owner()
    {
        await using var host = await EndpointHost.CreateAsync(); var id = Guid.NewGuid();
        await host.WithDatabase(async db => { db.DocumentJobs.Add(new DocumentJob { Id = id, OwnerId = EndpointHost.Owner, Kind = "export", Status = "succeeded", Version = 3, Format = "pdf", Result = "%PDF"u8.ToArray() }); await db.SaveChangesAsync(); });
        var path = $"/api/document-jobs/{id}";
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(path + "?version=1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync(path + "?version=0")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.GetAsync(path + "/download?version=2")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(path + "/download?version=3")).StatusCode);
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner"); host.Client.DefaultRequestHeaders.Add("X-Test-Owner", EndpointHost.OtherOwner);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(path + "/download?version=3")).StatusCode);
    }

    [Fact]
    public async Task Token_secret_is_returned_once_hashed_and_revocable()
    {
        await using var host = await EndpointHost.CreateAsync();
        var response = await host.Client.PostAsJsonAsync("/api/automation-tokens/", new { name = "Read token", scopes = new[] { "documents:read" } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); var token = await response.Content.ReadFromJsonAsync<JsonElement>();
        var secret = token.GetProperty("token").GetString()!; var id = token.GetProperty("id").GetGuid();
        await host.WithDatabase(async db => { var stored = await db.AutomationTokens.SingleAsync(); Assert.Equal(Automation.TokenHash(secret), stored.Hash); Assert.NotEqual(secret, stored.Hash); });
        var listing = await host.Client.GetStringAsync("/api/automation-tokens/"); Assert.DoesNotContain(secret, listing); Assert.DoesNotContain("hash", listing, StringComparison.OrdinalIgnoreCase);
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync($"/api/documents/{host.DocumentId}/structure?version=1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.PostAsJsonAsync($"/api/documents/{host.DocumentId}/batch", new { version = 1, operations = new[] { new { type = "trackChanges", enabled = true } } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/api/automation-tokens/")).StatusCode);
        host.Client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.DeleteAsync($"/api/automation-tokens/{id}?version=2")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync($"/api/automation-tokens/{id}?version=1")).StatusCode);
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/document-capabilities")).StatusCode);
    }

    [Theory]
    [InlineData("expired")] [InlineData("locked")] [InlineData("malformed")]
    public async Task Invalid_tokens_cannot_fall_back_to_test_cookie_identity(string state)
    {
        await using var host = await EndpointHost.CreateAsync(); var secret = "ink_" + new string('a', 64);
        await host.WithDatabase(async db =>
        {
            db.AutomationTokens.Add(new AutomationToken { OwnerId = EndpointHost.Owner, Name = "Token", Hash = Automation.TokenHash(secret), Scopes = "documents:read", ExpiresAt = state == "expired" ? 0 : DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds() });
            if (state == "locked") { var user = await db.Users.SingleAsync(u => u.Id == EndpointHost.Owner); user.LockoutEnabled = true; user.LockoutEnd = DateTimeOffset.UtcNow.AddDays(1); }
            await db.SaveChangesAsync();
        });
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", state == "malformed" ? "wrong" : secret);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/document-capabilities")).StatusCode);
    }

    [Fact]
    public async Task Invalid_bulk_records_create_no_documents_or_receipts()
    {
        await using var host = await EndpointHost.CreateAsync();
        var response = await host.Client.PostAsJsonAsync($"/api/templates/{host.TemplateId}/render-batch", new { version = 1, records = new[] { new { key = "a", title = "One", values = new { Name = "x" } }, new { key = "a", title = "Two", values = new { Name = "y" } } } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, host.Processor.Calls);
        await host.WithDatabase(async db => { Assert.Single(await db.Documents.ToArrayAsync()); Assert.Empty(await db.OperationReceipts.ToArrayAsync()); });
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Queued_job_quota_counts_only_the_authenticated_owner(bool sameOwner)
    {
        await using var host = await EndpointHost.CreateAsync();
        await host.WithDatabase(async db =>
        {
            for (var index = 0; index < 10; index++) db.DocumentJobs.Add(new DocumentJob { OwnerId = sameOwner ? EndpointHost.Owner : EndpointHost.OtherOwner, Kind = "export" });
            await db.SaveChangesAsync();
        });
        var response = await host.Client.PostAsJsonAsync("/api/document-jobs/", new { kind = "export", source = new ResourceRef(host.DocumentId, 1) });
        Assert.Equal(sameOwner ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK, response.StatusCode); Assert.Equal(0, host.Processor.Calls);
    }

    [Fact]
    public async Task Stale_job_and_template_sources_are_rejected_before_processing()
    {
        await using var host = await EndpointHost.CreateAsync();
        var job = await host.Client.PostAsJsonAsync("/api/document-jobs/", new { kind = "export", source = new ResourceRef(host.DocumentId, 2) });
        Assert.Equal(HttpStatusCode.Conflict, job.StatusCode);
        var bulk = await host.Client.PostAsJsonAsync($"/api/templates/{host.TemplateId}/render-batch", new { version = 2, records = new[] { new { key = "a", title = "One", values = new { Name = "x" } } } });
        Assert.Equal(HttpStatusCode.Conflict, bulk.StatusCode); Assert.Equal(0, host.Processor.Calls);
        await host.WithDatabase(async db => { Assert.Empty(await db.DocumentJobs.ToArrayAsync()); Assert.Single(await db.Documents.ToArrayAsync()); });
    }

    [Fact]
    public async Task Bulk_render_with_empty_values_copies_template_without_engine_work()
    {
        await using var host = await EndpointHost.CreateAsync();
        var response = await host.Client.PostAsJsonAsync($"/api/templates/{host.TemplateId}/render-batch", new { version = 1, records = new[] { new RenderRecord("copy", "Template copy", []) } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(0, host.Processor.Calls);
        await host.WithDatabase(async db => { var copy = await db.Documents.SingleAsync(d => d.Id != host.DocumentId); Assert.Equal(FakeProcessor.Original, copy.Content); });
    }

    [Theory]
    [InlineData("render-batch")] [InlineData("RENDER-BATCH")] [InlineData("render-batch/")] [InlineData("RENDER-BATCH/")]
    public async Task Template_write_scope_cannot_render_documents_through_route_case_or_slash_variants(string suffix)
    {
        await using var host = await EndpointHost.CreateAsync(); var secret = "ink_" + new string('b', 64);
        await host.WithDatabase(async db =>
        {
            db.AutomationTokens.Add(new AutomationToken { OwnerId = EndpointHost.Owner, Name = "Templates only", Hash = Automation.TokenHash(secret), Scopes = "templates:write", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds() });
            await db.SaveChangesAsync();
        });
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        var response = await host.Client.PostAsJsonAsync($"/api/templates/{host.TemplateId}/{suffix}", new { version = 1, records = new[] { new RenderRecord("copy", "Copy", []) } });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.Equal(0, host.Processor.Calls);
        await host.WithDatabase(async db => Assert.Single(await db.Documents.ToArrayAsync()));
    }

    [Theory]
    [InlineData("search")] [InlineData("SEARCH/")]
    public async Task Document_read_scope_allows_read_only_post_route_variants(string suffix)
    {
        await using var host = await EndpointHost.CreateAsync(); var secret = "ink_" + new string('c', 64);
        await host.WithDatabase(async db =>
        {
            db.AutomationTokens.Add(new AutomationToken { OwnerId = EndpointHost.Owner, Name = "Read", Hash = Automation.TokenHash(secret), Scopes = "documents:read", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds() });
            await db.SaveChangesAsync();
        });
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        var response = await host.Client.PostAsJsonAsync($"/api/documents/{host.DocumentId}/{suffix}", new { version = 1, text = "Original" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

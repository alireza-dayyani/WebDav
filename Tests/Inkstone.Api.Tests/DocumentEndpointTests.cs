using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inkstone.Web.Data;
using Inkstone.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Api.Tests;

public sealed class DocumentEndpointTests
{
    [Theory]
    [InlineData("images/b.0@0")]
    [InlineData("IMAGES/b.0@0/")]
    public async Task Image_routes_accept_case_and_trailing_slashes(string route)
    {
        await using var host = await EndpointHost.CreateAsync();
        using var response = await Send(host, "PUT", $"/api/documents/{host.DocumentId}/{route}", """{"version":1,"target":{},"data":"AA=="}""");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, host.Processor.Calls);
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        foreach (var (method, path, body, status) in new (string, string, string?, int)[] {
            ("GET", "/structure?version=0", null, 400), ("GET", "/tables?version=1&offset=-1", null, 400),
            ("GET", "/styles?version=1&limit=501", null, 400), ("GET", "/versions?version=1&page=0", null, 400),
            ("GET", "/pages/0/preview?version=1", null, 400), ("GET", "/pages/1/preview?version=1&scale=4", null, 400),
            ("GET", "/versions/0/download?version=1", null, 400), ("GET", "/versions/1/download?version=1&format=exe", null, 415),
            ("POST", "/search", """{"version":1,"text":null}""", 400),
            ("POST", "/search", """{"version":1,"text":"x","scope":"headers"}""", 400),
            ("POST", "/batch", """{"version":1,"operations":null}""", 400),
            ("POST", "/batch", """{"version":1,"operations":[{"type":"trackChanges","enabled":"yes"}]}""", 422),
            ("POST", "/batch", """{"version":1,"operations":[{"type":"replaceText","find":"x","replacement":"y","expectedMatch":1}]}""", 422),
            ("POST", "/preview", """{"version":1,"format":"exe","operations":[{"type":"trackChanges","enabled":true}]}""", 400),
            ("POST", "/preview", """{"version":1,"format":"png","scale":0,"operations":[{"type":"trackChanges","enabled":true}]}""", 400),
            ("POST", "/controls/fill", """{"version":1,"values":null}""", 400),
            ("PATCH", "/properties", """{"version":1,"values":{"unknown":"x"}}""", 422),
            ("POST", "/styles/apply", """{"version":1,"operations":[null]}""", 400),
            ("PATCH", "/tables/b.0", """{"version":1,"cells":[]}""", 422),
            ("PATCH", "/tables/not-a-ref", """{"version":1,"cells":[{"row":0,"column":0,"text":"x"}]}""", 400),
            ("POST", "/images", """{"version":1,"target":{},"data":null}""", 400),
            ("PUT", "/images/b.0@0", """{"version":1,"target":{},"data":"invalid"}""", 400),
            ("POST", "/comments", """{"version":1,"action":"reply","target":{},"text":"Reply"}""", 400),
            ("POST", "/comments", """{"version":1,"action":"unknown","target":{}}""", 400),
            ("POST", "/changes/resolve", """{"version":1,"decision":"accept","all":false,"target":{}}""", 400),
            ("POST", "/normalize", """{"version":1,"rules":null}""", 400),
            ("POST", "/restore", """{"version":1,"sourceVersion":0}""", 400),
            ("POST", "/validate", """{"version":-1}""", 400)
        }) yield return [method, path, body!, status];
    }

    [Theory, MemberData(nameof(InvalidRequests))]
    public async Task Invalid_requests_return_structured_errors_before_starting_engine(string method, string path, string? body, int status)
    {
        await using var host = await EndpointHost.CreateAsync();
        using var response = await Send(host, method, $"/api/documents/{host.DocumentId}" + path, body);
        Assert.Equal(status, (int)response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, problem.GetProperty("status").GetInt32()); Assert.NotEmpty(problem.GetProperty("errors").EnumerateObject());
        Assert.Equal(0, host.Processor.Calls);
        await host.WithDatabase(async db => { Assert.Equal(1, (await db.Documents.SingleAsync()).Version); Assert.Single(await db.DocumentRevisions.ToListAsync()); });
    }

    [Theory]
    [InlineData("structure")] [InlineData("tables")] [InlineData("controls")] [InlineData("styles")]
    [InlineData("images")] [InlineData("properties")] [InlineData("comments")] [InlineData("changes")]
    [InlineData("versions")] [InlineData("pages/1/preview")] [InlineData("versions/1/download")]
    public async Task Read_routes_enforce_owner_and_version(string route)
    {
        await using var host = await EndpointHost.CreateAsync();
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner"); host.Client.DefaultRequestHeaders.Add("X-Test-Owner", EndpointHost.OtherOwner);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/documents/{host.DocumentId}/{route}?version=1")).StatusCode);
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner"); host.Client.DefaultRequestHeaders.Add("X-Test-Owner", EndpointHost.Owner);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.GetAsync($"/api/documents/{host.DocumentId}/{route}?version=2")).StatusCode);
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner");
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync($"/api/documents/{host.DocumentId}/{route}?version=1")).StatusCode);
        Assert.Equal(0, host.Processor.Calls);
    }

    [Fact]
    public async Task Batch_saves_once_retains_history_and_replays_identical_requests()
    {
        await using var host = await EndpointHost.CreateAsync();
        var path = $"/api/documents/{host.DocumentId}/batch";
        const string body = """{"version":1,"idempotencyKey":"one","operations":[{"type":"properties","values":{"subject":"New"}}]}""";
        var first = await Send(host, "POST", path, body); var replay = await Send(host, "POST", path, body);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(1, host.Processor.Calls);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(host, "POST", path, body.Replace("New", "Other"))).StatusCode);
        await host.WithDatabase(async db =>
        {
            var document = await db.Documents.SingleAsync(); Assert.Equal(2, document.Version); Assert.Equal(FakeProcessor.Changed, document.Content);
            var history = await db.DocumentRevisions.OrderBy(r => r.Version).ToArrayAsync(); Assert.Equal(2, history.Length);
            Assert.Equal(FakeProcessor.Original, history[0].Content); Assert.Equal(FakeProcessor.Changed, history[1].Content); Assert.Single(await db.OperationReceipts.ToArrayAsync());
        });
    }

    public static IEnumerable<object[]> WriteRoutes()
    {
        foreach (var (method, route, body) in new[] {
            ("POST", "batch", """{"version":1,"operations":[{"type":"trackChanges","enabled":true}]}"""),
            ("POST", "preview", """{"version":1,"operations":[{"type":"trackChanges","enabled":true}]}"""),
            ("POST", "search", """{"version":1,"text":"Original"}"""),
            ("POST", "validate", """{"version":1}"""),
            ("POST", "controls/fill", """{"version":1,"values":{"Name":"Alice"}}"""),
            ("PATCH", "properties", """{"version":1,"values":{"subject":"Changed"}}"""),
            ("POST", "styles/apply", """{"version":1,"operations":[{"type":"format","target":{"ref":"b.0"},"values":{"bold":true}}]}"""),
            ("PATCH", "tables/b.0", """{"version":1,"cells":[{"row":0,"column":0,"text":"New"}]}"""),
            ("POST", "images", """{"version":1,"target":{"ref":"b.0"},"data":"AA=="}"""),
            ("PUT", "images/b.0@0", """{"version":1,"target":{},"data":"AA=="}"""),
            ("POST", "comments", """{"version":1,"action":"add","target":{"ref":"b.0"},"text":"Review"}"""),
            ("POST", "changes/resolve", """{"version":1,"decision":"accept","all":true,"target":{}}"""),
            ("POST", "normalize", """{"version":1,"rules":["persianLetters"],"apply":true}"""),
            ("POST", "restore", """{"version":1,"sourceVersion":1}""")
        }) yield return [method, route, body];
    }

    [Theory, MemberData(nameof(WriteRoutes))]
    public async Task Document_body_routes_enforce_owner_version_and_authentication(string method, string route, string body)
    {
        await using var host = await EndpointHost.CreateAsync(); var path = $"/api/documents/{host.DocumentId}/{route}";
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner"); host.Client.DefaultRequestHeaders.Add("X-Test-Owner", EndpointHost.OtherOwner);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(host, method, path, body)).StatusCode);
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner"); host.Client.DefaultRequestHeaders.Add("X-Test-Owner", EndpointHost.Owner);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(host, method, path, body.Replace("\"version\":1", "\"version\":2"))).StatusCode);
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(host, method, path, body)).StatusCode);
        Assert.Equal(0, host.Processor.Calls);
        await host.WithDatabase(async db => { Assert.Equal(1, (await db.Documents.SingleAsync()).Version); Assert.Empty(await db.OperationReceipts.ToArrayAsync()); Assert.Empty(await db.DocumentJobs.ToArrayAsync()); });
    }

    [Fact]
    public async Task Engine_failure_leaves_document_history_and_receipts_unchanged()
    {
        await using var host = await EndpointHost.CreateAsync(); host.Processor.Fail = true;
        var response = await Send(host, "POST", $"/api/documents/{host.DocumentId}/batch", """{"version":1,"idempotencyKey":"failed","operations":[{"type":"properties","values":{"subject":"X"}}]}""");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await host.WithDatabase(async db =>
        {
            var document = await db.Documents.SingleAsync(); Assert.Equal(1, document.Version); Assert.Equal(FakeProcessor.Original, document.Content);
            Assert.Single(await db.DocumentRevisions.ToArrayAsync()); Assert.Empty(await db.OperationReceipts.ToArrayAsync());
        });
    }

    [Fact]
    public async Task Preview_creates_private_artifact_without_saving_document()
    {
        await using var host = await EndpointHost.CreateAsync();
        var response = await Send(host, "POST", $"/api/documents/{host.DocumentId}/preview", """{"version":1,"operations":[{"type":"properties","values":{"subject":"Preview"}}]}""");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>(); var url = preview.GetProperty("downloadUrl").GetString();
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(url)).StatusCode);
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner"); host.Client.DefaultRequestHeaders.Add("X-Test-Owner", EndpointHost.OtherOwner);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(url)).StatusCode);
        await host.WithDatabase(async db => { Assert.Equal(1, (await db.Documents.SingleAsync()).Version); Assert.Single(await db.DocumentRevisions.ToArrayAsync()); });
    }

    [Theory]
    [InlineData("separator", "invalid")]
    [InlineData("stylePolicy", "invalid")]
    public async Task Single_source_composition_validates_options_instead_of_silently_copying(string name, string value)
    {
        await using var host = await EndpointHost.CreateAsync();
        var body = Automation.Json(new Dictionary<string, object?> { ["sources"] = new[] { new ResourceRef(host.DocumentId, 1) }, ["title"] = "Copy", [name] = value });
        var response = await host.Client.PostAsJsonAsync("/api/documents/compose", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, host.Processor.Calls);
        await host.WithDatabase(async db => Assert.Single(await db.Documents.ToArrayAsync()));
    }

    [Theory]
    [InlineData("/api/documents/compare")]
    [InlineData("/api/documents/compose")]
    public async Task Multi_document_routes_reject_other_owners_sources(string path)
    {
        await using var host = await EndpointHost.CreateAsync(); var source = new ResourceRef(host.DocumentId, 1);
        host.Client.DefaultRequestHeaders.Remove("X-Test-Owner"); host.Client.DefaultRequestHeaders.Add("X-Test-Owner", EndpointHost.OtherOwner);
        object request = path.EndsWith("compare", StringComparison.Ordinal) ? new { original = source, revised = source, title = "Compare" } : new { sources = new[] { source }, title = "Copy" };
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.PostAsJsonAsync(path, request)).StatusCode); Assert.Equal(0, host.Processor.Calls);
    }

    [Fact]
    public async Task Restore_copies_history_to_new_revision_and_detects_stale_version()
    {
        await using var host = await EndpointHost.CreateAsync();
        var path = $"/api/documents/{host.DocumentId}";
        await Send(host, "POST", path + "/batch", """{"version":1,"operations":[{"type":"properties","values":{"subject":"New"}}]}""");
        var response = await Send(host, "POST", path + "/restore", """{"version":2,"sourceVersion":1}"""); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(host, "POST", path + "/restore", """{"version":2,"sourceVersion":1}""")).StatusCode);
        await host.WithDatabase(async db => { var d = await db.Documents.SingleAsync(); Assert.Equal(3, d.Version); Assert.Equal(FakeProcessor.Original, d.Content); Assert.Equal(3, await db.DocumentRevisions.CountAsync()); });
    }

    [Theory]
    [InlineData("add")] [InlineData("reply")] [InlineData("resolve")] [InlineData("delete")]
    public async Task Comment_shortcuts_forward_only_fields_for_the_selected_action(string action)
    {
        await using var host = await EndpointHost.CreateAsync();
        var response = await host.Client.PostAsJsonAsync($"/api/documents/{host.DocumentId}/comments", new { version = 1, action, target = new { @ref = "b.0" }, text = "Review", id = 0, resolved = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(1, host.Processor.Calls);
        var operation = Automation.Json(host.Processor.LastParameters!).GetProperty("operations")[0];
        Assert.Equal(action == "add", operation.TryGetProperty("target", out _));
        Assert.Equal(action != "add", operation.TryGetProperty("id", out _));
        Assert.Equal(action is "add" or "reply", operation.TryGetProperty("text", out _));
        Assert.Equal(action == "resolve", operation.TryGetProperty("resolved", out _));
    }

    [Theory]
    [InlineData("""{"version":1,"idempotencyKeey":"retry","operations":[{"type":"trackChanges","enabled":true}]}""")]
    [InlineData("""{"version":1,"operations":[{"type":"trackChanges","enabled":true}],"sourcePath":"C:/private/file.docx"}""")]
    public async Task Unknown_request_properties_do_not_silently_disable_retry_guards(string body)
    {
        await using var host = await EndpointHost.CreateAsync();
        var response = await Send(host, "POST", $"/api/documents/{host.DocumentId}/batch", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, host.Processor.Calls);
    }

    internal static Task<HttpResponseMessage> Send(EndpointHost host, string method, string path, string? body)
        => host.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = body is null ? null : new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
}

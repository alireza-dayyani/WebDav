using System.Text.Json;
using Inkstone.Web.Services;

namespace Inkstone.Api.Tests;

public sealed class RequestValidationTests
{
    private static JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);
    private static readonly ResourceRef Source = new(Guid.NewGuid(), 1);

    public static IEnumerable<object[]> InvalidRequests()
    {
        yield return [new VersionRequest(0), "version"];
        yield return [new RestoreDocument(1, -1), "sourceVersion"];
        yield return [new DocumentSearch(1, null!), "text"];
        yield return [new DocumentSearch(1, ""), "text"];
        yield return [new DocumentSearch(1, "Hello", "headers"), "scope"];
        yield return [new DocumentPreview(1, [Json("{\"type\":\"properties\",\"values\":{\"subject\":\"x\"}}")], "html"), "format"];
        yield return [new DocumentPreview(1, [], "pdf", 0), "page"];
        yield return [new DocumentPreview(1, [], "png", 1, float.NaN), "scale"];
        yield return [new DocumentValues(1, default), "values"];
        yield return [new DocumentImage(1, Json("{}"), "not base64"), "image.data"];
        yield return [new CommentAction(1, "unknown", Json("{}")), "action"];
        yield return [new CommentAction(1, "add", Json("{\"ref\":\"b.0\"}")), "text"];
        yield return [new CommentAction(1, "reply", Json("{}"), "Reply"), "id"];
        yield return [new ResolveChanges(1, "unknown", true, Json("{}")), "decision"];
        yield return [new ResolveChanges(1, "accept", false, Json("{}")), "target"];
        yield return [new Normalization(1, null!), "rules"];
        yield return [new Normalization(1, ["latinDigits", "persianDigits"]), "rules"];
        yield return [new Normalization(1, ["persianLetters", "persianLetters"]), "rules"];
        yield return [new CompareDocuments(null!, Source, "Compare"), "original"];
        yield return [new CompareDocuments(new(Guid.Empty, 1), Source, "Compare"), "original"];
        yield return [new CompareDocuments(Source, Source, "Compare", "paragraph"), "level"];
        yield return [new AssembleDocuments([Source], "Copy", "invalid"), "separator"];
        yield return [new AssembleDocuments([Source], "Copy", "paragraph", "mergeAll"), "stylePolicy"];
        yield return [new AssembleDocuments([Source], "\nBad title"), "title"];
        yield return [new AssembleDocuments([], "Empty"), "sources"];
        yield return [new BulkRender(1, null!), "records"];
        yield return [new BulkRender(1, [new("a", "One", []), new("a", "Two", [])]), "records[1].key"];
        yield return [new BulkRender(1, [new("a", "\tInvalid", [])]), "records[0].title"];
        yield return [new TokenCreate("Token", ["admin:write"]), "scopes"];
        yield return [new TokenCreate("Token", ["documents:read", "documents:read"]), "scopes"];
        yield return [new TokenCreate("Token", ["documents:read"], 366), "expiresInDays"];
        yield return [new TokenCreate(new string('a', 81), ["documents:read"]), "name"];
        yield return [new JobRequest("unknown"), "kind"];
        yield return [new JobRequest("export"), "source"];
        yield return [new JobRequest("export", Source, Format: "exe"), "format"];
        yield return [new JobRequest("batch", Source), "operations"];
        yield return [new JobRequest("compose", Sources: [Source]), "sources"];
        yield return [new JobRequest("compose", Sources: [Source, Source], Separator: "invalid"), "separator"];
        yield return [new JobRequest("renderBatch", Template: Source), "records"];
        yield return [new JobRequest("export", Source, Operations: []), "operations"];
        yield return [new EditBatch(1, [Json("{\"type\":\"trackChanges\",\"enabled\":true}")], "bad\nkey"), "idempotencyKey"];
    }

    [Theory, MemberData(nameof(InvalidRequests))]
    public void Rejects_invalid_request_contracts(object request, string field)
    {
        var error = Assert.Throws<RequestValidationException>(() => RequestValidation.Validate(request));
        Assert.Contains(field, error.Errors.Keys); Assert.NotEmpty(error.Errors[field]);
    }

    [Theory]
    [InlineData(0.25f)] [InlineData(2f)]
    public void Accepts_render_scale_boundaries(float scale) => RequestValidation.Scale(scale);

    [Theory]
    [InlineData(-1f)] [InlineData(0f)] [InlineData(2.01f)] [InlineData(float.PositiveInfinity)]
    public void Rejects_invalid_render_scale(float scale) => Assert.Throws<RequestValidationException>(() => RequestValidation.Scale(scale));

    [Theory]
    [InlineData(1)] [InlineData(365)]
    public void Accepts_token_lifetime_boundaries(int days) => RequestValidation.Validate(new TokenCreate("Service", ["documents:write"], days));

    [Fact]
    public void Accepts_persian_text_and_newlines_but_rejects_invalid_unicode()
    {
        RequestValidation.Text("سلام\nعلی\tاردو", "text");
        Assert.Throws<RequestValidationException>(() => RequestValidation.Text("bad\0text", "text"));
        Assert.Throws<RequestValidationException>(() => RequestValidation.Text("\uD800", "text"));
    }

    [Fact]
    public void Text_limit_counts_utf8_bytes()
    {
        RequestValidation.Text(new string('ع', 256 * 1024), "text");
        var error = Assert.Throws<RequestValidationException>(() => RequestValidation.Text(new string('ع', 256 * 1024 + 1), "text"));
        Assert.Equal(413, error.Status);
    }

    [Fact]
    public void Source_count_boundaries_and_versions_are_checked()
    {
        RequestValidation.Sources(Enumerable.Repeat(Source, 32).ToArray());
        Assert.Throws<RequestValidationException>(() => RequestValidation.Sources(Enumerable.Repeat(Source, 33).ToArray()));
        Assert.Throws<RequestValidationException>(() => RequestValidation.Sources([Source with { Version = 0 }]));
        Assert.Throws<RequestValidationException>(() => RequestValidation.Sources([null!]));
    }

    [Fact]
    public void Combined_bookmark_text_and_record_count_are_bounded()
    {
        var records = Enumerable.Range(0, 100).Select(index => new RenderRecord(index.ToString(), "Title", [])).ToArray();
        RequestValidation.Records(records);
        Assert.Throws<RequestValidationException>(() => RequestValidation.Records(records.Append(new("extra", "Title", [])).ToArray()));
        var values = Automation.Json(new { A = new string('a', 256 * 1024), B = new string('b', 256 * 1024 + 1) });
        Assert.Equal(413, Assert.Throws<RequestValidationException>(() => OperationValidation.BookmarkValues(values, "values")).Status);
    }
}

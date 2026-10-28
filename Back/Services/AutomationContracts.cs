using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

public sealed record AutomationResult(byte[] Content, JsonElement Report);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EditBatch(long Version, JsonElement[] Operations, string? IdempotencyKey = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DocumentSearch(long Version, string Text, string Scope = "body", bool MatchCase = true, bool WholeWord = false);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DocumentPreview(long Version, JsonElement[] Operations, string Format = "pdf", int Page = 1, float Scale = 1);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DocumentValues(long Version, JsonElement Values, string? IdempotencyKey = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TableUpdate(long Version, JsonElement[]? Cells = null, string[][]? AppendRows = null, string? IdempotencyKey = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DocumentImage(long Version, JsonElement Target, string Data, float Width = 144, float? Height = null, string Alt = "", string? IdempotencyKey = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CommentAction(long Version, string Action, JsonElement Target, string? Text = null, uint? Id = null, bool Resolved = true, string? IdempotencyKey = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResolveChanges(long Version, string Decision, bool All, JsonElement Target, bool StopTracking = false, string? IdempotencyKey = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Normalization(long Version, string[] Rules, bool Apply = false, string? IdempotencyKey = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RestoreDocument(long Version, long SourceVersion, string? IdempotencyKey = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompareDocuments(ResourceRef Original, ResourceRef Revised, string Title, string Level = "word", bool CaseChanges = true, bool WhiteSpace = true, bool Formatting = true, string? IdempotencyKey = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssembleDocuments(ResourceRef[] Sources, string Title, string Separator = "paragraph", string StylePolicy = "preserveSource", string? IdempotencyKey = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BulkRender(long Version, RenderRecord[] Records, string? IdempotencyKey = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RenderRecord(string Key, string Title, Dictionary<string, string> Values);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TokenCreate(string Name, string[] Scopes, int ExpiresInDays = 30);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VersionRequest(long Version);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record JobRequest(string Kind, ResourceRef? Source = null, JsonElement[]? Operations = null, string Format = "pdf", ResourceRef[]? Sources = null, string Separator = "paragraph", string? IdempotencyKey = null, ResourceRef? Template = null, RenderRecord[]? Records = null);
public sealed record JobWork(byte[] Input, byte[][] Sources, JsonElement Parameters);

public static class Automation
{
    public static readonly string[] Operations = ["replaceText", "insertText", "replaceRange", "insertParagraph", "format", "fillBookmarks", "updateTable", "fillControls", "insertImage", "replaceImage", "properties", "normalize", "addComment", "replyComment", "resolveComment", "deleteComment", "trackChanges", "resolveChanges"];
    public static readonly string[] Scopes = ["documents:read", "documents:write", "templates:read", "templates:write", "jobs:read", "jobs:write"];
    public static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, Studio.Json);
    public static DocumentMetadata Metadata(StoredDocument d) => new(d.Id, d.Title, d.Version, d.CreatedAt, d.UpdatedAt, d.Content.Length);
    public static void Touch(StoredDocument d) { d.Version++; d.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }
    public static string Hash(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Studio.Json)));
    public static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static Task<StoredDocument?> Find(AppDbContext db, string owner, Guid id, CancellationToken ct) => db.Documents.SingleOrDefaultAsync(d => d.Id == id && d.OwnerId == owner, ct);
    public static void ValidateOperations(JsonElement[]? operations)
    {
        if (operations is not { Length: > 0 and <= 256 }) RequestValidation.Fail("operations", "Supply 1–256 operations.");
        for (var index = 0; index < operations!.Length; index++)
        {
            var operation = operations[index];
            if (operation.ValueKind != JsonValueKind.Object || !operation.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || !Operations.Contains(type.GetString()))
                RequestValidation.Fail($"operations[{index}].type", "Unknown operation type.");
            ValidateObjectKeys(operation);
            OperationValidation.Validate(operation, $"operations[{index}]");
        }
        if (JsonSerializer.SerializeToUtf8Bytes(new { operations }, Studio.Json).Length > 3 * FileLimits.MiB)
            RequestValidation.Fail("operations", "Operations exceed the 3 MiB processing limit.", 413);
    }
    private static void ValidateObjectKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) { foreach (var element in value.EnumerateArray()) ValidateObjectKeys(element); }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!keys.Add(property.Name)) RequestValidation.Fail("operations", "Duplicate JSON keys are not allowed in operations.");
                ValidateObjectKeys(property.Value);
            }
        }
    }
    public static void ValidateKey(string? key)
    {
        if (key is not null && (key.Length is < 1 or > 128 || key.Any(char.IsControl))) throw new ConversionException("Idempotency key must be 1–128 printable characters.", 400);
    }
    public static async Task<IResult?> Replay(AppDbContext db, string owner, string scope, string? key, object request, CancellationToken ct)
    {
        ValidateKey(key);
        if (key is null) return null;
        var receipt = await db.OperationReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerId == owner && x.Scope == scope && x.Key == key, ct);
        if (receipt is null) return null;
        if (receipt.Hash != Hash(request)) return Studio.Bad("This idempotency key was already used with different input.", 409);
        return Results.Content(receipt.Response, "application/json", Encoding.UTF8);
    }
    public static void Receipt(AppDbContext db, string owner, string scope, string? key, object request, object response)
    {
        if (key is not null) db.OperationReceipts.Add(new OperationReceipt { OwnerId = owner, Scope = scope, Key = key, Hash = Hash(request), Response = Studio.Encode(response) });
    }
    public static async Task<IResult> Save(AppDbContext db, object response, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return Results.Ok(response); }
        catch (DbUpdateConcurrencyException) { return Studio.Conflict(); }
        catch (DbUpdateException e) when (e.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 }) { return Studio.Conflict(); }
    }
    public static async Task<byte[][]> ReadSources(AppDbContext db, string owner, ResourceRef[]? sources, CancellationToken ct)
    {
        if (sources is not { Length: > 0 and <= 32 }) throw new ConversionException("Supply 1–32 owned document revisions.", 400);
        var result = new List<byte[]>(); long total = 0;
        foreach (var reference in sources)
        {
            RequestValidation.Source(reference, "sources");
            var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == reference.Id && d.OwnerId == owner, ct)
                ?? throw new ConversionException("Source document not found.", 404);
            if (document.Version != reference.Version) throw new ConversionException("A source document changed.", 409);
            total += document.Content.Length;
            if (total > FileLimits.MaxModelBytes) throw new ConversionException("Combined sources exceed 256 MiB.", 413);
            result.Add(document.Content);
        }
        return result.ToArray();
    }
    public static object JobSummary(DocumentJob job) => new { job.Id, job.Kind, job.Status, job.Version, job.CreatedAt, job.UpdatedAt, job.Format, error = job.Error.Length == 0 ? null : job.Error,
        progress = new { stage = job.Status }, report = JsonSerializer.Deserialize<JsonElement>(job.Report), downloadUrl = job.Status == "succeeded" && job.Result.Length > 0 ? $"/api/document-jobs/{job.Id}/download?version={job.Version}" : null };
}

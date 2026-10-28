using System.Text.Json;
using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

public sealed record ResourceRef(Guid Id, long Version);
public sealed record TemplateConfig(string Kind, string Language, Dictionary<string, string> Fields, string[] RequiredFields);
public sealed record CreateTemplate(string? Title, string? Kind, string? Language, ResourceRef Source, Dictionary<string, string>? Fields = null, string[]? RequiredFields = null);
public sealed record StarterTemplate(string? Title, string Kind = "letter", string Language = "en-US", string? Paper = null);
public sealed record UpdateTemplate(long Version, string? Title, Dictionary<string, string>? Fields, string[]? RequiredFields);
public sealed record RenderTemplate(long Version, string? Title, Dictionary<string, string>? Values);
public sealed record SenderProfile(string FullName, string Title, string Organization, string Address);
public sealed record SaveSender(long Version, string? FullName, string? Title, string? Organization, string? Address);
public sealed record ImageDescriptor(string Mime, int Width, int Height);
public sealed record LetterRecipient(string UserId, string Kind = "to", bool PrintOnLetter = true);
public sealed record LetterDraft(
    string Title, string Subject, string Language, string Date,
    ResourceRef? Template, ResourceRef? Sender, ResourceRef? Signature, ResourceRef? Logo,
    ResourceRef? BodyDocument, string BodyText, LetterRecipient[] Recipients,
    ResourceRef[] Attachments, string Number = "", string Priority = "normal", string Classification = "normal",
    bool IncludeQrCode = true, bool CopyVariants = false);
public sealed record SaveLetter(long Version, LetterDraft Draft);
public sealed record GenerateLetter(long Version, string IdempotencyKey);
public sealed record ValidationIssue(string Code, string Field, string Message);
public sealed record ValidationReport(bool Valid, long Version, ValidationIssue[] Issues);
public sealed record ResourceSummary(Guid Id, string Kind, string Title, long Version, long CreatedAt, long UpdatedAt, string State, bool HasEditor = false);
public sealed record ResourceDetail(ResourceSummary Metadata, JsonElement Data, JsonElement? Snapshot);
public sealed record RecipientSnapshot(string UserId, string Name, string Email, string Kind, bool PrintOnLetter);
public sealed record ArtifactInfo(Guid Id, string Key, string Title);
public sealed record GenerationResult(ResourceSummary Metadata, ArtifactInfo[] Artifacts);

public static class Studio
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 256 };
    public static readonly string[] Fields = ["Organization", "Logo", "Date", "Number", "Classification", "Priority", "Subject", "Recipients", "Body", "SenderName", "SenderTitle", "Signature", "Copies", "Attachments", "Address", "Tracking", "QRCode"];
    public static readonly string[] Required = ["Subject", "Recipients", "Body", "SenderName", "SenderTitle", "Signature"];
    public static ResourceSummary Summary(WorkspaceItem item) => new(item.Id, item.Kind, item.Title, item.Version, item.CreatedAt, item.UpdatedAt, item.Kind == "letter" ? item.Snapshot.Length == 0 ? "draft" : "generated" : "ready", item.Kind == "letter" && item.Content.Length > 0);
    public static ResourceDetail Detail(WorkspaceItem item) => new(Summary(item), JsonSerializer.Deserialize<JsonElement>(item.Data, Json), item.Snapshot.Length == 0 ? null : JsonSerializer.Deserialize<JsonElement>(item.Snapshot, Json));
    public static T Read<T>(WorkspaceItem item) => JsonSerializer.Deserialize<T>(item.Data, Json)!;
    public static string Encode<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static string? Title(string? value, int max = 160) => value?.Trim() is { Length: > 0 } text && text.Length <= max && !text.Any(c => c == '\uFFFC' || char.IsControl(c)) ? text : null;
    public static IResult Bad(string message, int status = 400) => Results.Problem(message, statusCode: status);
    public static IResult Conflict() => Bad("This record changed. Refresh it and use the current version.", 409);
    public static Task<WorkspaceItem?> Find(AppDbContext db, string owner, string kind, Guid id, CancellationToken ct, bool tracked = true)
        => (tracked ? db.WorkspaceItems : db.WorkspaceItems.AsNoTracking()).SingleOrDefaultAsync(x => x.OwnerId == owner && x.Kind == kind && x.Id == id, ct);
    public static void Touch(WorkspaceItem item) { item.Version++; item.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }
    public static async Task<bool> Save(AppDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
        catch (DbUpdateException error) when (error.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 }
            && error.Entries.Any(entry => entry.Entity is LetterArtifact)) { return false; }
    }
    public static async Task<IResult> Delete(AppDbContext db, string owner, string kind, Guid id, long version, CancellationToken ct)
    {
        var item = await Find(db, owner, kind, id, ct);
        if (item is null) return Results.NotFound();
        if (item.Version != version) return Conflict();
        db.WorkspaceItems.Remove(item);
        return await Save(db, ct) ? Results.NoContent() : Conflict();
    }
    public static bool ValidConfig(TemplateConfig config) => config.Fields.Count <= 32 && config.RequiredFields.Length <= 32
        && config.Fields.All(x => Fields.Contains(x.Key) && Title(x.Value, 256) is not null)
        && config.Fields.Values.Distinct(StringComparer.Ordinal).Count() == config.Fields.Count
        && config.RequiredFields.All(x => Fields.Contains(x) && config.Fields.ContainsKey(x));
    public static async Task<IResult> List(AppDbContext db, string owner, string kind, int? page, CancellationToken ct)
    {
        var index = page ?? 1;
        if (index is < 1 or > 100000) return Bad("Invalid page.");
        var rows = await db.WorkspaceItems.AsNoTracking().Where(x => x.OwnerId == owner && x.Kind == kind)
            .OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).Skip((index - 1) * 50).Take(51)
            .Select(x => new { x.Id, x.Kind, x.Title, x.Version, x.CreatedAt, x.UpdatedAt, Data = kind == "letter" ? "{}" : x.Data, Generated = x.Snapshot != "", HasEditor = kind == "letter" && x.Content.Length > 0 }).ToListAsync(ct);
        return Results.Ok(new { page = index, hasMore = rows.Count > 50, items = rows.Take(50).Select(x => new {
            metadata = new ResourceSummary(x.Id, x.Kind, x.Title, x.Version, x.CreatedAt, x.UpdatedAt, kind == "letter" ? x.Generated ? "generated" : "draft" : "ready", x.HasEditor),
            data = JsonSerializer.Deserialize<JsonElement>(x.Data, Json)
        }) });
    }
    public static IResult PdfPreview(byte[] content, HttpResponse response)
    {
        response.Headers.ContentDisposition = "inline";
        return Results.File(content, "application/pdf");
    }
    public static IResult File(byte[] content, string title, string format)
    {
        var name = string.Concat(title.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        var mime = format switch { "pdf" => "application/pdf", "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "json" => "application/json", _ => "application/octet-stream" };
        return Results.File(content, mime, (name.Length == 0 ? "letter" : name) + "." + format);
    }
}

using System.Security.Claims;
using System.Text.Json;
using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

public static class DocumentAutomationEndpoints
{
    public static void MapDocumentAutomation(this WebApplication app)
    {
        app.MapGet("/api/document-capabilities", () => Results.Ok(new {
            apiVersion = 1, operations = Automation.Operations, scopes = Automation.Scopes, offsetUnit = "utf8Bytes", referenceLifetime = "documentVersion",
            limits = new { maxOperations = 256, maxOperationBytes = 3 * FileLimits.MiB, maxSources = 32, maxBatchRecords = 100, maxImageBytes = 512 * 1024, maxReportBytes = 2 * FileLimits.MiB },
            normalizationRules = new[] { "persianLetters", "persianDigits", "latinDigits", "trimTrailingSpaces", "halfSpace" },
            contentControls = new[] { "text", "richText", "checkBox", "comboBox", "dropDown", "date", "picture", "repeatingSectionBodyParagraphs" },
            comparison = new { scopes = new[] { "body", "tables" }, levels = new[] { "word", "character" }, formatting = true },
            assembly = new { content = "body", stylePolicies = new[] { "preserveSource" }, separators = new[] { "paragraph", "pageBreak" } },
            jobKinds = new[] { "export", "batch", "compose", "renderBatch" }, previewFormats = new[] { "pdf", "png" }, jobRetentionHours = 24,
            validationChecks = new[] { "documentHealth", "accessibility", "bookmarkRanges", "placeholderMarkers", "contentControlPlaceholders", "fontAvailability", "internalLinks", "bookmarkReferenceFields" },
            fidelity = "Imported DOCX uses the native model; unsupported Word features may not round-trip losslessly."
        })).RequireAuthorization().WithTags("Document automation");
        var routes = app.MapGroup("/api/documents/{id:guid}").RequireAuthorization().WithTags("Document automation").WithMetadata(new BookmarkValuesRequest()).ValidateRequests();
        foreach (var action in new[] { "structure", "tables", "controls", "styles", "images", "properties", "comments", "changes" })
        {
            var queryAction = action;
            routes.MapGet("/" + action, async (Guid id, long version, int? offset, int? limit, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
                => offset < 0 || limit is < 1 or > 500 ? Studio.Bad("Offset must be nonnegative and limit must be 1–500.") : await Query(id, version, new { action = queryAction, offset = offset ?? 0, limit = limit ?? 200 }, user, db, converter, ct))
                .WithSummary($"Read {action} from the expected document revision").ProducesProblem(409).Produces(404);
        }
        routes.MapPost("/search", async (Guid id, DocumentSearch input, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
            => await Query(id, input.Version, new { action = "search", input.Text, input.Scope, input.MatchCase, input.WholeWord }, user, db, converter, ct));
        routes.MapPost("/validate", async (Guid id, VersionRequest input, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
            => await Query(id, input.Version, new { action = "validate" }, user, db, converter, ct));
        routes.MapPost("/batch", Batch).WithSummary("Apply typed operations atomically, saving one new version");
        routes.MapPost("/preview", Preview).WithSummary("Preview a proposed batch as PDF without saving the document");
        routes.MapGet("/pages/{page:int}/preview", async (Guid id, int page, long version, float? scale, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct) =>
        {
            var document = await Automation.Find(db, Ownership.Owner(user), id, ct);
            if (document is null) return Results.NotFound();
            if (document.Version != version) return Studio.Conflict();
            return Results.File(await converter.RenderPageAsync(document.Content, page, scale ?? 1, ct), "image/png");
        }).WithSummary("Render a saved document page as a PNG image");
        routes.MapPost("/controls/fill", async (Guid id, DocumentValues input, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
            => await Batch(id, new(input.Version, [Automation.Json(new { type = "fillControls", input.Values })], input.IdempotencyKey), user, db, converter, ct));
        routes.MapPatch("/properties", async (Guid id, DocumentValues input, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
            => await Batch(id, new(input.Version, [Automation.Json(new { type = "properties", input.Values })], input.IdempotencyKey), user, db, converter, ct));
        routes.MapPost("/styles/apply", async (Guid id, EditBatch input, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct) =>
        {
            Automation.ValidateOperations(input.Operations);
            if (input.Operations.Any(x => x.GetProperty("type").GetString() != "format")) return Studio.Bad("Styles accepts format operations only.");
            return await Batch(id, input, user, db, converter, ct);
        });
        routes.MapPatch("/tables/{reference}", async (Guid id, string reference, TableUpdate input, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
            => await Batch(id, new(input.Version, [Automation.Json(new { type = "updateTable", @ref = reference, input.Cells, input.AppendRows })], input.IdempotencyKey), user, db, converter, ct));
        routes.MapPost("/images", async (Guid id, DocumentImage input, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
            => await Batch(id, new(input.Version, [Automation.Json(new { type = "insertImage", input.Target, input.Data, input.Width, input.Height, input.Alt })], input.IdempotencyKey), user, db, converter, ct));
        routes.MapPut("/images/{reference}", async (Guid id, string reference, DocumentImage input, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
            => await Batch(id, new(input.Version, [Automation.Json(new { type = "replaceImage", @ref = reference, input.Data, input.Width, input.Height, input.Alt })], input.IdempotencyKey), user, db, converter, ct));
        routes.MapPost("/comments", Comment);
        routes.MapPost("/changes/resolve", async (Guid id, ResolveChanges input, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
            => await Batch(id, new(input.Version, [Automation.Json(new { type = "resolveChanges", input.Decision, input.All, input.Target, input.StopTracking })], input.IdempotencyKey), user, db, converter, ct));
        routes.MapPost("/normalize", async (Guid id, Normalization input, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
            => input.Apply ? await Batch(id, new(input.Version, [Automation.Json(new { type = "normalize", input.Rules })], input.IdempotencyKey), user, db, converter, ct)
                : await Query(id, input.Version, new { action = "normalization", input.Rules }, user, db, converter, ct));
        routes.MapGet("/versions", Versions);
        routes.MapGet("/versions/{revision:long}/download", DownloadRevision);
        routes.MapPost("/restore", Restore);
        app.MapPost("/api/documents/compare", Compare).RequireAuthorization().WithTags("Document automation").WithMetadata(new BookmarkValuesRequest()).ValidateRequests();
        app.MapPost("/api/documents/compose", Assemble).RequireAuthorization().WithTags("Document automation").WithMetadata(new BookmarkValuesRequest()).ValidateRequests();
        app.MapPost("/api/templates/{id:guid}/render-batch", RenderBatch).RequireAuthorization().WithTags("Templates").WithMetadata(new BookmarkValuesRequest()).ValidateRequests();
    }

    private static async Task<IResult> Query(Guid id, long version, object parameters, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
    {
        var document = await Automation.Find(db, Ownership.Owner(user), id, ct);
        if (document is null) return Results.NotFound();
        if (document.Version != version) return Studio.Conflict();
        var result = await converter.AutomateAsync(document.Content, "query", parameters, ct);
        return Results.Ok(new { id, version, result = result.Report });
    }

    public static async Task<IResult> Batch(Guid id, EditBatch input, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
    {
        var owner = Ownership.Owner(user);
        var document = await Automation.Find(db, owner, id, ct);
        if (document is null) return Results.NotFound();
        var scope = $"document:{id}:batch";
        if (await Automation.Replay(db, owner, scope, input.IdempotencyKey, input, ct) is { } replay) return replay;
        if (document.Version != input.Version) return Studio.Conflict();
        Automation.ValidateOperations(input.Operations);
        var result = await converter.AutomateAsync(document.Content, "batch", new { input.Operations, actor = user.Identity?.Name }, ct);
        document.Content = result.Content; Automation.Touch(document);
        var response = new { metadata = Automation.Metadata(document), report = result.Report };
        Automation.Receipt(db, owner, scope, input.IdempotencyKey, input, response);
        return await Automation.Save(db, response, ct);
    }

    private static async Task<IResult> Preview(Guid id, DocumentPreview input, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
    {
        var document = await Automation.Find(db, Ownership.Owner(user), id, ct);
        if (document is null) return Results.NotFound();
        if (document.Version != input.Version) return Studio.Conflict();
        if (await db.DocumentJobs.CountAsync(j => j.OwnerId == document.OwnerId, ct) >= 100) return Studio.Bad("At most 100 retained jobs/previews per account; results expire after 24 hours.", 429);
        Automation.ValidateOperations(input.Operations);
        var result = await converter.AutomateAsync(document.Content, "batch", new { input.Operations, actor = user.Identity?.Name }, ct);
        if (input.Format is not ("pdf" or "png")) return Studio.Bad("Preview format must be pdf or png.");
        var output = input.Format == "png" ? await converter.RenderPageAsync(result.Content, input.Page, input.Scale, ct) : await converter.ExportAsync(result.Content, "pdf", ct);
        var job = new DocumentJob { OwnerId = document.OwnerId, Kind = "preview", Status = "succeeded", Result = output, Format = input.Format, Report = Studio.Encode(new { documentId = id, baseVersion = input.Version, result = result.Report }) };
        db.DocumentJobs.Add(job); await db.SaveChangesAsync(ct);
        return Results.Ok(Automation.JobSummary(job));
    }

    private static Task<IResult> Comment(Guid id, CommentAction input, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
    {
        var type = input.Action switch { "add" => "addComment", "reply" => "replyComment", "resolve" => "resolveComment", "delete" => "deleteComment", _ => throw new ConversionException("Unknown comment action.", 400) };
        var operation = new Dictionary<string, object?> { ["type"] = type };
        if (input.Action == "add") operation["target"] = input.Target;
        else operation["id"] = input.Id;
        if (input.Action is "add" or "reply") operation["text"] = input.Text;
        if (input.Action == "resolve") operation["resolved"] = input.Resolved;
        return Batch(id, new(input.Version, [Automation.Json(operation)], input.IdempotencyKey), user, db, converter, ct);
    }

    private static async Task<IResult> Versions(Guid id, long version, int? page, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var document = await Automation.Find(db, Ownership.Owner(user), id, ct);
        if (document is null) return Results.NotFound();
        if (document.Version != version) return Studio.Conflict();
        var index = page ?? 1; if (index is < 1 or > 100000) return Studio.Bad("Invalid page.");
        var items = await db.DocumentRevisions.AsNoTracking().Where(x => x.DocumentId == id).OrderByDescending(x => x.Version).Skip((index - 1) * 50).Take(51).Select(x => new { x.Version, x.Title, x.CreatedAt, size = x.Content.Length }).ToListAsync(ct);
        return Results.Ok(new { id, version, page = index, hasMore = items.Count > 50, items = items.Take(50), historyStartsAtFirstSaveAfterUpgrade = true });
    }

    private static async Task<IResult> DownloadRevision(Guid id, long revision, long version, string? format, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
    {
        var document = await Automation.Find(db, Ownership.Owner(user), id, ct);
        if (document is null) return Results.NotFound();
        if (document.Version != version) return Studio.Conflict();
        var snapshot = await db.DocumentRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.DocumentId == id && x.Version == revision, ct);
        if (snapshot is null) return Results.NotFound();
        return Studio.File(await converter.ExportAsync(snapshot.Content, format ?? "docx", ct), snapshot.Title, format ?? "docx");
    }

    private static async Task<IResult> Restore(Guid id, RestoreDocument input, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var owner = Ownership.Owner(user); var document = await Automation.Find(db, owner, id, ct);
        if (document is null) return Results.NotFound();
        var scope = $"document:{id}:restore";
        if (await Automation.Replay(db, owner, scope, input.IdempotencyKey, input, ct) is { } replay) return replay;
        if (document.Version != input.Version) return Studio.Conflict();
        var revision = await db.DocumentRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.DocumentId == id && x.Version == input.SourceVersion, ct);
        if (revision is null) return Results.NotFound();
        document.Content = revision.Content.ToArray(); document.Title = revision.Title; Automation.Touch(document);
        var response = Automation.Metadata(document); Automation.Receipt(db, owner, scope, input.IdempotencyKey, input, response);
        return await Automation.Save(db, response, ct);
    }

    private static async Task<IResult> Compare(CompareDocuments input, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
    {
        var owner = Ownership.Owner(user);
        if (input.Original is null || input.Revised is null || Studio.Title(input.Title) is not { } title || input.Level is not ("word" or "character")) return Studio.Bad("Choose two owned source revisions, a title and word/character comparison.");
        if (await Automation.Replay(db, owner, "compare", input.IdempotencyKey, input, ct) is { } replay) return replay;
        var sources = await Automation.ReadSources(db, owner, [input.Original, input.Revised], ct);
        foreach (var source in sources)
        {
            var health = await converter.PreflightAsync(source, ct);
            if (health.GetProperty("protected").GetBoolean() || health.GetProperty("pendingRevisions").GetBoolean()) return Studio.Bad("Comparison requires unprotected sources with no pending tracked changes.", 422);
        }
        var result = await converter.AutomateAsync(sources[0], "compare", new { input.Level, input.CaseChanges, input.WhiteSpace, input.Formatting, author = user.Identity?.Name ?? "Document comparison", showIn = "original" }, ct, [sources[1]]);
        var document = new StoredDocument { OwnerId = owner, Title = title, Content = result.Content }; db.Documents.Add(document);
        var response = new { metadata = Automation.Metadata(document), report = result.Report, warnings = new[] { "Comparison covers body text, tables and supported formatting; headers, footers and other stories are not compared." } };
        Automation.Receipt(db, owner, "compare", input.IdempotencyKey, input, response); return await Automation.Save(db, response, ct);
    }

    private static async Task<IResult> Assemble(AssembleDocuments input, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
    {
        var owner = Ownership.Owner(user); if (Studio.Title(input.Title) is not { } title) return Studio.Bad("A title is required.");
        if (await Automation.Replay(db, owner, "compose", input.IdempotencyKey, input, ct) is { } replay) return replay;
        var sources = await Automation.ReadSources(db, owner, input.Sources, ct);
        var result = sources.Length == 1 ? new AutomationResult(sources[0], Automation.Json(new { assembled = 1 })) : await converter.AutomateAsync(sources[0], "assemble", new { input.Separator, input.StylePolicy }, ct, sources.Skip(1).ToArray());
        var document = new StoredDocument { OwnerId = owner, Title = title, Content = result.Content }; db.Documents.Add(document);
        var response = new { metadata = Automation.Metadata(document), report = result.Report }; Automation.Receipt(db, owner, "compose", input.IdempotencyKey, input, response);
        return await Automation.Save(db, response, ct);
    }

    private static async Task<IResult> RenderBatch(Guid id, BulkRender input, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
    {
        var owner = Ownership.Owner(user); var item = await Studio.Find(db, owner, "template", id, ct, false);
        if (item is null) return Results.NotFound();
        var scope = $"template:{id}:render-batch";
        if (await Automation.Replay(db, owner, scope, input.IdempotencyKey, input, ct) is { } replay) return replay;
        if (item.Version != input.Version) return Studio.Conflict();
        if (Studio.Read<TemplateConfig>(item).Kind != "document") return Studio.Bad("Use document templates for bulk rendering.", 422);
        if (input.Records is not { Length: > 0 and <= 100 } || input.Records.Any(r => r is null || Studio.Title(r.Key, 128) is null || Studio.Title(r.Title) is null || r.Values is null) || input.Records.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count() != input.Records.Length) return Studio.Bad("Supply 1–100 records with unique keys, titles and values.");
        var results = new List<object>(); long bytes = 0;
        foreach (var record in input.Records)
        {
            try
            {
                var operations = new[] { Automation.Json(new { type = "fillBookmarks", values = record.Values }) };
                if (record.Values.Count > 0) Automation.ValidateOperations(operations);
                var content = record.Values.Count == 0 ? item.Content.ToArray() : (await converter.AutomateAsync(item.Content, "batch", new { operations }, ct)).Content;
                bytes += content.Length; if (bytes > FileLimits.MaxModelBytes) throw new ConversionException("Bulk output exceeds 256 MiB.", 413);
                var document = new StoredDocument { OwnerId = owner, Title = Studio.Title(record.Title)!, Content = content }; db.Documents.Add(document);
                results.Add(new { record.Key, success = true, metadata = Automation.Metadata(document) });
            }
            catch (ConversionException error) when (error.Status == 422) { results.Add(new { record.Key, success = false, error = error.Message }); }
        }
        var response = new { templateId = id, version = input.Version, records = results };
        Automation.Receipt(db, owner, scope, input.IdempotencyKey, input, response); return await Automation.Save(db, response, ct);
    }
}

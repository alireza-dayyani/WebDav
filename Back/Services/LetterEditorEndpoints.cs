using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Inkstone.Web.Data;

namespace Inkstone.Web.Services;

public sealed class LetterEditorRequest;
public sealed record SaveLetterEditor(long Version, JsonElement Document);
public sealed record LetterEditorSnapshot(ResourceSummary Metadata, JsonElement Document, string? BodyBookmark);

public static class LetterEditorEndpoints
{
    public const int MaxEditorBytes = 64 * FileLimits.MiB;

    public static void MapLetterEditor(this WebApplication app)
    {
        var routes = app.MapGroup("/api/letters/{id:guid}/editor").RequireAuthorization().WithTags("Letter editor");
        routes.MapPost("/", async (Guid id, LetterVersion input, ClaimsPrincipal user, AppDbContext db, LetterComposer composer, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "letter", id, ct);
            if (item is null) return Results.NotFound();
            if (item.Version != input.Version || item.Snapshot.Length > 0) return Studio.Conflict();
            // Reopening always works, including drafts with comments or missing dependencies.
            if (item.Content.Length > 0) return Results.Ok(Studio.Detail(item));
            return await Fill(item, db, composer, ct);
        }).WithSummary("Open a private editable copy of the selected letter template; incomplete details are allowed")
          .WithMetadata(new BookmarkValuesRequest()).Produces<ResourceDetail>().ProducesProblem(409).ProducesProblem(422);
        routes.MapGet("/", async (Guid id, long version, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "letter", id, ct, false);
            if (item is null) return Results.NotFound();
            if (item.Version != version || item.Snapshot.Length > 0 || item.Content.Length == 0) return Studio.Conflict();
            var draft = Studio.Read<LetterDraft>(item);
            var template = draft.Template is null ? null : await Studio.Find(db, item.OwnerId, "template", draft.Template.Id, ct, false);
            return Results.Ok(new LetterEditorSnapshot(Studio.Summary(item), JsonSerializer.Deserialize<JsonElement>(item.Content, Studio.Json),
                template is null ? null : Studio.Read<TemplateConfig>(template).Fields.GetValueOrDefault("Body")));
        }).Produces<LetterEditorSnapshot>();
        routes.MapPut("/", async (Guid id, SaveLetterEditor input, ClaimsPrincipal user, AppDbContext db, DocumentConverter converter, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "letter", id, ct);
            if (item is null) return Results.NotFound();
            if (item.Version != input.Version || item.Snapshot.Length > 0 || item.Content.Length == 0) return Studio.Conflict();
            if (input.Document.ValueKind != JsonValueKind.Object) return Studio.Bad("A native document model object is required.");
            var bytes = Encoding.UTF8.GetBytes(input.Document.GetRawText());
            if (bytes.Length > MaxEditorBytes) return Studio.Bad("The letter editor model exceeds 64 MiB.", 413);
            // Canonicalize through Rust; do not trust caller-supplied model structure or paths.
            var model = await converter.ImportAsync(bytes, "json", MaxEditorBytes, ct);
            if (model.Length > MaxEditorBytes) return Studio.Bad("The letter editor model exceeds 64 MiB.", 413);
            item.Content = model; Studio.Touch(item);
            return await Studio.Save(db, ct) ? Results.Ok(Studio.Summary(item)) : Studio.Conflict();
        }).WithMetadata(new LetterEditorRequest()).WithSummary("Save the complete private letter editor model using its current revision")
          .Produces<ResourceSummary>().ProducesProblem(409).ProducesProblem(413);
        routes.MapPost("/fields", async (Guid id, LetterVersion input, ClaimsPrincipal user, AppDbContext db, LetterComposer composer, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "letter", id, ct);
            if (item is null) return Results.NotFound();
            if (item.Version != input.Version || item.Snapshot.Length > 0 || item.Content.Length == 0) return Studio.Conflict();
            return await Fill(item, db, composer, ct);
        }).WithMetadata(new BookmarkValuesRequest()).WithSummary("Fill automatic recipient, sender, title, signature and other mapped fields, preserving the written body")
          .Produces<ResourceDetail>().ProducesProblem(422);
        routes.MapGet("/download", async (Guid id, long version, string? format, ClaimsPrincipal user, AppDbContext db, DocumentConverter converter, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "letter", id, ct, false);
            if (item is null) return Results.NotFound();
            if (item.Version != version || item.Content.Length == 0 || item.Snapshot.Length > 0) return Studio.Conflict();
            format ??= "docx";
            return Studio.File(await converter.ExportAsync(item.Content, format, ct), item.Title, format);
        }).WithSummary("Download the saved working letter revision; use generation for finalized files");
    }

    private static async Task<IResult> Fill(WorkspaceItem item, AppDbContext db, LetterComposer composer, CancellationToken ct)
    {
        var plan = await composer.Prepare(item, ct, forEditor: true);
        if (plan.Issues.Length > 0) return Results.Problem("Resolve the template or resource issues before filling letter fields.", statusCode: 422,
            extensions: new Dictionary<string, object?> { ["issues"] = plan.Issues });
        var model = await composer.Compose(item, plan, plan.Recipients.Where(x => x.Kind == "cc" && x.PrintOnLetter), ct);
        if (model.Length > MaxEditorBytes) return Studio.Bad("The letter editor model exceeds 64 MiB.", 413);
        item.Content = model; Studio.Touch(item);
        return await Studio.Save(db, ct) ? Results.Ok(Studio.Detail(item)) : Studio.Conflict();
    }
}

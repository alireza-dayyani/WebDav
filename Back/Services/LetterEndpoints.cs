using System.Security.Claims;
using System.Text.Json;
using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

public sealed record LetterVersion(long Version);
public sealed record DuplicateLetter(long Version, string? Title);

public static class LetterEndpoints
{
    public static void MapLetters(this WebApplication app)
    {
        var routes = app.MapGroup("/api/letters").RequireAuthorization().WithTags("Letters").WithMetadata(new BookmarkValuesRequest());
        routes.MapGet("/", (int? page, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) => Studio.List(db, Ownership.Owner(user), "letter", page, ct));
        routes.MapPost("/", async (LetterDraft input, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            if (!LetterComposer.ValidShape(input)) return Studio.Bad("Invalid draft. Choose a title and language, at most 20 distinct recipients (10 copies), five attachments and 480 KiB of body text.");
            var item = new WorkspaceItem { OwnerId = Ownership.Owner(user), Kind = "letter", Title = input.Title.Trim(), Data = Studio.Encode(input) };
            db.WorkspaceItems.Add(item); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/letters/{item.Id}?version=1", Studio.Detail(item));
        }).WithMetadata(new BookmarkValuesRequest()).WithSummary("Create a private letter draft, separate from ordinary documents").Produces<ResourceDetail>(201).ProducesProblem(400);
        routes.MapGet("/{id:guid}", async (Guid id, long version, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "letter", id, ct, false);
            return item is null ? Results.NotFound() : item.Version != version ? Studio.Conflict() : Results.Ok(Studio.Detail(item));
        }).Produces<ResourceDetail>();
        routes.MapPut("/{id:guid}", async (Guid id, SaveLetter input, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "letter", id, ct);
            if (item is null) return Results.NotFound();
            if (item.Version != input.Version || item.Snapshot.Length != 0) return Studio.Conflict();
            if (!LetterComposer.ValidShape(input.Draft)) return Studio.Bad("Invalid draft fields, recipients, attachments or body size.");
            var previous = Studio.Read<LetterDraft>(item);
            if (item.Content.Length > 0 && (previous.Template?.Id != input.Draft.Template?.Id || previous.BodyDocument != input.Draft.BodyDocument || previous.BodyText != input.Draft.BodyText))
                return Studio.Bad("This letter has an editable template copy. Edit its body in the letter editor; create another letter to change its template.", 409);
            item.Title = input.Draft.Title.Trim(); item.Data = Studio.Encode(input.Draft); Studio.Touch(item);
            return await Studio.Save(db, ct) ? Results.Ok(Studio.Detail(item)) : Studio.Conflict();
        }).WithMetadata(new BookmarkValuesRequest()).WithSummary("Update an owned draft revision; generated letters are immutable").Produces<ResourceDetail>();
        routes.MapDelete("/{id:guid}", (Guid id, long version, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) => Studio.Delete(db, Ownership.Owner(user), "letter", id, version, ct));
        routes.MapPost("/{id:guid}/duplicate", async (Guid id, DuplicateLetter input, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "letter", id, ct, false);
            if (item is null) return Results.NotFound();
            if (item.Version != input.Version) return Studio.Conflict();
            if (Studio.Title(input.Title) is not { } title) return Studio.Bad("A new draft title is required.");
            var draft = Studio.Read<LetterDraft>(item) with { Title = title, Number = "" };
            var copy = new WorkspaceItem { OwnerId = item.OwnerId, Kind = "letter", Title = title, Data = Studio.Encode(draft), Content = item.Content.ToArray() };
            db.WorkspaceItems.Add(copy); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/letters/{copy.Id}?version=1", Studio.Detail(copy));
        }).Produces<ResourceDetail>(201);
        routes.MapPost("/{id:guid}/validate", async (Guid id, LetterVersion input, ClaimsPrincipal user, AppDbContext db, LetterComposer composer, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "letter", id, ct, false);
            if (item is null) return Results.NotFound();
            if (item.Version != input.Version) return Studio.Conflict();
            if (item.Snapshot.Length != 0) return Results.Ok(new ValidationReport(true, item.Version, []));
            var plan = await composer.Prepare(item, ct);
            return Results.Ok(new ValidationReport(plan.Issues.Length == 0, item.Version, plan.Issues));
        }).WithSummary("Preflight a saved draft without modifying it").Produces<ValidationReport>().ProducesProblem(409);
        routes.MapPost("/{id:guid}/preview", async (Guid id, LetterVersion input, ClaimsPrincipal user, AppDbContext db, LetterComposer composer, DocumentConverter converter, HttpResponse response, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "letter", id, ct, false);
            if (item is null) return Results.NotFound();
            if (item.Version != input.Version) return Studio.Conflict();
            if (item.Snapshot.Length != 0)
            {
                var artifact = await db.LetterArtifacts.AsNoTracking().Where(x => x.LetterId == id && x.Key == "primary").Select(x => x.Pdf).SingleAsync(ct);
                return Studio.PdfPreview(artifact, response);
            }
            var plan = await composer.Prepare(item, ct);
            if (plan.Issues.Length != 0) return Invalid(plan);
            var model = await composer.Compose(item, plan, plan.Recipients.Where(x => x.Kind == "cc" && x.PrintOnLetter), ct);
            return Studio.PdfPreview(await converter.ExportAsync(model, "pdf", ct), response);
        }).WithSummary("Render a private PDF preview without finalizing the draft").ProducesProblem(422);
        routes.MapPost("/{id:guid}/generate", async (Guid id, GenerateLetter input, ClaimsPrincipal user, AppDbContext db, LetterComposer composer, DocumentConverter converter, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "letter", id, ct);
            if (item is null) return Results.NotFound();
            if (input.IdempotencyKey is not { Length: >= 16 and <= 80 } key || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
                return Studio.Bad("Supply a unique idempotencyKey of 16–80 ASCII letters, digits, hyphens or underscores.");
            if (item.Snapshot.Length != 0)
            {
                using var saved = JsonDocument.Parse(item.Snapshot);
                return saved.RootElement.GetProperty("idempotencyKey").GetString() == key
                    && (saved.RootElement.GetProperty("requestVersion").GetInt64() == input.Version || item.Version == input.Version)
                    ? Results.Ok(await Result(db, item, ct)) : Studio.Conflict();
            }
            if (item.Version != input.Version) return Studio.Conflict();
            var plan = await composer.Prepare(item, ct);
            if (plan.Issues.Length != 0) return Invalid(plan);
            var artifacts = new List<LetterArtifact>();
            long aggregate = 0;
            async Task Add(string artifactKey, string title, byte[] model)
            {
                var docx = await converter.ExportAsync(model, "docx", ct);
                var pdf = await converter.ExportAsync(model, "pdf", ct);
                aggregate += model.Length + (long)docx.Length + pdf.Length;
                if (aggregate > FileLimits.MaxModelBytes) throw new ConversionException("The complete letter output set exceeds 256 MiB.", 413);
                artifacts.Add(new LetterArtifact { LetterId = item.Id, Key = artifactKey, Title = title, Model = model, Docx = docx, Pdf = pdf });
            }
            var printable = plan.Recipients.Where(x => x.Kind == "cc" && x.PrintOnLetter).ToArray();
            await Add("primary", item.Title, await composer.Compose(item, plan, printable, ct));
            var copies = plan.Recipients.Where(x => x.Kind == "cc").ToArray();
            if (plan.Draft.CopyVariants && copies.Length > 0)
            {
                await Add("without-copies", item.Title + " — without copies", await composer.Compose(item, plan, [], ct));
                for (var i = 0; i < copies.Length; i++)
                    await Add("copy-" + (i + 1), item.Title + " — " + copies[i].Name, await composer.Compose(item, plan, [copies[i]], ct));
            }
            foreach (var attachment in plan.Attachments)
                await Add("attachment-" + attachment.Id, attachment.Title, attachment.Content.Length == 0 ? await converter.CreateAsync(plan.Draft.Language, ct) : attachment.Content);
            item.Snapshot = Studio.Encode(new {
                requestVersion = input.Version, idempotencyKey = key, generatedAt = DateTimeOffset.UtcNow,
                number = composer.Number(item, plan.Draft), draft = plan.Draft,
                template = new { plan.Template!.Id, plan.Template.Title, plan.Template.Version, hash = LetterComposer.Hash(plan.Template.Content), config = Studio.Read<TemplateConfig>(plan.Template) },
                sender = new { plan.Sender!.Id, plan.Sender.Version, profile = Studio.Read<SenderProfile>(plan.Sender) },
                signature = new { plan.Signature!.Id, plan.Signature.Version, hash = LetterComposer.Hash(plan.Signature.Content) },
                logo = plan.Logo is null ? null : new { plan.Logo.Id, plan.Logo.Version, hash = LetterComposer.Hash(plan.Logo.Content) },
                body = plan.Body is null ? null : new { plan.Body.Id, plan.Body.Version, hash = LetterComposer.Hash(plan.Body.Content) },
                editor = item.Content.Length == 0 ? null : new { version = item.Version, hash = LetterComposer.Hash(item.Content) },
                attachments = plan.Attachments.Select(x => new { x.Id, x.Title, x.Version, hash = LetterComposer.Hash(x.Content) }), recipients = plan.Recipients
            });
            Studio.Touch(item); db.LetterArtifacts.AddRange(artifacts);
            // One EF transaction: a racing save/delete rolls every generated artifact back too.
            if (!await Studio.Save(db, ct)) return Studio.Conflict();
            return Results.Ok(new GenerationResult(Studio.Summary(item), artifacts.Select(Info).ToArray()));
        }).WithSummary("Finalize a validated letter and atomically store immutable DOCX/PDF outputs")
          .WithDescription("Requires the saved draft version and a client-generated idempotency key. A retry with the same key returns the original outputs. Recipient selection formats the files; it does not send or share them. Signatures are selected private images, not cryptographic signatures.")
          .Produces<GenerationResult>().ProducesProblem(409).ProducesProblem(422).ProducesProblem(413);
        routes.MapGet("/{id:guid}/artifacts", async (Guid id, long version, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "letter", id, ct, false);
            return item is null ? Results.NotFound() : item.Version != version ? Studio.Conflict() : Results.Ok((await Result(db, item, ct)).Artifacts);
        }).Produces<ArtifactInfo[]>();
        routes.MapGet("/{id:guid}/artifacts/{artifactId:guid}/download", async (Guid id, Guid artifactId, long version, string? format, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "letter", id, ct, false);
            if (item is null) return Results.NotFound();
            if (item.Version != version) return Studio.Conflict();
            format ??= "docx";
            if (format is not ("docx" or "pdf" or "json")) return Studio.Bad("Choose docx, pdf or json.", 415);
            var artifact = await db.LetterArtifacts.AsNoTracking().SingleOrDefaultAsync(x => x.LetterId == id && x.Id == artifactId, ct);
            if (artifact is null) return Results.NotFound();
            return Studio.File(format == "docx" ? artifact.Docx : format == "pdf" ? artifact.Pdf : artifact.Model, artifact.Title, format);
        }).WithSummary("Download an owned frozen letter output revision");
    }

    private static IResult Invalid(LetterPlan plan) => Results.Problem("The letter is not ready. Resolve the validation issues before generation.", statusCode: 422,
        extensions: new Dictionary<string, object?> { ["issues"] = plan.Issues });
    private static ArtifactInfo Info(LetterArtifact item) => new(item.Id, item.Key, item.Title);
    private static async Task<GenerationResult> Result(AppDbContext db, WorkspaceItem item, CancellationToken ct)
        => new(Studio.Summary(item), await db.LetterArtifacts.AsNoTracking().Where(x => x.LetterId == item.Id).OrderBy(x => x.Key)
            .Select(x => new ArtifactInfo(x.Id, x.Key, x.Title)).ToArrayAsync(ct));
}

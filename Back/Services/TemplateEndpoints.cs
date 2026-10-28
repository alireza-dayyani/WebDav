using System.Security.Claims;
using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

public static class TemplateEndpoints
{
    public static void MapTemplates(this WebApplication app)
    {
        var routes = app.MapGroup("/api/templates").RequireAuthorization().WithTags("Templates").WithMetadata(new BookmarkValuesRequest());
        routes.MapGet("/", (int? page, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) => Studio.List(db, Ownership.Owner(user), "template", page, ct));
        routes.MapGet("/fields", () => Results.Ok(new { fields = Studio.Fields, required = Studio.Required, languages = DocumentConverter.Languages }));
        routes.MapGet("/{id:guid}/bookmarks", async (Guid id, long version, ClaimsPrincipal user, AppDbContext db, DocumentConverter converter, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "template", id, ct, false);
            if (item is null) return Results.NotFound();
            if (item.Version != version) return Studio.Conflict();
            return Results.Ok(new BookmarkList(id, version, await converter.ReadBookmarksAsync(item.Content, ct)));
        }).Produces<BookmarkList>().WithSummary("Inspect template bookmarks before mapping letter fields");
        routes.MapGet("/{id:guid}", async (Guid id, long version, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "template", id, ct, false);
            return item is null ? Results.NotFound() : item.Version != version ? Studio.Conflict() : Results.Ok(Studio.Detail(item));
        }).Produces<ResourceDetail>().Produces(404).ProducesProblem(409);
        routes.MapPost("/starter", async (StarterTemplate input, ClaimsPrincipal user, AppDbContext db, DocumentConverter converter, CancellationToken ct) =>
        {
            if (Studio.Title(input.Title) is not { } title || input.Kind is not ("letter" or "document") || !DocumentConverter.Languages.Contains(input.Language)
                || (input.Paper is not null && (input.Kind != "letter" || input.Paper is not ("A4" or "A5" or "Letter"))))
                return Studio.Bad("Choose a title, document/letter kind and supported language.");
            var model = input.Kind == "letter" ? await converter.LetterTemplateAsync(input.Language, ct, input.Paper) : await converter.CreateAsync(input.Language, ct);
            var config = new TemplateConfig(input.Kind, input.Language, input.Kind == "letter" ? Studio.Fields.ToDictionary(x => x, x => x) : [], input.Kind == "letter" ? Studio.Required : []);
            var item = new WorkspaceItem { OwnerId = Ownership.Owner(user), Kind = "template", Title = title, Data = Studio.Encode(config), Content = model };
            db.WorkspaceItems.Add(item); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/templates/{item.Id}?version=1", Studio.Detail(item));
        }).WithSummary("Create a private starter template for ordinary documents or letters").Produces<ResourceDetail>(201).ProducesProblem(400);
        routes.MapPost("/", async (CreateTemplate input, ClaimsPrincipal user, AppDbContext db, DocumentConverter converter, CancellationToken ct) =>
        {
            var owner = Ownership.Owner(user);
            if (Studio.Title(input.Title) is not { } title || input.Kind is not ("letter" or "document") || !DocumentConverter.Languages.Contains(input.Language!) || input.Source is null)
                return Studio.Bad("Choose a title, kind, language and an owned source document revision.");
            var source = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.Source.Id && x.OwnerId == owner, ct);
            if (source is null) return Results.NotFound();
            if (source.Version != input.Source.Version) return Studio.Conflict();
            var config = new TemplateConfig(input.Kind, input.Language!, input.Fields ?? [], input.RequiredFields ?? (input.Kind == "letter" ? Studio.Required : []));
            if (!Studio.ValidConfig(config)) return Studio.Bad("Map required letter fields to distinct bookmark names before saving the template.");
            var content = source.Content.Length == 0 ? await converter.CreateAsync(input.Language!, ct) : source.Content.ToArray();
            if (input.Kind == "letter")
            {
                var names = (await converter.ReadBookmarksAsync(content, ct)).Where(x => x.Replaceable).Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
                if (config.RequiredFields.Any(x => !names.Contains(config.Fields[x]))) return Studio.Bad("A required letter bookmark is missing or cannot be replaced.", 422);
            }
            var item = new WorkspaceItem { OwnerId = owner, Kind = "template", Title = title, Data = Studio.Encode(config), Content = content,
                Snapshot = Studio.Encode(new { sourceDocumentId = source.Id, sourceVersion = source.Version }) };
            db.WorkspaceItems.Add(item); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/templates/{item.Id}?version=1", Studio.Detail(item));
        }).WithSummary("Save an immutable snapshot of an owned document as a private template").Produces<ResourceDetail>(201).Produces(404).ProducesProblem(400).ProducesProblem(409).ProducesProblem(422);
        routes.MapPut("/{id:guid}", async (Guid id, UpdateTemplate input, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "template", id, ct);
            if (item is null) return Results.NotFound();
            if (item.Version != input.Version) return Studio.Conflict();
            var old = Studio.Read<TemplateConfig>(item);
            var config = old with { Fields = input.Fields ?? old.Fields, RequiredFields = input.RequiredFields ?? old.RequiredFields };
            if (Studio.Title(input.Title) is not { } title || !Studio.ValidConfig(config)) return Studio.Bad("Invalid title or bookmark mappings.");
            item.Title = title; item.Data = Studio.Encode(config); Studio.Touch(item);
            return await Studio.Save(db, ct) ? Results.Ok(Studio.Detail(item)) : Studio.Conflict();
        }).Produces<ResourceDetail>().ProducesProblem(409);
        routes.MapDelete("/{id:guid}", (Guid id, long version, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) => Studio.Delete(db, Ownership.Owner(user), "template", id, version, ct));
        routes.MapGet("/{id:guid}/download", async (Guid id, long version, string? format, ClaimsPrincipal user, AppDbContext db, DocumentConverter converter, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "template", id, ct, false);
            if (item is null) return Results.NotFound();
            if (item.Version != version) return Studio.Conflict();
            format ??= "docx";
            return Studio.File(await converter.ExportAsync(item.Content, format, ct), item.Title, format);
        }).WithSummary("Download a private template revision");
        routes.MapPost("/{id:guid}/render", async (Guid id, RenderTemplate input, ClaimsPrincipal user, AppDbContext db, DocumentConverter converter, CancellationToken ct) =>
        {
            var owner = Ownership.Owner(user);
            var item = await Studio.Find(db, owner, "template", id, ct, false);
            if (item is null) return Results.NotFound();
            if (item.Version != input.Version) return Studio.Conflict();
            if (Studio.Read<TemplateConfig>(item).Kind != "document") return Studio.Bad("Use the Letters workflow to generate a letter template.", 422);
            if (Studio.Title(input.Title) is not { } title) return Studio.Bad("A document title is required.");
            var values = input.Values ?? [];
            var content = values.Count == 0 ? item.Content.ToArray() : await converter.FillBookmarksAsync(item.Content, values, ct);
            var document = new StoredDocument { OwnerId = owner, Title = title, Content = content };
            db.Documents.Add(document); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/documents/{document.Id}", new DocumentMetadata(document.Id, document.Title, document.Version, document.CreatedAt, document.UpdatedAt, document.Content.Length));
        }).WithMetadata(new BookmarkValuesRequest()).WithSummary("Create a new owned document from a template without changing the template").Produces<DocumentMetadata>(201).Produces(404).ProducesProblem(409).ProducesProblem(422);
    }
}

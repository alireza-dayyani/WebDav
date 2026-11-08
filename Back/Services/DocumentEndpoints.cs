using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Inkstone.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

public static class DocumentEndpoints
{
    public static void MapDocumentRoutes(this WebApplication app)
    {
        var documents = app.MapGroup("/api/documents").RequireAuthorization().WithTags("Documents");
        documents.MapBookmarks();
        documents.MapGet("/formats", async (AppDbContext db, CancellationToken ct) => Results.Ok(new
        {
            upload = DocumentConverter.ImportFormats,
            download = DocumentConverter.ExportFormats,
            maxUploadBytes = (long)(await FileLimits.ReadAsync(db, ct)).MaxUploadMiB * FileLimits.MiB,
            maxDocumentBytes = FileLimits.MaxModelBytes
        })).WithSummary("Supported file formats and upload limit");

        documents.MapGet("/", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
            Results.Ok(await DocumentAccess.Visible(db, user).AsNoTracking()
                .OrderByDescending(d => d.UpdatedAt)
                .Select(d => new DocumentMetadata(d.Id, d.Title, d.Version, d.CreatedAt, d.UpdatedAt, d.Content.Length))
                .ToListAsync(ct)))
            .WithSummary("List documents visible to the current account")
            .WithDescription("Includes owned documents and reader/editor shares for cookie sessions. Scoped automation tokens list owned documents only. Trashed documents are excluded. Prefer /api/library for search and pagination.")
            .Produces<List<DocumentMetadata>>();

        documents.MapPost("/", async (DocumentTitle input, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var title = DocumentEndpointHelpers.CleanTitle(input.Title);
            if (title is null) return Results.Problem("A title must contain 1–160 characters.", statusCode: 400);
            var document = new StoredDocument { OwnerId = Ownership.Owner(user), Title = title };
            db.Documents.Add(document);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/documents/{document.Id}", DocumentEndpointHelpers.Metadata(document));
        }).Produces<DocumentMetadata>(201).ProducesProblem(400);

        documents.MapPost("/upload", async ([FromForm] IFormFile file, [FromForm] string? title, ClaimsPrincipal user, AppDbContext db, DocumentConverter converter, HttpContext context, CancellationToken ct) =>
        {
            var maxUploadBytes = (long)context.Items["UploadLimitBytes"]!;
            if (file.Length == 0 || file.Length > maxUploadBytes) return Results.Problem("Choose a non-empty file within the current upload limit.", statusCode: 413, extensions: new Dictionary<string, object?> { ["code"] = "upload_too_large", ["maxBytes"] = maxUploadBytes });
            var filename = file.FileName.Replace('\\', '/').Split('/').Last();
            var cleanTitle = DocumentEndpointHelpers.CleanTitle(title ?? Path.GetFileNameWithoutExtension(filename));
            if (cleanTitle is null) return Results.Problem("A title must contain 1–160 characters.", statusCode: 400);
            var extension = Path.GetExtension(filename).TrimStart('.').ToLowerInvariant();
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            var content = await converter.ImportAsync(buffer.ToArray(), extension, maxUploadBytes, ct);
            var document = new StoredDocument { OwnerId = Ownership.Owner(user), Title = cleanTitle, Content = content };
            db.Documents.Add(document);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/documents/{document.Id}", DocumentEndpointHelpers.Metadata(document));
        }).WithMetadata(new UploadPolicyRequest()).WithSummary("Upload a file into the current account")
          .WithDescription("Multipart fields: file (required), title (optional). Supports DOCX, ODT, RTF, TXT, Markdown and native JSON. The current administrator-configured upload limit is returned by /api/documents/formats. The Rust engine converts uploads into editable native documents.")
          .Produces<DocumentMetadata>(201).ProducesProblem(400).ProducesProblem(413).ProducesProblem(415).ProducesProblem(422).ProducesProblem(503);

        documents.MapGet("/{id:guid}/download", async (Guid id, string? format, long? version, ClaimsPrincipal user, AppDbContext db, DocumentConverter converter, CancellationToken ct) =>
        {
            var document = await DocumentAccess.Visible(db, user).AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
            if (document is null) return Results.NotFound();
            return await DocumentDownloads.CreateAsync(document, format, version, converter, ct);
        }).WithSummary("Download the saved document as an attachment")
          .WithDescription("format: docx (default), pdf, txt, md or json. Optional version returns 409 if the saved document has changed. The owner or an explicitly shared reader/editor can download the current document.")
          .Produces(200, contentType: "application/octet-stream").ProducesProblem(409).ProducesProblem(415).ProducesProblem(422).ProducesProblem(503);

        documents.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var document = await DocumentAccess.Visible(db, user).AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
            return document is null ? Results.NotFound() : Results.Ok(DocumentEndpointHelpers.Metadata(document));
        }).Produces<DocumentMetadata>().Produces(404);

        documents.MapGet("/{id:guid}/content", async (Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var document = await DocumentAccess.Visible(db, user).AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
            if (document is null) return Results.NotFound();
            return document.Content.Length == 0 ? Results.NoContent() : Results.Bytes(document.Content, "application/json");
        }).Produces<System.Text.Json.JsonElement>().Produces(204).Produces(404);

        documents.MapGet("/{id:guid}/snapshot", async (Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var document = await DocumentAccess.Visible(db, user).AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
            if (document is null) return Results.NotFound();
            JsonElement? content = document.Content.Length == 0 ? null : JsonSerializer.Deserialize<JsonElement>(document.Content, new JsonSerializerOptions { MaxDepth = 256 });
            return Results.Ok(new DocumentSnapshot(DocumentEndpointHelpers.Metadata(document), content));
        }).Produces<DocumentSnapshot>().Produces(404);

        documents.MapPut("/{id:guid}/content", async (Guid id, SaveDocument input, ClaimsPrincipal user, AppDbContext db, DocumentConverter converter, CancellationToken ct) =>
        {
            var document = await DocumentAccess.FindAsync(db, user, id, ct, true);
            if (document is null) return Results.NotFound();
            if (document.Version != input.Version) return DocumentEndpointHelpers.Conflict();
            if (input.Document.ValueKind != JsonValueKind.Object || !input.Document.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Array)
                return Results.Problem("The document format is invalid.", statusCode: 400);
            var bytes = Encoding.UTF8.GetBytes(input.Document.GetRawText());
            if (bytes.Length > FileLimits.MaxModelBytes) return Results.Problem("The editable document exceeds the 256 MiB processing limit.", statusCode: 413, extensions: new Dictionary<string, object?> { ["code"] = "model_too_large", ["maxBytes"] = FileLimits.MaxModelBytes });
            await converter.PreflightAsync(bytes, ct);
            document.Content = bytes;
            document.Version++;
            DocumentAccess.Audit(db, document, Ownership.Owner(user), "edit");
            document.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return DocumentEndpointHelpers.Conflict(); }
            return Results.Ok(DocumentEndpointHelpers.Metadata(document));
        }).Produces<DocumentMetadata>().ProducesProblem(409).ProducesProblem(400).ProducesProblem(413).Produces(404);

        documents.MapPatch("/{id:guid}", async (Guid id, RenameDocument input, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var title = DocumentEndpointHelpers.CleanTitle(input.Title);
            if (title is null) return Results.Problem("A title must contain 1–160 characters.", statusCode: 400);
            var document = await DocumentAccess.FindAsync(db, user, id, ct, true);
            if (document is null) return Results.NotFound();
            if (document.Version != input.Version) return DocumentEndpointHelpers.Conflict();
            document.Title = title;
            document.Version++;
            DocumentAccess.Audit(db, document, Ownership.Owner(user), "edit");
            document.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return DocumentEndpointHelpers.Conflict(); }
            return Results.Ok(DocumentEndpointHelpers.Metadata(document));
        }).Produces<DocumentMetadata>().ProducesProblem(409).ProducesProblem(400).Produces(404);

        documents.MapDelete("/{id:guid}", async (Guid id, long version, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == id && d.OwnerId == Ownership.Owner(user), ct);
            if (document is null) return Results.NotFound();
            if (document.Version != version) return DocumentEndpointHelpers.Conflict();
            document.Version++;
            document.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            db.TrashedDocuments.Add(new TrashedDocument { DocumentId = id });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Produces(204).ProducesProblem(409).Produces(404);
    }
}

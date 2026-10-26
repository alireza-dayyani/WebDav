using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

public static class BookmarkEndpoints
{
    public static void MapBookmarks(this RouteGroupBuilder documents)
    {
        documents.MapGet("/{id:guid}/bookmarks", async (Guid id, long version, ClaimsPrincipal user, AppDbContext db, DocumentConverter converter, CancellationToken ct) =>
        {
            var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.OwnerId == Ownership.Owner(user), ct);
            if (document is null) return Results.NotFound();
            if (document.Version != version) return VersionConflict();
            var bookmarks = await converter.ReadBookmarksAsync(document.Content, ct);
            return Results.Ok(new BookmarkList(document.Id, document.Version, bookmarks));
        }).WithSummary("List bookmarks in an owned document revision")
          .WithDescription("Upload a DOCX first, then use its document ID and version. Includes body, tables, headers, footers and other document stories. Replaceable describes the bookmark range; protection or Track Changes can still prevent replacement.")
          .Produces<BookmarkList>().Produces(401).Produces(404).ProducesProblem(400).ProducesProblem(409).ProducesProblem(422).ProducesProblem(503);

        documents.MapPost("/{id:guid}/bookmarks/replace", async (Guid id, ReplaceBookmarks input, ClaimsPrincipal user, AppDbContext db, DocumentConverter converter, CancellationToken ct) =>
        {
            var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == id && d.OwnerId == Ownership.Owner(user), ct);
            if (document is null) return Results.NotFound();
            if (document.Version != input.Version) return VersionConflict();
            if (input.Values.ValueKind != JsonValueKind.Object)
                return Results.Problem("values must be a flat JSON object of bookmark names and text strings.", statusCode: 400);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            long total = 0;
            foreach (var property in input.Values.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name) || Encoding.UTF8.GetByteCount(property.Name) > 256 || property.Value.ValueKind != JsonValueKind.String)
                    return Results.Problem("Every bookmark key must be a non-empty name (up to 256 UTF-8 bytes), and every value must be a text string.", statusCode: 400);
                var text = property.Value.GetString()!;
                if (!values.TryAdd(property.Name, text)) return Results.Problem("Duplicate bookmark keys are not allowed.", statusCode: 400);
                total += Encoding.UTF8.GetByteCount(text);
                if (values.Count > 256 || total > 512 * 1024) return Results.Problem("Supply at most 256 bookmark values and 512 KiB of UTF-8 text.", statusCode: 413);
                if (text.Any(c => c == '\uFFFC' || (char.IsControl(c) && c is not '\r' and not '\n' and not '\t')))
                    return Results.Problem("Values may contain text, tabs and line breaks, but no other control characters or inline-object markers.", statusCode: 400);
            }
            if (values.Count == 0) return Results.Problem("Supply at least one bookmark value.", statusCode: 400);
            if (document.Content.Length == 0) return Results.Problem("This document has no bookmarks. Upload a DOCX template first.", statusCode: 422);
            var bookmarks = await converter.ReadBookmarksAsync(document.Content, ct);
            var fields = bookmarks.ToDictionary(b => b.Name, StringComparer.Ordinal);
            var invalid = values.Keys.Where(name => !fields.TryGetValue(name, out var field) || !field.Replaceable).ToArray();
            if (invalid.Length != 0) return Results.Problem("Some keys have no matching replaceable bookmark. No changes were saved.", statusCode: 422,
                extensions: new Dictionary<string, object?> { ["invalidKeys"] = invalid });
            var content = await converter.FillBookmarksAsync(document.Content, values, ct);
            document.Content = content;
            document.Version++;
            document.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return VersionConflict(); }
            return Results.Ok(new BookmarkReplacement(
                new DocumentMetadata(document.Id, document.Title, document.Version, document.CreatedAt, document.UpdatedAt, document.Content.Length),
                values.Keys.Order(StringComparer.Ordinal).ToArray()));
        }).WithMetadata(new BookmarkValuesRequest()).WithSummary("Replace bookmark contents with JSON text values and save the next revision")
          .WithDescription("Body: {version: 1, values: {CustomerName: 'literal text'}}. Exact case-sensitive key matching. All keys must exist and have valid ranges; otherwise nothing is saved. Unspecified bookmarks stay unchanged; empty strings clear contents. Markers remain reusable. Rejects duplicate/malformed ranges, ranges crossing table cells, and ranges containing other bookmark markers. Protected documents or Track Changes must be disabled first. Only the owner can modify a document, including when the caller is an administrator.")
          .Produces<BookmarkReplacement>().Produces(401).Produces(404).ProducesProblem(400).ProducesProblem(409).ProducesProblem(413).ProducesProblem(422).ProducesProblem(429).ProducesProblem(503);
    }

    private static IResult VersionConflict() => Results.Problem("The document changed. Fetch its current metadata and retry with that version.", statusCode: 409);
}

public sealed record ReplaceBookmarks(long Version, JsonElement Values);
public sealed record BookmarkList(Guid Id, long Version, BookmarkInfo[] Bookmarks);
public sealed record BookmarkReplacement(DocumentMetadata Metadata, string[] Replaced);
public sealed class BookmarkValuesRequest;

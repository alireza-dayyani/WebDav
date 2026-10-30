using System.Security.Claims;
using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

public static class LibraryRecovery
{
    public static void MapLibraryRecovery(this WebApplication app)
    {
        var routes = app.MapGroup("/api/documents").RequireAuthorization().WithTags("Document recovery");
        routes.MapGet("/trash", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) => Results.Ok(await db.Documents.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.OwnerId == Ownership.Owner(user) && db.TrashedDocuments.Any(t => t.DocumentId == d.Id)).OrderByDescending(d => d.UpdatedAt)
            .Select(d => new DocumentMetadata(d.Id, d.Title, d.Version, d.CreatedAt, d.UpdatedAt, d.Content.Length)).ToListAsync(ct)));
        routes.MapPost("/{id:guid}/recover", async (Guid id, VersionRequest input, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var document = await db.Documents.IgnoreQueryFilters().SingleOrDefaultAsync(d => d.Id == id && d.OwnerId == Ownership.Owner(user), ct);
            if (document is null) return Results.NotFound();
            var trash = await db.TrashedDocuments.SingleOrDefaultAsync(t => t.DocumentId == id, ct);
            if (trash is null || document.Version != input.Version) return Studio.Conflict();
            db.TrashedDocuments.Remove(trash); document.Version++; document.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return await Studio.Save(db, ct) ? Results.Ok(Automation.Metadata(document)) : Studio.Conflict();
        });
        routes.MapDelete("/{id:guid}/purge", async (Guid id, long version, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var query = db.Documents.IgnoreQueryFilters().Where(d => d.Id == id && d.OwnerId == Ownership.Owner(user) && db.TrashedDocuments.Any(t => t.DocumentId == d.Id));
            if (await query.Where(d => d.Version == version).ExecuteDeleteAsync(ct) != 0) return Results.NoContent();
            return await query.AnyAsync(ct) ? Studio.Conflict() : Results.NotFound();
        });
        app.MapGet("/api/storage", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var owner = Ownership.Owner(user);
            var documents = db.Documents.IgnoreQueryFilters().Where(d => d.OwnerId == owner);
            var contentBytes = await documents.SumAsync(d => (long)d.Content.Length, ct);
            var historyBytes = await db.DocumentRevisions.Where(r => documents.Any(d => d.Id == r.DocumentId)).SumAsync(r => (long)r.Content.Length, ct);
            // SQLite length(TEXT) counts characters, not the persisted UTF-8 bytes.
            // Draft bodies and frozen resource metadata live in these JSON columns.
            var resourceBytes = await db.Database.SqlQuery<long>($"SELECT COALESCE(SUM(length(Content) + length(CAST(Data AS BLOB)) + length(CAST(Snapshot AS BLOB))), 0) AS Value FROM WorkspaceItems WHERE OwnerId = {owner}").SingleAsync(ct);
            var artifactBytes = await db.LetterArtifacts.Where(a => db.WorkspaceItems.Any(w => w.Id == a.LetterId && w.OwnerId == owner)).SumAsync(a => (long)a.Model.Length + a.Docx.Length + a.Pdf.Length, ct);
            var jobBytes = await db.DocumentJobs.Where(j => j.OwnerId == owner).SumAsync(j => (long)j.Result.Length, ct);
            return Results.Ok(new { contentBytes, historyBytes, resourceBytes, artifactBytes, jobBytes, totalBytes = contentBytes + historyBytes + resourceBytes + artifactBytes + jobBytes, includesTrash = true, excludesDatabaseOverheadAndJobInputs = true });
        }).RequireAuthorization().WithTags("Storage");
    }
}

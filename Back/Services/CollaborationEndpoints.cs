using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

public static class CollaborationEndpoints
{
    private static async Task<StoredDocument?> Owned(AppDbContext db, ClaimsPrincipal user, Guid id, long version, CancellationToken ct)
    {
        var doc = await db.Documents.SingleOrDefaultAsync(d => d.Id == id && d.OwnerId == Ownership.Owner(user), ct);
        if (doc is not null && doc.Version != version) throw new ConversionException("The document changed. Refresh before continuing.", 409);
        return doc;
    }
    private static void GuardDocument(AppDbContext db, StoredDocument doc) => db.Entry(doc).Property(d => d.Version).IsModified = true;
    public static void MapCollaboration(this WebApplication app)
    {
        var routes = app.MapGroup("/api/documents/{id:guid}").RequireAuthorization().WithTags("Sharing and approvals").WithMetadata(new BookmarkValuesRequest());
        routes.AddEndpointFilter(async (context, next) => context.HttpContext.User.HasClaim(c => c.Type == AutomationTokens.Claim) ? Results.Forbid() : await next(context));
        routes.MapGet("/access", async (Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var actor = Ownership.Owner(user);
            var result = await DocumentAccess.Visible(db, user).AsNoTracking().Where(d => d.Id == id)
                .Select(d => new { d.Id, d.Title, d.Version, d.CreatedAt, d.UpdatedAt, size = d.Content.Length,
                    permission = d.OwnerId == actor ? "owner" : db.DocumentGrants.Where(g => g.DocumentId == id && g.UserId == actor).Select(g => g.Permission).First() }).SingleOrDefaultAsync(ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
        routes.MapGet("/shares", async (Guid id, long version, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            if (await Owned(db, user, id, version, ct) is null) return Results.NotFound();
            return Results.Ok(await db.DocumentGrants.Where(g => g.DocumentId == id).Join(db.Users, g => g.UserId, u => u.Id, (g, u) => new { g.UserId, u.Email, g.Permission, g.Version }).OrderBy(x => x.Email).ToListAsync(ct));
        });
        routes.MapPut("/shares", async (Guid id, ShareSettings input, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            if (input.Permission is not ("read" or "edit") || input.Email is not { Length: > 0 and <= 254 } || input.GrantVersion < 0) return Studio.Bad("Supply an existing account email and read/edit permission.");
            var doc = await Owned(db, user, id, input.Version, ct);
            if (doc is null) return Results.NotFound();
            var email = input.Email.Trim().ToUpperInvariant();
            var account = await db.Users.SingleOrDefaultAsync(u => u.Email != null && u.Email.ToUpper() == email, ct);
            if (account is null) return Studio.Bad("That account was not found.", 404);
            if (account.Id == doc.OwnerId) return Studio.Bad("The owner already has full access.");
            var grant = await db.DocumentGrants.IgnoreQueryFilters().SingleOrDefaultAsync(g => g.DocumentId == id && g.UserId == account.Id, ct);
            if ((grant is null || grant.Permission == "revoked" ? 0 : grant.Version) != input.GrantVersion) return Studio.Conflict();
            if (grant is null) { grant = new DocumentGrant { DocumentId = id, UserId = account.Id, Permission = input.Permission, Version = 0 }; db.DocumentGrants.Add(grant); }
            grant.Permission = input.Permission; grant.Version++; GuardDocument(db, doc);
            DocumentAccess.Audit(db, doc, Ownership.Owner(user), "share", account.Id + ":" + input.Permission);
            return await Studio.Save(db, ct) ? Results.Ok(new { grant.UserId, account.Email, grant.Permission, grant.Version }) : Studio.Conflict();
        });
        routes.MapDelete("/shares/{userId}", async (Guid id, string userId, long version, long grantVersion, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var doc = await Owned(db, user, id, version, ct); if (doc is null) return Results.NotFound();
            var grant = await db.DocumentGrants.SingleOrDefaultAsync(g => g.DocumentId == id && g.UserId == userId, ct);
            if (grant is null) return Results.NotFound();
            if (grant.Version != grantVersion) return Studio.Conflict();
            // Keep the version across revocation/regrant. Reusing version 1 would
            // let an older in-flight editor save pass its concurrency check.
            grant.Permission = "revoked"; grant.Version++;
            GuardDocument(db, doc); DocumentAccess.Audit(db, doc, Ownership.Owner(user), "revoke", userId);
            return await Studio.Save(db, ct) ? Results.NoContent() : Studio.Conflict();
        });
        routes.MapGet("/audit", async (Guid id, long version, int? page, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            if (await Owned(db, user, id, version, ct) is null) return Results.NotFound();
            var index = page ?? 1; if (index is < 1 or > 100000) return Studio.Bad("Invalid page.");
            var rows = await db.DocumentAudits.AsNoTracking().Where(a => a.DocumentId == id).OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Id).Skip((index - 1) * 50).Take(51).ToListAsync(ct);
            return Results.Ok(new { items = rows.Take(50), hasMore = rows.Count > 50 });
        });
        routes.MapPost("/reviews", async (Guid id, ReviewSubmission input, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            if (input.ReviewerId is not { Length: > 0 and <= 450 }) return Studio.Bad("Choose a shared reviewer.");
            var doc = await Owned(db, user, id, input.Version, ct); if (doc is null) return Results.NotFound();
            if (!await db.DocumentGrants.AnyAsync(g => g.DocumentId == id && g.UserId == input.ReviewerId, ct)) return Studio.Bad("Share the document with the reviewer first.");
            if (await db.DocumentReviews.AnyAsync(r => r.DocumentId == id && r.DocumentVersion == doc.Version && r.ReviewerId == input.ReviewerId && r.Status == "pending", ct)) return Studio.Conflict();
            if (!await db.DocumentRevisions.AnyAsync(r => r.DocumentId == id && r.Version == doc.Version, ct))
                db.DocumentRevisions.Add(new DocumentRevision { DocumentId = id, Version = doc.Version, Title = doc.Title, Content = doc.Content.ToArray(), CreatedAt = doc.UpdatedAt });
            var review = new DocumentReview { DocumentId = id, DocumentVersion = doc.Version, ReviewerId = input.ReviewerId, ContentHash = Convert.ToHexString(SHA256.HashData(doc.Content)) };
            db.DocumentReviews.Add(review); GuardDocument(db, doc); DocumentAccess.Audit(db, doc, Ownership.Owner(user), "submit", review.Id.ToString());
            return await Studio.Save(db, ct) ? Results.Ok(review) : Studio.Conflict();
        });

        var reviews = app.MapGroup("/api/reviews").RequireAuthorization().WithTags("Approvals").WithMetadata(new BookmarkValuesRequest());
        reviews.MapGet("/", async (int? page, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var index = page ?? 1; if (index is < 1 or > 100000) return Studio.Bad("Invalid page.");
            var actor = Ownership.Owner(user);
            var rows = await db.DocumentReviews.AsNoTracking().Where(r => db.Documents.Any(d => d.Id == r.DocumentId && (d.OwnerId == actor || (r.ReviewerId == actor && db.DocumentGrants.Any(g => g.DocumentId == d.Id && g.UserId == actor)))))
                .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).Skip((index - 1) * 50).Take(51)
                .Select(r => new { r.Id, r.DocumentId, r.DocumentVersion, r.ReviewerId, r.Status, r.Note, r.ContentHash, r.Version, r.CreatedAt, r.UpdatedAt,
                    title = db.Documents.Where(d => d.Id == r.DocumentId).Select(d => d.Title).First(), currentVersion = db.Documents.Where(d => d.Id == r.DocumentId).Select(d => d.Version).First(), canDecide = r.ReviewerId == actor, canCancel = db.Documents.Any(d => d.Id == r.DocumentId && d.OwnerId == actor) }).ToListAsync(ct);
            return Results.Ok(new { items = rows.Take(50), hasMore = rows.Count > 50 });
        });
        reviews.MapPost("/{reviewId:guid}/decision", async (Guid reviewId, ReviewDecision input, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            if (input.Decision is not ("approved" or "returned" or "cancelled") || input.Note is null || input.Note.Length > 2000) return Studio.Bad("Choose approved, returned or cancelled, with at most 2000 note characters.");
            var review = await db.DocumentReviews.SingleOrDefaultAsync(r => r.Id == reviewId, ct);
            if (review is null) return Results.NotFound();
            var actor = Ownership.Owner(user);
            var doc = await DocumentAccess.FindAsync(db, user, review.DocumentId, ct);
            if (doc is null || (input.Decision == "cancelled" ? doc.OwnerId != actor : review.ReviewerId != actor)) return Results.NotFound();
            if (review.Version != input.Version || review.Status != "pending" || doc.Version != input.DocumentVersion || (input.Decision != "cancelled" && doc.Version != review.DocumentVersion)) return Studio.Conflict();
            if (doc.OwnerId != actor)
            {
                var grant = await db.DocumentGrants.SingleOrDefaultAsync(g => g.DocumentId == doc.Id && g.UserId == actor, ct);
                if (grant is null) return Results.NotFound(); db.Entry(grant).Property(g => g.Version).IsModified = true;
            }
            review.Status = input.Decision; review.Note = input.Note.Trim(); review.Version++; review.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            GuardDocument(db, doc); DocumentAccess.Audit(db, doc, actor, input.Decision, review.Id.ToString());
            return await Studio.Save(db, ct) ? Results.Ok(review) : Studio.Conflict();
        });
        reviews.MapGet("/{reviewId:guid}/download", async (Guid reviewId, long version, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct) =>
        {
            var review = await db.DocumentReviews.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reviewId, ct); if (review is null) return Results.NotFound();
            var doc = await DocumentAccess.FindAsync(db, user, review.DocumentId, ct);
            if (doc is null || (doc.OwnerId != Ownership.Owner(user) && review.ReviewerId != Ownership.Owner(user))) return Results.NotFound();
            if (review.Version != version) return Studio.Conflict();
            var snapshot = await db.DocumentRevisions.AsNoTracking().SingleAsync(r => r.DocumentId == doc.Id && r.Version == review.DocumentVersion, ct);
            return Studio.File(await converter.ExportAsync(snapshot.Content, "pdf", ct), snapshot.Title, "pdf");
        });
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ShareSettings(long Version, string Email, string Permission, long GrantVersion = 0);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewSubmission(long Version, string ReviewerId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewDecision(long Version, long DocumentVersion, string Decision, string Note = "");

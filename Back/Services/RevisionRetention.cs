using System.Security.Claims;
using System.Text.Json.Serialization;
using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

public static class RevisionRetention
{
    public static IQueryable<DocumentRevision> Eligible(AppDbContext db, string owner, RetentionPolicy policy, Guid? documentId = null)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-policy.Days).ToUnixTimeMilliseconds();
        return db.DocumentRevisions.Where(r => (documentId == null || r.DocumentId == documentId) && r.CreatedAt < cutoff
            && !db.DocumentReviews.Any(review => review.DocumentId == r.DocumentId && review.DocumentVersion == r.Version)
            && db.Documents.IgnoreQueryFilters().Any(d => d.Id == r.DocumentId && d.OwnerId == owner && d.Version != r.Version)
            && r.Version != db.DocumentRevisions.Where(x => x.DocumentId == r.DocumentId).Min(x => x.Version)
            && !db.DocumentRevisions.Where(x => x.DocumentId == r.DocumentId).OrderByDescending(x => x.Version).Take(policy.KeepLatest).Select(x => x.Version).Contains(r.Version));
    }
    public static void MapRevisionRetention(this WebApplication app)
    {
        var routes = app.MapGroup("/api/retention").RequireAuthorization().WithTags("Retention");
        routes.MapGet("/", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) => Results.Ok(await db.RetentionPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.OwnerId == Ownership.Owner(user), ct) ?? new RetentionPolicy { OwnerId = Ownership.Owner(user) }));
        routes.MapPut("/", async (RetentionSettings input, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            if (input.KeepLatest is < 5 or > 1000 || input.Days is < 7 or > 3650) return Studio.Bad("Keep 5–1000 recent revisions and 7–3650 days.");
            var owner = Ownership.Owner(user);
            var policy = await db.RetentionPolicies.SingleOrDefaultAsync(p => p.OwnerId == owner, ct);
            if ((policy?.Version ?? 0) != input.Version) return Studio.Conflict();
            if (policy is null) { policy = new RetentionPolicy { OwnerId = owner }; db.RetentionPolicies.Add(policy); }
            policy.Enabled = input.Enabled; policy.Days = input.Days; policy.KeepLatest = input.KeepLatest; policy.Version++;
            return await Studio.Save(db, ct) ? Results.Ok(policy) : Studio.Conflict();
        });
        routes.MapPost("/preview", async (VersionRequest input, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var policy = await db.RetentionPolicies.SingleOrDefaultAsync(p => p.OwnerId == Ownership.Owner(user), ct);
            if (policy is null || policy.Version != input.Version) return Studio.Conflict();
            var eligible = Eligible(db, policy.OwnerId, policy);
            return Results.Ok(new { count = await eligible.CountAsync(ct), bytes = await eligible.SumAsync(r => (long)r.Content.Length, ct), policy.Enabled });
        });
        routes.MapPost("/apply", async (VersionRequest input, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var policy = await db.RetentionPolicies.SingleOrDefaultAsync(p => p.OwnerId == Ownership.Owner(user), ct);
            if (policy is null || policy.Version != input.Version || !policy.Enabled) return Studio.Conflict();
            var removed = await Eligible(db, policy.OwnerId, policy).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct); return Results.Ok(new { removed });
        });
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RetentionSettings(long Version, bool Enabled, int KeepLatest, int Days);

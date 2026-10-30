using System.Security.Claims;
using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

public static class DocumentAccess
{
    // Scoped tokens retain their original owner-only boundary. Sharing is for
    // cookie-authenticated users; granting access never widens an existing token.
    public static IQueryable<StoredDocument> Visible(AppDbContext db, ClaimsPrincipal user, bool edit = false)
    {
        var owner = Ownership.Owner(user);
        var shares = !user.HasClaim(c => c.Type == AutomationTokens.Claim);
        return db.Documents.Where(d => d.OwnerId == owner || (shares && db.DocumentGrants.Any(g => g.DocumentId == d.Id && g.UserId == owner && (!edit || g.Permission == "edit"))));
    }
    public static async Task<StoredDocument?> FindAsync(AppDbContext db, ClaimsPrincipal user, Guid id, CancellationToken ct, bool edit = false)
    {
        var document = await Visible(db, user, edit).SingleOrDefaultAsync(d => d.Id == id, ct);
        if (document is null || !edit || document.OwnerId == Ownership.Owner(user)) return document;
        var grant = await db.DocumentGrants.SingleOrDefaultAsync(g => g.DocumentId == id && g.UserId == Ownership.Owner(user) && g.Permission == "edit", ct);
        if (grant is null) return null;
        // A same-value concurrency update makes revocation/downgrade during an
        // awaited conversion roll back the document, revision and receipt together.
        db.Entry(grant).Property(g => g.Version).IsModified = true;
        return document;
    }
    public static void Audit(AppDbContext db, StoredDocument doc, string actor, string action, string detail = "") => db.DocumentAudits.Add(new DocumentAudit { DocumentId = doc.Id, ActorId = actor, Action = action, Detail = detail, DocumentVersion = doc.Version });
}

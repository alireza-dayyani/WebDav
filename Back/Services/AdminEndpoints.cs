using System.Text;
using System.Text.Json;
using Inkstone.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

public static class AdminEndpoints
{
    public static void MapAdministration(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(AccessRoles.Policy).WithTags("Administration");
        admin.MapGet("/settings", async (AppDbContext db, CancellationToken ct) => Results.Ok(FileLimits.Describe(await FileLimits.ReadAsync(db, ct))))
            .WithSummary("Read the global upload policy");
        admin.MapPut("/settings", async (UploadPolicyChange input, AppDbContext db, ILogger<AppDbContext> logger, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct) =>
        {
            if (input.MaxUploadMiB < 1 || input.MaxUploadMiB > FileLimits.MaxUploadMiB)
                return Results.Problem($"Choose an upload limit from 1 to {FileLimits.MaxUploadMiB} MiB.", statusCode: 400);
            var policy = await db.UploadPolicies.SingleAsync(p => p.Id == 1, ct);
            if (policy.Version != input.Version) return Stale();
            policy.MaxUploadMiB = input.MaxUploadMiB;
            policy.Version++;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Stale(); }
            logger.LogInformation("Administrator {ActorId} set the upload limit to {Limit} MiB", Ownership.Owner(actor), policy.MaxUploadMiB);
            return Results.Ok(FileLimits.Describe(policy));
        }).WithSummary("Change the global upload limit (1–128 MiB)").ProducesProblem(409).ProducesProblem(400);

        admin.MapGet("/users", async (string? search, int? page, int? pageSize, AppDbContext db, CancellationToken ct) =>
        {
            var number = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 25, 1, 100);
            if (number > 1_000_000 || search?.Length > 254) return Results.Problem("Invalid user search or page.", statusCode: 400);
            var query = db.Users.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToUpperInvariant();
                query = query.Where(u => u.NormalizedEmail != null && u.NormalizedEmail.Contains(term));
            }
            var total = await query.CountAsync(ct);
            var users = await query.OrderBy(u => u.NormalizedEmail).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            var ids = users.Select(u => u.Id).ToArray();
            var memberships = await (from membership in db.UserRoles join role in db.Roles on membership.RoleId equals role.Id
                                     where ids.Contains(membership.UserId) select new { membership.UserId, role.Name }).ToListAsync(ct);
            var totals = await db.Documents.Where(d => ids.Contains(d.OwnerId)).GroupBy(d => d.OwnerId)
                .Select(g => new { OwnerId = g.Key, Count = g.Count(), Bytes = g.Sum(d => (long)d.Content.Length) }).ToListAsync(ct);
            var items = users.Select(u =>
            {
                var roles = memberships.Where(r => r.UserId == u.Id).Select(r => r.Name).ToArray();
                var usage = totals.SingleOrDefault(d => d.OwnerId == u.Id);
                return new AdminUser(u.Id, u.Email!, u.ConcurrencyStamp!, roles.Contains(AccessRoles.Admin) || roles.Contains(AccessRoles.SuperAdmin), roles.Contains(AccessRoles.SuperAdmin), usage?.Count ?? 0, usage?.Bytes ?? 0);
            }).ToArray();
            return Results.Ok(new AdminUserPage(items, total, number, size));
        }).WithSummary("List accounts and document usage").Produces<AdminUserPage>();

        admin.MapPut("/users/{id}/admin", async (string id, AdminRoleChange input, UserManager<IdentityUser> users, ILogger<AppDbContext> logger, System.Security.Claims.ClaimsPrincipal actor) =>
        {
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            if (await users.IsInRoleAsync(user, AccessRoles.SuperAdmin)) return Results.Problem("The bootstrap superadministrator is protected.", statusCode: 400);
            if (string.IsNullOrEmpty(input.Version) || user.ConcurrencyStamp != input.Version) return Stale();
            var current = await users.IsInRoleAsync(user, AccessRoles.Admin);
            if (current != input.IsAdmin)
            {
                var result = input.IsAdmin ? await users.AddToRoleAsync(user, AccessRoles.Admin) : await users.RemoveFromRoleAsync(user, AccessRoles.Admin);
                if (!result.Succeeded) return result.Errors.Any(e => e.Code == "ConcurrencyFailure") ? Stale() : Results.Problem("The role could not be changed.", statusCode: 400);
                logger.LogInformation("Administrator {ActorId} changed administrator access for {TargetId} to {IsAdmin}", Ownership.Owner(actor), id, input.IsAdmin);
            }
            return Results.Ok(new { user.Id, isAdmin = input.IsAdmin, version = user.ConcurrencyStamp });
        }).WithSummary("Grant or revoke administrator access").WithDescription("Administrators can manage ordinary administrator roles. The bootstrap SuperAdmin cannot be demoted. Role changes apply immediately to existing sessions.").ProducesProblem(409);

        admin.MapGet("/users/{id}/documents", async (string id, int? page, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Users.AnyAsync(u => u.Id == id, ct)) return Results.NotFound();
            var number = Math.Clamp(page ?? 1, 1, 1_000_000);
            var query = db.Documents.AsNoTracking().Where(d => d.OwnerId == id);
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(d => d.UpdatedAt).Skip((number - 1) * 50).Take(50)
                .Select(d => new DocumentMetadata(d.Id, d.Title, d.Version, d.CreatedAt, d.UpdatedAt, d.Content.Length)).ToListAsync(ct);
            return Results.Ok(new { items, total, page = number, pageSize = 50 });
        }).WithSummary("List a user's files (administrator access)");

        admin.MapGet("/documents/{id:guid}/preview", async (Guid id, long version, AppDbContext db, ILogger<AppDbContext> logger, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct) =>
        {
            var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
            if (document is null) return Results.NotFound();
            if (document.Version != version) return Stale();
            var preview = Preview(document.Content);
            logger.LogInformation("Administrator {ActorId} previewed document {DocumentId} owned by {OwnerId}", Ownership.Owner(actor), id, document.OwnerId);
            return Results.Ok(new { document.Id, document.Title, document.Version, document.OwnerId, preview.Text, preview.Truncated });
        }).WithSummary("Read a bounded text preview of another user's file").ProducesProblem(409);

        admin.MapGet("/documents/{id:guid}/download", async (Guid id, string? format, long version, AppDbContext db, DocumentConverter converter, ILogger<AppDbContext> logger, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct) =>
        {
            var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
            if (document is null) return Results.NotFound();
            logger.LogInformation("Administrator {ActorId} requested a download of document {DocumentId} owned by {OwnerId}", Ownership.Owner(actor), id, document.OwnerId);
            return await DocumentDownloads.CreateAsync(document, format, version, converter, ct);
        }).WithSummary("Download another user's saved file").WithDescription("Read-only administrative access. Requires the listed revision in version. Supports the same formats as owner downloads.").ProducesProblem(409);
    }

    private static IResult Stale() => Results.Problem("This record changed. Refresh and try again.", statusCode: 409);

    private static (string Text, bool Truncated) Preview(byte[] content)
    {
        if (content.Length == 0) return ("", false);
        using var json = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 256 });
        var text = new StringBuilder();
        var nodes = 0;
        var truncated = false;
        void Walk(JsonElement value, string? name = null)
        {
            if (++nodes > 50_000 || text.Length >= 20_000) { truncated = true; return; }
            if (value.ValueKind == JsonValueKind.String && name is "text" or "Text")
            {
                var part = value.GetString()!;
                var available = 20_000 - text.Length;
                text.Append(part.AsSpan(0, Math.Min(part.Length, available)));
                if (part.Length > available) truncated = true;
                text.AppendLine();
            }
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray()) { Walk(item, name); if (truncated) break; }
            else if (value.ValueKind == JsonValueKind.Object)
                foreach (var property in value.EnumerateObject())
                {
                    if (property.Name is "media" or "data" or "fonts" or "opaque") continue;
                    Walk(property.Value, property.Name);
                    if (truncated) break;
                }
        }
        if (json.RootElement.TryGetProperty("body", out var body)) Walk(body);
        return (text.ToString(), truncated);
    }
}

public sealed record AdminUser(string Id, string Email, string Version, bool IsAdmin, bool IsSuperAdmin, int DocumentCount, long StoredBytes);
public sealed record AdminUserPage(AdminUser[] Items, int Total, int Page, int PageSize);
public sealed record AdminRoleChange(bool IsAdmin, string Version);
public sealed record UploadPolicyChange(int MaxUploadMiB, long Version);

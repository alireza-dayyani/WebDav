using System.Security.Claims;
using System.Security.Cryptography;
using Inkstone.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

public static class AutomationTokens
{
    public const string Claim = "inkstone:automation";
    public static void MapAutomationTokens(this WebApplication app)
    {
        var routes = app.MapGroup("/api/automation-tokens").RequireAuthorization().WithTags("Automation tokens").WithMetadata(new BookmarkValuesRequest()).ValidateRequests();
        routes.AddEndpointFilter(async (context, next) => context.HttpContext.User.HasClaim(c => c.Type == Claim) ? Results.Forbid() : await next(context));
        routes.MapGet("/", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) => Results.Ok(await db.AutomationTokens.AsNoTracking().Where(t => t.OwnerId == Ownership.Owner(user)).OrderByDescending(t => t.CreatedAt)
            .Select(t => new { t.Id, t.Name, t.Scopes, t.ExpiresAt, t.CreatedAt, t.Version }).ToListAsync(ct)));
        routes.MapPost("/", async (TokenCreate input, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            if (Studio.Title(input.Name, 80) is not { } name || input.Scopes is not { Length: > 0 and <= 6 } || input.Scopes.Any(s => !Automation.Scopes.Contains(s)) || input.ExpiresInDays is < 1 or > 365)
                return Studio.Bad("Choose a name, supported scopes and an expiry from 1 to 365 days.");
            var owner = Ownership.Owner(user);
            if (await db.AutomationTokens.CountAsync(t => t.OwnerId == owner, ct) >= 50) return Studio.Bad("Revoke an existing token before creating another.", 409);
            var secret = "ink_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var token = new AutomationToken { OwnerId = owner, Name = name, Hash = Automation.TokenHash(secret), Scopes = string.Join(' ', input.Scopes.Distinct()), ExpiresAt = DateTimeOffset.UtcNow.AddDays(input.ExpiresInDays).ToUnixTimeMilliseconds() };
            db.AutomationTokens.Add(token); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/automation-tokens/{token.Id}", new { token.Id, token.Name, token.Scopes, token.Version, token.ExpiresAt, token = secret });
        });
        routes.MapDelete("/{id:guid}", async (Guid id, long version, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var token = await db.AutomationTokens.SingleOrDefaultAsync(t => t.Id == id && t.OwnerId == Ownership.Owner(user), ct);
            if (token is null) return Results.NotFound();
            if (token.Version != version) return Studio.Conflict();
            db.AutomationTokens.Remove(token); return await Studio.Save(db, ct) ? Results.NoContent() : Studio.Conflict();
        });
    }

    public static async Task Authenticate(HttpContext context, RequestDelegate next)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.Length == 0) { await next(context); return; }
        if (!header.StartsWith("Bearer ink_", StringComparison.Ordinal) || header.Length != 75) { context.Response.StatusCode = 401; return; }
        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        var hash = Automation.TokenHash(header[7..]);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var token = await db.AutomationTokens.AsNoTracking().SingleOrDefaultAsync(t => t.Hash == hash && t.ExpiresAt > now, context.RequestAborted);
        if (token is null) { context.Response.StatusCode = 401; return; }
        var account = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == token.OwnerId, context.RequestAborted);
        if (account is null || (account.LockoutEnabled && account.LockoutEnd > DateTimeOffset.UtcNow)) { context.Response.StatusCode = 401; return; }
        var scopes = token.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var needed = RequiredScopes(context.Request);
        if (needed.Length == 0 || needed.Any(scope => !scopes.Contains(scope) && !(scope.EndsWith(":read", StringComparison.Ordinal) && scopes.Contains(scope.Replace(":read", ":write", StringComparison.Ordinal)))))
        { context.Response.StatusCode = 403; return; }
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, token.OwnerId), new(ClaimTypes.Name, account.Email ?? "Automation"), new(Claim, token.Id.ToString()) };
        claims.AddRange(scopes.Select(scope => new Claim("inkstone:scope", scope)));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "InkstoneToken"));
        await next(context);
    }

    private static string[] RequiredScopes(HttpRequest request)
    {
        // ASP.NET routes ignore case and trailing slashes; scope checks must use the same identity.
        var path = (request.Path.Value ?? "").TrimEnd('/').ToLowerInvariant();
        var read = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || path.EndsWith("/search", StringComparison.Ordinal) || path.EndsWith("/validate", StringComparison.Ordinal);
        if (path == "/api/document-capabilities") return ["documents:read"];
        if (request.Path.StartsWithSegments("/api/documents")) return [read ? "documents:read" : "documents:write"];
        if (request.Path.StartsWithSegments("/api/templates"))
        {
            if (path.EndsWith("/render", StringComparison.Ordinal) || path.EndsWith("/render-batch", StringComparison.Ordinal)) return ["templates:read", "documents:write"];
            if (HttpMethods.IsPost(request.Method) && path.TrimEnd('/') == "/api/templates") return ["templates:write", "documents:read"];
            return [read ? "templates:read" : "templates:write"];
        }
        if (request.Path.StartsWithSegments("/api/document-jobs")) return read ? ["jobs:read"] : ["jobs:write"];
        return [];
    }

    public static bool HasScope(ClaimsPrincipal user, string area) => !user.HasClaim(c => c.Type == Claim)
        || user.HasClaim("inkstone:scope", area + ":read") || user.HasClaim("inkstone:scope", area + ":write");
}

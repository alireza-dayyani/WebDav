using static Ownership;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Inkstone.Web.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var dataDirectory = Path.GetFullPath(builder.Configuration["Storage:DataDirectory"] ?? "App_Data", builder.Environment.ContentRootPath);
Directory.CreateDirectory(dataDirectory);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 12 * 1024 * 1024);
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(dataDirectory, "inkstone.db")}"));
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys"))).SetApplicationName("Inkstone");
builder.Services.AddIdentity<IdentityUser, IdentityRole>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 10;
    o.Password.RequireNonAlphanumeric = false;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "Inkstone.Session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromDays(7);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = "Inkstone.Csrf";
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("accounts", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.MaxDepth = 256);
var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self'; frame-src 'self' blob:; object-src 'none'; base-uri 'self'; frame-ancestors 'self'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
// Validate every API mutation, including login/logout; raw JSON endpoints do not get automatic validation.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { await Results.Problem("Refresh the page and try again.", statusCode: 400).ExecuteAsync(context); return; }
    }
    await next(context);
});
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/csrf", (HttpContext c, IAntiforgery a) => Results.Ok(new { token = a.GetAndStoreTokens(c).RequestToken }));
var accounts = app.MapGroup("/api/account").RequireRateLimiting("accounts");
accounts.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new { email = user.Identity!.Name })).RequireAuthorization();
accounts.MapPost("/register", async (Credentials input, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) =>
{
    if (input.Email is null || input.Email.Length > 254 || !new EmailAddressAttribute().IsValid(input.Email) || input.Password is null || input.Password.Length > 128)
        return Results.Problem("Enter a valid email and a password of 10–128 characters.", statusCode: 400);
    var user = new IdentityUser { UserName = input.Email.Trim(), Email = input.Email.Trim() };
    var result = await users.CreateAsync(user, input.Password);
    if (!result.Succeeded) return Results.ValidationProblem(result.Errors.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
    await signIn.SignInAsync(user, isPersistent: true);
    return Results.Ok(new { email = user.Email });
});
accounts.MapPost("/login", async (Credentials input, SignInManager<IdentityUser> signIn) =>
{
    if (input.Email is null || input.Password is null || input.Email.Length > 254 || input.Password.Length > 128)
        return Results.Problem("Email or password is incorrect.", statusCode: 401);
    var result = await signIn.PasswordSignInAsync(input.Email.Trim(), input.Password, isPersistent: true, lockoutOnFailure: true);
    return result.Succeeded ? Results.Ok(new { email = input.Email.Trim() }) : Results.Problem(result.IsLockedOut ? "Too many attempts. Try again in 15 minutes." : "Email or password is incorrect.", statusCode: 401);
});
accounts.MapPost("/logout", async (SignInManager<IdentityUser> signIn) => { await signIn.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization();

var documents = app.MapGroup("/api/documents").RequireAuthorization();
documents.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
    Results.Ok(await db.Documents.AsNoTracking().Where(d => d.OwnerId == Owner(user)).OrderByDescending(d => d.UpdatedAt)
        .Select(d => new { d.Id, d.Title, d.Version, d.CreatedAt, d.UpdatedAt, size = d.Content.Length }).ToListAsync()));
documents.MapPost("/", async (DocumentTitle input, ClaimsPrincipal user, AppDbContext db) =>
{
    var title = CleanTitle(input.Title);
    if (title is null) return Results.Problem("A title must contain 1–160 characters.", statusCode: 400);
    var document = new StoredDocument { OwnerId = Owner(user), Title = title };
    db.Documents.Add(document);
    await db.SaveChangesAsync();
    return Results.Created($"/api/documents/{document.Id}", Metadata(document));
});
documents.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, AppDbContext db) =>
{
    var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.OwnerId == Owner(user));
    return document is null ? Results.NotFound() : Results.Ok(Metadata(document));
});
documents.MapGet("/{id:guid}/content", async (Guid id, ClaimsPrincipal user, AppDbContext db) =>
{
    var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.OwnerId == Owner(user));
    if (document is null) return Results.NotFound();
    return document.Content.Length == 0 ? Results.NoContent() : Results.Bytes(document.Content, "application/json");
});
// Metadata and content come from one database read, so an opening tab cannot mix revisions.
documents.MapGet("/{id:guid}/snapshot", async (Guid id, ClaimsPrincipal user, AppDbContext db) =>
{
    var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.OwnerId == Owner(user));
    if (document is null) return Results.NotFound();
    JsonElement? content = document.Content.Length == 0 ? null : JsonSerializer.Deserialize<JsonElement>(document.Content, new JsonSerializerOptions { MaxDepth = 256 });
    return Results.Ok(new { metadata = Metadata(document), document = content });
});
documents.MapPut("/{id:guid}/content", async (Guid id, SaveDocument input, ClaimsPrincipal user, AppDbContext db) =>
{
    var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == id && d.OwnerId == Owner(user));
    if (document is null) return Results.NotFound();
    if (document.Version != input.Version) return Conflict();
    if (input.Document.ValueKind != JsonValueKind.Object || !input.Document.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Array)
        return Results.Problem("The document format is invalid.", statusCode: 400);
    var bytes = JsonSerializer.SerializeToUtf8Bytes(input.Document);
    if (bytes.Length > 10 * 1024 * 1024) return Results.Problem("Documents can be up to 10 MB.", statusCode: 413);
    document.Content = bytes;
    document.Version++;
    document.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateConcurrencyException) { return Conflict(); }
    return Results.Ok(Metadata(document));
});
documents.MapPatch("/{id:guid}", async (Guid id, RenameDocument input, ClaimsPrincipal user, AppDbContext db) =>
{
    var title = CleanTitle(input.Title);
    if (title is null) return Results.Problem("A title must contain 1–160 characters.", statusCode: 400);
    var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == id && d.OwnerId == Owner(user));
    if (document is null) return Results.NotFound();
    if (document.Version != input.Version) return Conflict();
    document.Title = title;
    document.Version++;
    document.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    try { await db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { return Conflict(); }
    return Results.Ok(Metadata(document));
});
documents.MapDelete("/{id:guid}", async (Guid id, long version, ClaimsPrincipal user, AppDbContext db) =>
{
    var count = await db.Documents.Where(d => d.Id == id && d.OwnerId == Owner(user) && d.Version == version).ExecuteDeleteAsync();
    if (count != 0) return Results.NoContent();
    return await db.Documents.AnyAsync(d => d.Id == id && d.OwnerId == Owner(user)) ? Conflict() : Results.NotFound();
});
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.Run();

static string? CleanTitle(string? title) => title?.Trim() is { Length: > 0 and <= 160 } value && !value.Any(char.IsControl) ? value : null;
static object Metadata(StoredDocument d) => new { d.Id, d.Title, d.Version, d.CreatedAt, d.UpdatedAt, size = d.Content.Length };
static IResult Conflict() => Results.Problem("This document changed in another tab. Download your current work before reopening the saved copy.", statusCode: 409);

public sealed record Credentials(string? Email, string? Password);
public sealed record DocumentTitle(string? Title);
public sealed record RenameDocument(string? Title, long Version);
public sealed record SaveDocument(long Version, JsonElement Document);
public partial class Program;

static class Ownership
{
    public static string Owner(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;
}


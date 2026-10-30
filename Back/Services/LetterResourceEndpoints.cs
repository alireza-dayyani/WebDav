using System.Security.Claims;
using Inkstone.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

public static class LetterResourceEndpoints
{
    public static void MapLetterResources(this WebApplication app)
    {
        var senders = app.MapGroup("/api/letters/senders").RequireAuthorization().WithTags("Letter sender profiles").WithMetadata(new BookmarkValuesRequest());
        senders.MapGet("/", (int? page, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) => Studio.List(db, Ownership.Owner(user), "sender", page, ct));
        senders.MapPost("/", async (SaveSender input, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var profile = Clean(input);
            if (profile is null) return Studio.Bad("A sender name and title (up to 160 characters) are required; organization/address may contain at most 500 characters.");
            var item = new WorkspaceItem { OwnerId = Ownership.Owner(user), Kind = "sender", Title = profile.Title, Data = Studio.Encode(profile) };
            db.WorkspaceItems.Add(item); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/letters/senders/{item.Id}?version=1", Studio.Detail(item));
        }).Produces<ResourceDetail>(201).WithSummary("Save a private sender identity/title for letter creation");
        senders.MapGet("/{id:guid}", async (Guid id, long version, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "sender", id, ct, false);
            return item is null ? Results.NotFound() : item.Version != version ? Studio.Conflict() : Results.Ok(Studio.Detail(item));
        }).Produces<ResourceDetail>();
        senders.MapPut("/{id:guid}", async (Guid id, SaveSender input, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "sender", id, ct);
            if (item is null) return Results.NotFound();
            if (item.Version != input.Version) return Studio.Conflict();
            var profile = Clean(input);
            if (profile is null) return Studio.Bad("Invalid sender name, title, organization or address.");
            item.Title = profile.Title; item.Data = Studio.Encode(profile); Studio.Touch(item);
            return await Studio.Save(db, ct) ? Results.Ok(Studio.Detail(item)) : Studio.Conflict();
        }).Produces<ResourceDetail>();
        senders.MapDelete("/{id:guid}", (Guid id, long version, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) => Studio.Delete(db, Ownership.Owner(user), "sender", id, version, ct));

        var images = app.MapGroup("/api/letters/images").RequireAuthorization().WithTags("Letter images").WithMetadata(new BookmarkValuesRequest());
        images.MapGet("/", (int? page, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) => Studio.List(db, Ownership.Owner(user), "image", page, ct));
        images.MapPost("/", async ([FromForm] IFormFile file, [FromForm] string? title, ClaimsPrincipal user, AppDbContext db, DocumentConverter converter, CancellationToken ct) =>
        {
            if (file.Length is <= 0 or > 512 * 1024) return Results.Problem("Choose a PNG or JPEG image within 512 KiB.", statusCode: 413,
                extensions: new Dictionary<string, object?> { ["code"] = "image_too_large", ["maxBytes"] = 512 * 1024 });
            if (Studio.Title(title) is not { } name) return Studio.Bad("An image title is required.");
            using var buffer = new MemoryStream(); await file.CopyToAsync(buffer, ct);
            var content = buffer.ToArray();
            var mime = content.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? "image/png"
                : content.AsSpan().StartsWith(new byte[] { 255, 216, 255 }) ? "image/jpeg" : null;
            if (mime is null) return Studio.Bad("Only PNG and JPEG images are accepted.", 415);
            var info = await converter.ImageInfoAsync(content, ct);
            var descriptor = new ImageDescriptor(mime, info.GetProperty("width").GetInt32(), info.GetProperty("height").GetInt32());
            var item = new WorkspaceItem { OwnerId = Ownership.Owner(user), Kind = "image", Title = name, Data = Studio.Encode(descriptor), Content = content };
            db.WorkspaceItems.Add(item); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/letters/images/{item.Id}?version=1", Studio.Detail(item));
        }).WithMetadata(new BookmarkValuesRequest()).WithSummary("Upload a private signature or logo image (PNG/JPEG, 512 KiB, 4096 pixels)").Produces<ResourceDetail>(201).ProducesProblem(413).ProducesProblem(415).ProducesProblem(422);
        images.MapGet("/{id:guid}", async (Guid id, long version, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var item = await Studio.Find(db, Ownership.Owner(user), "image", id, ct, false);
            if (item is null) return Results.NotFound();
            if (item.Version != version) return Studio.Conflict();
            var info = Studio.Read<ImageDescriptor>(item);
            return Results.File(item.Content, info.Mime);
        }).WithSummary("Read an owned image revision; image IDs do not grant access");
        images.MapDelete("/{id:guid}", (Guid id, long version, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) => Studio.Delete(db, Ownership.Owner(user), "image", id, version, ct));

        app.MapGet("/api/letters/directory", async (string? search, int? page, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var index = page ?? 1;
            if (index is < 1 or > 100000 || search?.Length > 100) return Studio.Bad("Invalid directory search or page.");
            var term = search?.Trim().ToUpperInvariant();
            var query = db.Users.AsNoTracking();
            query = string.IsNullOrEmpty(term) ? query.Where(u => u.Id == Ownership.Owner(user)) : query.Where(u => u.NormalizedEmail == term);
            var users = await query.OrderBy(u => u.NormalizedEmail).ThenBy(u => u.Id).Skip((index - 1) * 50).Take(51)
                .Select(u => new { u.Id, u.Email }).ToListAsync(ct);
            return Results.Ok(new { page = index, hasMore = users.Count > 50, items = users.Take(50).Select(u => new { u.Id, u.Email, name = u.Email!.Split('@')[0] }) });
        }).RequireAuthorization().WithTags("Letters").WithSummary("Search registered users for letter recipients")
          .WithDescription("Blank search returns only the signed-in account. Other recipients require an exact email address; partial search and directory browsing are unavailable. Lookup never grants file access or verifies email ownership.");
    }

    private static SenderProfile? Clean(SaveSender input)
    {
        if (Studio.Title(input.FullName) is not { } name || Studio.Title(input.Title) is not { } title || input.Organization?.Length > 500 || input.Address?.Length > 500
            || (input.Organization ?? "").Concat(input.Address ?? "").Any(c => c == '\uFFFC' || (char.IsControl(c) && c is not '\n' and not '\r' and not '\t')))
            return null;
        return new SenderProfile(name, title, input.Organization?.Trim() ?? "", input.Address?.Trim() ?? "");
    }
}

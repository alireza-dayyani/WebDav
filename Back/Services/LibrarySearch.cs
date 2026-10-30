using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

public static class LibrarySearch
{
    public const int CharacterLimit = 1_000_000;
    public static string Normalize(string value) => new(value.Normalize(NormalizationForm.FormKC)
        .Replace('ي', 'ی').Replace('ى', 'ی').Replace('ك', 'ک').Replace('\u200c', ' ')
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());

    public static async Task EnsureAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("CREATE VIRTUAL TABLE IF NOT EXISTS DocumentSearch USING fts5(DocumentId UNINDEXED, Text, tokenize='unicode61')", ct);
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER IF NOT EXISTS Documents_Search_Delete AFTER DELETE ON Documents BEGIN DELETE FROM DocumentSearch WHERE DocumentId = old.Id COLLATE NOCASE; END", ct);
    }
    public static async Task IndexAsync(AppDbContext db, StoredDocument document, CancellationToken ct)
    {
        var text = new StringBuilder(document.Title + " ");
        if (document.Content.Length > 0)
        {
            using var json = JsonDocument.Parse(document.Content, new JsonDocumentOptions { MaxDepth = 256 });
            Read(json.RootElement, text);
        }
        var id = document.Id.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM DocumentSearch WHERE DocumentId = {id} COLLATE NOCASE", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO DocumentSearch(DocumentId, Text) VALUES ({id}, {Normalize(text.ToString())})", ct);
    }
    private static void Read(JsonElement value, StringBuilder output)
    {
        if (output.Length >= CharacterLimit) return;
        if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) { Read(child, output); if (output.Length >= CharacterLimit) break; }
        else if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name == "text" && property.Value.ValueKind == JsonValueKind.String)
                {
                    var text = property.Value.GetString()!;
                    var length = Math.Min(text.Length, Math.Max(0, CharacterLimit - output.Length));
                    // The limit counts UTF-16 units. Never leave half of a Unicode
                    // scalar for Normalize(), which rejects unpaired surrogates.
                    if (length > 0 && length < text.Length && char.IsHighSurrogate(text[length - 1]))
                    {
                        output.Append(text.AsSpan(0, length - 1));
                        output.Append(' ');
                    }
                    else
                    {
                        output.Append(text.AsSpan(0, length));
                        if (output.Length < CharacterLimit) output.Append(' ');
                    }
                }
                else Read(property.Value, output);
                if (output.Length >= CharacterLimit) break;
            }
    }
    public static async Task BackfillAsync(AppDbContext db)
    {
        await EnsureAsync(db);
        // Only missing entries are read; normal saves maintain the index transactionally.
        while (true)
        {
            var missing = await db.Documents.FromSqlRaw("SELECT d.* FROM Documents d WHERE NOT EXISTS (SELECT 1 FROM DocumentSearch s WHERE s.DocumentId = d.Id COLLATE NOCASE)")
                .IgnoreQueryFilters().AsNoTracking().OrderBy(d => d.Id).Take(50).ToListAsync();
            if (missing.Count == 0) break;
            await using var tx = await db.Database.BeginTransactionAsync();
            foreach (var document in missing) await IndexAsync(db, document, default);
            await tx.CommitAsync();
        }
    }
    public static void MapLibrarySearch(this WebApplication app) => app.MapGet("/api/library", async (string? q, int? page, bool? trash, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
    {
        var index = page ?? 1;
        if (index is < 1 or > 100000 || q?.Length > 200) return Studio.Bad("Search must be at most 200 characters and page must be positive.");
        var terms = Normalize(q ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length > 16) return Studio.Bad("Use at most 16 search terms.");
        var query = db.Documents.AsQueryable();
        if (terms.Length > 0)
        {
            var match = string.Join(" AND ", terms.Select(t => "\"" + t.Replace("\"", "\"\"") + "\"*"));
            query = db.Documents.FromSqlInterpolated($"SELECT d.* FROM Documents d JOIN DocumentSearch s ON s.DocumentId = d.Id COLLATE NOCASE WHERE DocumentSearch MATCH {match}");
        }
        if (trash == true) query = query.IgnoreQueryFilters().Where(d => db.TrashedDocuments.Any(t => t.DocumentId == d.Id));
        var owner = Ownership.Owner(user);
        var shared = !user.HasClaim(c => c.Type == AutomationTokens.Claim) && trash != true;
        query = query.Where(d => d.OwnerId == owner || (shared && db.DocumentGrants.Any(g => g.DocumentId == d.Id && g.UserId == owner)));
        var total = await query.CountAsync(ct);
        var rows = await query.AsNoTracking().OrderByDescending(d => d.UpdatedAt).ThenBy(d => d.Id).Skip((index - 1) * 50).Take(50)
            .Select(d => new { d.Id, d.Title, d.Version, d.CreatedAt, d.UpdatedAt, size = d.Content.Length, permission = d.OwnerId == owner ? "owner" : db.DocumentGrants.Where(g => g.DocumentId == d.Id && g.UserId == owner).Select(g => g.Permission).First() }).ToListAsync(ct);
        return Results.Ok(new { page = index, total, hasMore = (long)index * 50 < total, items = rows, indexedCharacterLimit = CharacterLimit });
    }).RequireAuthorization().WithTags("Library");
}

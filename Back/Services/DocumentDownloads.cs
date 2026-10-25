using Inkstone.Web.Data;

namespace Inkstone.Web.Services;

public static class DocumentDownloads
{
    public static async Task<IResult> CreateAsync(StoredDocument document, string? format, long? version, DocumentConverter converter, CancellationToken ct)
    {
        if (version.HasValue && document.Version != version.Value)
            return Results.Problem("The saved document changed. Refresh its details and try again.", statusCode: 409);
        format = (format ?? "docx").ToLowerInvariant();
        var bytes = await converter.ExportAsync(document.Content, format, ct);
        var name = string.Concat(document.Title.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        if (name.Length == 0) name = "document";
        var mime = format switch { "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "pdf" => "application/pdf", "json" => "application/json", "md" => "text/markdown; charset=utf-8", _ => "text/plain; charset=utf-8" };
        return Results.File(bytes, mime, name + "." + format);
    }
}

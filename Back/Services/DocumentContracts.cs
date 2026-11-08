using System.Text.Json;
using Inkstone.Web.Data;
using Microsoft.AspNetCore.Http;

public sealed record DocumentSnapshot(DocumentMetadata Metadata, JsonElement? Document);
public sealed record DocumentTitle(string? Title);
public sealed record DocumentMetadata(Guid Id, string Title, long Version, long CreatedAt, long UpdatedAt, int Size);
public sealed record RenameDocument(string? Title, long Version);
public sealed record SaveDocument(long Version, JsonElement Document);

public static class DocumentEndpointHelpers
{
    public static string? CleanTitle(string? title) =>
        title?.Trim() is { Length: > 0 and <= 160 } value && !value.Any(char.IsControl) ? value : null;

    public static DocumentMetadata Metadata(StoredDocument d) =>
        new(d.Id, d.Title, d.Version, d.CreatedAt, d.UpdatedAt, d.Content.Length);

    public static IResult Conflict() =>
        Results.Problem(
            "This document changed in another tab. Download your current work before reopening the saved copy.",
            statusCode: 409);
}

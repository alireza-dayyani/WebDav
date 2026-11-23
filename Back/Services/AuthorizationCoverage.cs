namespace Papyrus.Web.Services;

/// <summary>Authorization audit manifest. Update when document, job, or workspace routes change.</summary>
public static class AuthorizationCoverage
{
    public sealed record EndpointModule(string FileName, params string[] RequiredMarkers);

    public static IReadOnlyList<EndpointModule> DocumentModules { get; } =
    [
        new("DocumentEndpoints.cs", "DocumentAccess", "Ownership.Owner"),
        new("DocumentAutomationEndpoints.cs", "DocumentAccess.FindAsync", "Automation.ReadSources"),
        new("CollaborationEndpoints.cs", "DocumentAccess", "Owned("),
        new("BookmarkEndpoints.cs", "DocumentAccess"),
        new("DocumentJobs.cs", "Ownership.Owner", "AutomationTokens.HasScope", "Automation.ReadSources", "Automation.EnsureJobSourcesAsync"),
        new("LibraryRecovery.cs", "Ownership.Owner"),
        new("LibrarySearch.cs", "Ownership.Owner"),
        new("RevisionRetention.cs", "Ownership.Owner"),
    ];

    public static IReadOnlySet<string> DocumentApiRoutes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "/api/documents/formats",
        "/api/documents",
        "/api/documents/upload",
        "/api/documents/{id}/download",
        "/api/documents/{id}",
        "/api/documents/{id}/content",
        "/api/documents/{id}/snapshot",
        "/api/documents/{id}/bookmarks",
        "/api/documents/{id}/bookmarks/replace",
        "/api/documents/trash",
        "/api/documents/{id}/recover",
        "/api/documents/{id}/purge",
        "/api/documents/compare",
        "/api/documents/compose",
        "/api/templates/{id}/render-batch",
        "/api/document-capabilities",
        "/api/document-jobs",
        "/api/document-jobs/{id}",
        "/api/document-jobs/{id}/download",
        "/api/document-jobs/{id}/cancel",
        "/api/library",
        "/api/storage",
        "/api/retention",
        "/api/retention/preview",
        "/api/retention/apply",
        "/api/documents/{id}/access",
        "/api/documents/{id}/shares",
        "/api/documents/{id}/shares/{userId}",
        "/api/documents/{id}/audit",
        "/api/documents/{id}/reviews",
        "/api/documents/{id}/structure",
        "/api/documents/{id}/tables",
        "/api/documents/{id}/controls",
        "/api/documents/{id}/styles",
        "/api/documents/{id}/images",
        "/api/documents/{id}/images/{reference}",
        "/api/documents/{id}/properties",
        "/api/documents/{id}/comments",
        "/api/documents/{id}/changes",
        "/api/documents/{id}/search",
        "/api/documents/{id}/validate",
        "/api/documents/{id}/batch",
        "/api/documents/{id}/preview",
        "/api/documents/{id}/pages/{page}/preview",
        "/api/documents/{id}/controls/fill",
        "/api/documents/{id}/styles/apply",
        "/api/documents/{id}/tables/{reference}",
        "/api/documents/{id}/changes/resolve",
        "/api/documents/{id}/normalize",
        "/api/documents/{id}/versions",
        "/api/documents/{id}/versions/{revision}/download",
        "/api/documents/{id}/restore",
        "/api/reviews",
        "/api/reviews/{reviewId}/decision",
        "/api/reviews/{reviewId}/download",
    };
}
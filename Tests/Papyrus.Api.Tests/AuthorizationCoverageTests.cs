using System.Text.RegularExpressions;
using Papyrus.Web.Services;

namespace Papyrus.Api.Tests;

public sealed class AuthorizationCoverageTests
{
    private static string ServicesDirectory => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Back", "Services"));

    [Fact]
    public void Document_endpoint_modules_include_required_authorization_markers()
    {
        foreach (var module in AuthorizationCoverage.DocumentModules)
        {
            var path = Path.Combine(ServicesDirectory, module.FileName);
            Assert.True(File.Exists(path), "Missing endpoint module " + module.FileName);
            var source = File.ReadAllText(path);
            foreach (var marker in module.RequiredMarkers)
                Assert.True(source.Contains(marker, StringComparison.Ordinal), $"{module.FileName} must reference {marker}.");
        }
    }

    [Fact]
    public void Document_api_routes_match_endpoint_definitions()
    {
        var discovered = DiscoverDocumentRoutes();
        var expected = AuthorizationCoverage.DocumentApiRoutes;
        var missingFromManifest = discovered.Except(expected, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        var staleManifest = expected.Except(discovered, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        Assert.True(missingFromManifest.Length == 0, "Add these routes to AuthorizationCoverage.DocumentApiRoutes: " + string.Join(", ", missingFromManifest));
        Assert.True(staleManifest.Length == 0, "Remove or remap stale manifest routes: " + string.Join(", ", staleManifest));
    }

    private static HashSet<string> DiscoverDocumentRoutes()
    {
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new[]
        {
            "DocumentEndpoints.cs", "DocumentAutomationEndpoints.cs", "CollaborationEndpoints.cs",
            "BookmarkEndpoints.cs", "DocumentJobs.cs", "LibraryRecovery.cs", "LibrarySearch.cs", "RevisionRetention.cs",
        };
        foreach (var file in files)
        {
            var text = File.ReadAllText(Path.Combine(ServicesDirectory, file));
            if (file == "CollaborationEndpoints.cs")
            {
                var parts = text.Split("var reviews = app.MapGroup", 2);
                AddMappedRoutes(routes, "/api/documents/{id:guid}", parts[0]);
                if (parts.Length == 2) AddMappedRoutes(routes, "/api/reviews", "var reviews = app.MapGroup" + parts[1]);
                continue;
            }
            var groups = Regex.Matches(text, """MapGroup\("([^"]+)"\)""").Select(m => m.Groups[1].Value).ToArray();
            var primaryGroup = file switch
            {
                "BookmarkEndpoints.cs" => "/api/documents",
                _ => groups.FirstOrDefault() ?? "",
            };
            AddMappedRoutes(routes, primaryGroup, text);
            if (file == "DocumentAutomationEndpoints.cs")
            {
                const string prefix = "/api/documents/{id}";
                foreach (var action in new[] { "structure", "tables", "controls", "styles", "images", "properties", "comments", "changes" })
                    routes.Add(NormalizeRoute($"{prefix}/{action}"));
            }
        }
        return routes;
    }

    private static void AddMappedRoutes(HashSet<string> routes, string group, string text)
    {
        foreach (Match match in Regex.Matches(text, "Map(?:Get|Post|Put|Patch|Delete)\\(\"([^\"]+)\""))
        {
            var relative = match.Groups[1].Value;
            if (relative.StartsWith("/api", StringComparison.Ordinal))
                routes.Add(NormalizeRoute(relative));
            else if (group.Length > 0)
                routes.Add(NormalizeRoute(Combine(group, relative)));
        }
    }

    private static string Combine(string group, string relative)
    {
        if (relative == "/") return group.TrimEnd('/');
        return group.TrimEnd('/') + (relative.StartsWith('/') ? relative : "/" + relative);
    }

    private static string NormalizeRoute(string route) =>
        route.Replace("{id:guid}", "{id}", StringComparison.Ordinal)
            .Replace("{revision:long}", "{revision}", StringComparison.Ordinal)
            .Replace("{page:int}", "{page}", StringComparison.Ordinal)
            .Replace("{artifactId:guid}", "{artifactId}", StringComparison.Ordinal)
            .Replace("{userId}", "{userId}", StringComparison.Ordinal)
            .Replace("{reviewId:guid}", "{reviewId}", StringComparison.Ordinal);
}
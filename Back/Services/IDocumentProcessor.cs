using System.Text.Json;

namespace Inkstone.Web.Services;

/// <summary>The engine boundary used by document automation and background jobs.</summary>
public interface IDocumentProcessor
{
    Task<AutomationResult> AutomateAsync(byte[] content, string action, object parameters, CancellationToken ct, byte[][]? sources = null);
    Task<byte[]> CreateAsync(string language, CancellationToken ct);
    Task<byte[]> ExportAsync(byte[] content, string format, CancellationToken ct);
    Task<JsonElement> PreflightAsync(byte[] content, CancellationToken ct);
    Task<byte[]> RenderPageAsync(byte[] content, int page, float scale, CancellationToken ct);
}

using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

public sealed class UploadPolicyRequest;

public static class FileLimits
{
    public const int MiB = 1024 * 1024;
    public const int MaxUploadMiB = 128;
    public const int MaxModelBytes = 256 * MiB;
    public const long MaxRequestBytes = MaxModelBytes + MiB;
    public const int MaxArchiveEntryBytes = 128 * MiB;
    public const int MaxArchiveBytes = 256 * MiB;

    public static async Task<UploadPolicy> ReadAsync(AppDbContext db, CancellationToken ct = default) =>
        await db.UploadPolicies.AsNoTracking().SingleAsync(p => p.Id == 1, ct);

    public static object Describe(UploadPolicy policy) => new
    {
        policy.MaxUploadMiB, policy.Version,
        maxUploadBytes = (long)policy.MaxUploadMiB * MiB,
        maxConfigurableUploadMiB = MaxUploadMiB,
        maxDocumentBytes = MaxModelBytes,
        maxExpandedArchiveBytes = MaxArchiveBytes
    };
}

namespace Inkstone.Web.Data;

public sealed class DocumentGrant
{
    public Guid DocumentId { get; set; }
    public required string UserId { get; set; }
    // Revoked rows are hidden by the query filter but preserve the concurrency
    // counter when access is granted again. Document/account deletion cascades.
    public required string Permission { get; set; }
    public long Version { get; set; } = 1;
}
public sealed class DocumentReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public long DocumentVersion { get; set; }
    public required string ReviewerId { get; set; }
    public string Status { get; set; } = "pending";
    public string Note { get; set; } = "";
    public required string ContentHash { get; set; }
    public long Version { get; set; } = 1;
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
public sealed class DocumentAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public required string ActorId { get; set; }
    public required string Action { get; set; }
    public long DocumentVersion { get; set; }
    public string Detail { get; set; } = "";
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

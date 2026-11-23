using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<StoredDocument> Documents => Set<StoredDocument>();
    public DbSet<UploadPolicy> UploadPolicies => Set<UploadPolicy>();
    public DbSet<WorkspaceItem> WorkspaceItems => Set<WorkspaceItem>();
    public DbSet<LetterArtifact> LetterArtifacts => Set<LetterArtifact>();
    public DbSet<DocumentRevision> DocumentRevisions => Set<DocumentRevision>();
    public DbSet<OperationReceipt> OperationReceipts => Set<OperationReceipt>();
    public DbSet<AutomationToken> AutomationTokens => Set<AutomationToken>();
    public DbSet<DocumentJob> DocumentJobs => Set<DocumentJob>();
    public DbSet<TrashedDocument> TrashedDocuments => Set<TrashedDocument>();
    public DbSet<RetentionPolicy> RetentionPolicies => Set<RetentionPolicy>();
    public DbSet<DocumentGrant> DocumentGrants => Set<DocumentGrant>();
    public DbSet<DocumentReview> DocumentReviews => Set<DocumentReview>();
    public DbSet<DocumentAudit> DocumentAudits => Set<DocumentAudit>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(cancellationToken) : null;
        await Inkstone.Web.Services.LibrarySearch.EnsureAsync(this, cancellationToken);
        ChangeTracker.DetectChanges();
        var changed = ChangeTracker.Entries<StoredDocument>().Where(e => e.State is EntityState.Added or EntityState.Modified).Select(e => e.Entity).ToArray();
        foreach (var entry in ChangeTracker.Entries<StoredDocument>().Where(e => e.State is EntityState.Added or EntityState.Modified).ToArray())
        {
            var document = entry.Entity;
            if (entry.State == EntityState.Modified)
            {
                var previous = entry.OriginalValues;
                var oldVersion = previous.GetValue<long>(nameof(StoredDocument.Version));
                var oldTitle = previous.GetValue<string>(nameof(StoredDocument.Title)) ?? "";
                var oldContent = previous.GetValue<byte[]>(nameof(StoredDocument.Content)) ?? [];
                if (oldVersion != document.Version && !await HasRevisionAsync(document.Id, oldVersion, cancellationToken)
                    && (!SameBytes(oldContent, document.Content) || oldTitle != document.Title))
                    DocumentRevisions.Add(new DocumentRevision { DocumentId = document.Id, Version = oldVersion, Title = oldTitle, Content = oldContent.ToArray(), CreatedAt = previous.GetValue<long>(nameof(StoredDocument.UpdatedAt)) });
            }
            if (!await HasRevisionAsync(document.Id, document.Version, cancellationToken) && !await RedundantRevisionAsync(document, cancellationToken))
                DocumentRevisions.Add(new DocumentRevision { DocumentId = document.Id, Version = document.Version, Title = document.Title, Content = document.Content.ToArray(), CreatedAt = document.UpdatedAt });
        }
        var saved = await base.SaveChangesAsync(cancellationToken);
        foreach (var document in changed)
        {
            await Inkstone.Web.Services.LibrarySearch.IndexAsync(this, document, cancellationToken);
            var policy = await RetentionPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.OwnerId == document.OwnerId && p.Enabled, cancellationToken);
            if (policy is not null) await Inkstone.Web.Services.RevisionRetention.Eligible(this, document.OwnerId, policy, document.Id).ExecuteDeleteAsync(cancellationToken);
        }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    private Task<bool> HasRevisionAsync(Guid documentId, long version, CancellationToken ct)
        => DocumentRevisions.Local.Any(r => r.DocumentId == documentId && r.Version == version)
            ? Task.FromResult(true)
            : DocumentRevisions.AnyAsync(r => r.DocumentId == documentId && r.Version == version, ct);

    private async Task<bool> RedundantRevisionAsync(StoredDocument document, CancellationToken ct)
    {
        var latest = await DocumentRevisions.AsNoTracking().Where(r => r.DocumentId == document.Id).OrderByDescending(r => r.Version).FirstOrDefaultAsync(ct);
        return latest is not null && latest.Title == document.Title && SameBytes(latest.Content, document.Content);
    }

    private static bool SameBytes(byte[] left, byte[] right) => left.AsSpan().SequenceEqual(right);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<DocumentGrant>(e => { e.HasKey(x => new { x.DocumentId, x.UserId }); e.HasQueryFilter(x => x.Permission == "read" || x.Permission == "edit"); e.Property(x => x.Version).IsConcurrencyToken(); e.HasOne<StoredDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade); e.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<DocumentReview>(e => { e.HasKey(x => x.Id); e.HasIndex(x => new { x.DocumentId, x.DocumentVersion, x.ReviewerId }).IsUnique().HasFilter("Status = 'pending'"); e.HasIndex(x => new { x.ReviewerId, x.CreatedAt }); e.Property(x => x.Version).IsConcurrencyToken(); e.HasOne<StoredDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade); e.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.ReviewerId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<DocumentAudit>(e => { e.HasKey(x => x.Id); e.HasIndex(x => new { x.DocumentId, x.CreatedAt }); e.HasOne<StoredDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<RetentionPolicy>(e => { e.HasKey(x => x.OwnerId); e.Property(x => x.Version).IsConcurrencyToken(); e.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<TrashedDocument>(e => { e.HasKey(x => x.DocumentId); e.HasOne<StoredDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<DocumentRevision>(e => { e.HasKey(x => new { x.DocumentId, x.Version }); e.HasOne<StoredDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<OperationReceipt>(e => { e.HasIndex(x => new { x.OwnerId, x.Scope, x.Key }).IsUnique(); e.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<AutomationToken>(e => { e.HasIndex(x => x.Hash).IsUnique(); e.Property(x => x.Version).IsConcurrencyToken(); e.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<DocumentJob>(e => { e.HasIndex(x => new { x.OwnerId, x.CreatedAt }); e.HasIndex(x => new { x.Status, x.CreatedAt }); e.Property(x => x.Version).IsConcurrencyToken(); e.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<WorkspaceItem>(entity =>
        {
            entity.Property(d => d.Version).IsConcurrencyToken();
            entity.HasIndex(d => new { d.OwnerId, d.Kind });
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(d => d.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<LetterArtifact>(entity =>
        {
            entity.HasIndex(d => new { d.LetterId, d.Key }).IsUnique();
            entity.HasOne<WorkspaceItem>().WithMany().HasForeignKey(d => d.LetterId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<UploadPolicy>().Property(p => p.Version).IsConcurrencyToken();
        builder.Entity<StoredDocument>(entity =>
        {
            entity.HasQueryFilter(d => !TrashedDocuments.Any(t => t.DocumentId == d.Id));
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Title).HasMaxLength(160);
            entity.Property(d => d.Version).IsConcurrencyToken();
            entity.HasIndex(d => new { d.OwnerId, d.UpdatedAt });
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(d => d.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class TrashedDocument
{
    public Guid DocumentId { get; set; }
    public long DeletedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
public sealed class RetentionPolicy
{
    public required string OwnerId { get; set; }
    public bool Enabled { get; set; }
    public int KeepLatest { get; set; } = 50;
    public int Days { get; set; } = 30;
    public long Version { get; set; }
}

public sealed class UploadPolicy
{
    public int Id { get; set; } = 1;
    public int MaxUploadMiB { get; set; } = 10;
    public long Version { get; set; } = 1;
}

public sealed class StoredDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerId { get; set; }
    public required string Title { get; set; }
    public byte[] Content { get; set; } = [];
    public long Version { get; set; } = 1;
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

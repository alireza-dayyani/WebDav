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

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<StoredDocument>().Where(e => e.State is EntityState.Added or EntityState.Modified).ToArray())
        {
            var document = entry.Entity;
            if (entry.State == EntityState.Modified)
            {
                var previous = entry.OriginalValues;
                var oldVersion = previous.GetValue<long>(nameof(StoredDocument.Version));
                if (!await DocumentRevisions.AnyAsync(r => r.DocumentId == document.Id && r.Version == oldVersion, cancellationToken))
                    DocumentRevisions.Add(new DocumentRevision { DocumentId = document.Id, Version = oldVersion, Title = previous.GetValue<string>(nameof(StoredDocument.Title)), Content = previous.GetValue<byte[]>(nameof(StoredDocument.Content)).ToArray(), CreatedAt = previous.GetValue<long>(nameof(StoredDocument.UpdatedAt)) });
            }
            if (!await DocumentRevisions.AnyAsync(r => r.DocumentId == document.Id && r.Version == document.Version, cancellationToken))
                DocumentRevisions.Add(new DocumentRevision { DocumentId = document.Id, Version = document.Version, Title = document.Title, Content = document.Content.ToArray(), CreatedAt = document.UpdatedAt });
        }
        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
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
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Title).HasMaxLength(160);
            entity.Property(d => d.Version).IsConcurrencyToken();
            entity.HasIndex(d => new { d.OwnerId, d.UpdatedAt });
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(d => d.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
    }
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

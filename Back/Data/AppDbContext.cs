using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<StoredDocument> Documents => Set<StoredDocument>();
    public DbSet<UploadPolicy> UploadPolicies => Set<UploadPolicy>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
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

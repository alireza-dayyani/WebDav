using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

// Existing databases are baselined in place. Each subsequent numbered upgrade is
// transactional and recorded with its schema change; never recreate customer data.
public static class SchemaUpgrades
{
    public static async Task InitializeAsync(AppDbContext db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS SchemaVersions (Version INTEGER NOT NULL PRIMARY KEY, AppliedAt TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("INSERT OR IGNORE INTO SchemaVersions VALUES (1, datetime('now'))");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS TrashedDocuments (DocumentId TEXT NOT NULL PRIMARY KEY, DeletedAt INTEGER NOT NULL, FOREIGN KEY (DocumentId) REFERENCES Documents(Id) ON DELETE CASCADE)");
        await db.Database.ExecuteSqlRawAsync("INSERT OR IGNORE INTO SchemaVersions VALUES (2, datetime('now'))");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS RetentionPolicies (OwnerId TEXT NOT NULL PRIMARY KEY, Enabled INTEGER NOT NULL, KeepLatest INTEGER NOT NULL, Days INTEGER NOT NULL, Version INTEGER NOT NULL, FOREIGN KEY (OwnerId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE)");
        await db.Database.ExecuteSqlRawAsync("INSERT OR IGNORE INTO SchemaVersions VALUES (3, datetime('now'))");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS DocumentGrants (DocumentId TEXT NOT NULL, UserId TEXT NOT NULL, Permission TEXT NOT NULL, Version INTEGER NOT NULL, PRIMARY KEY(DocumentId,UserId), FOREIGN KEY(DocumentId) REFERENCES Documents(Id) ON DELETE CASCADE, FOREIGN KEY(UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS DocumentReviews (Id TEXT NOT NULL PRIMARY KEY, DocumentId TEXT NOT NULL, DocumentVersion INTEGER NOT NULL, ReviewerId TEXT NOT NULL, Status TEXT NOT NULL, Note TEXT NOT NULL, ContentHash TEXT NOT NULL, Version INTEGER NOT NULL, CreatedAt INTEGER NOT NULL, UpdatedAt INTEGER NOT NULL, FOREIGN KEY(DocumentId) REFERENCES Documents(Id) ON DELETE CASCADE, FOREIGN KEY(ReviewerId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE);
            CREATE INDEX IF NOT EXISTS IX_DocumentReviews_ReviewerId_CreatedAt ON DocumentReviews(ReviewerId,CreatedAt);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_DocumentReviews_Pending ON DocumentReviews(DocumentId,DocumentVersion,ReviewerId) WHERE Status = 'pending';
            CREATE TABLE IF NOT EXISTS DocumentAudits (Id TEXT NOT NULL PRIMARY KEY, DocumentId TEXT NOT NULL, ActorId TEXT NOT NULL, Action TEXT NOT NULL, DocumentVersion INTEGER NOT NULL, Detail TEXT NOT NULL, CreatedAt INTEGER NOT NULL, FOREIGN KEY(DocumentId) REFERENCES Documents(Id) ON DELETE CASCADE);
            CREATE INDEX IF NOT EXISTS IX_DocumentAudits_DocumentId_CreatedAt ON DocumentAudits(DocumentId,CreatedAt);
            INSERT OR IGNORE INTO SchemaVersions VALUES (4, datetime('now'));
            """);
        await transaction.CommitAsync();
        await LibrarySearch.BackfillAsync(db);
    }
}

using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Data;

public sealed class DocumentRevision
{
    public Guid DocumentId { get; set; }
    public long Version { get; set; }
    public required string Title { get; set; }
    public byte[] Content { get; set; } = [];
    public long CreatedAt { get; set; }
}

public sealed class OperationReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerId { get; set; }
    public required string Scope { get; set; }
    public required string Key { get; set; }
    public required string Hash { get; set; }
    public required string Response { get; set; }
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public sealed class AutomationToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerId { get; set; }
    public required string Name { get; set; }
    public required string Hash { get; set; }
    public required string Scopes { get; set; }
    public long ExpiresAt { get; set; }
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long Version { get; set; } = 1;
}

public sealed class DocumentJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerId { get; set; }
    public required string Kind { get; set; }
    public string Status { get; set; } = "queued";
    public string Work { get; set; } = "{}";
    public string Report { get; set; } = "{}";
    public byte[] Result { get; set; } = [];
    public string Format { get; set; } = "json";
    public string Error { get; set; } = "";
    public long Version { get; set; } = 1;
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public static class AutomationSchema
{
    public static Task InitializeAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS DocumentRevisions (
          DocumentId TEXT NOT NULL, Version INTEGER NOT NULL, Title TEXT NOT NULL, Content BLOB NOT NULL, CreatedAt INTEGER NOT NULL,
          PRIMARY KEY (DocumentId,Version), FOREIGN KEY (DocumentId) REFERENCES Documents(Id) ON DELETE CASCADE);
        CREATE TABLE IF NOT EXISTS OperationReceipts (
          Id TEXT NOT NULL PRIMARY KEY, OwnerId TEXT NOT NULL, Scope TEXT NOT NULL, Key TEXT NOT NULL, Hash TEXT NOT NULL, Response TEXT NOT NULL, CreatedAt INTEGER NOT NULL,
          FOREIGN KEY (OwnerId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_OperationReceipts_OwnerId_Scope_Key ON OperationReceipts(OwnerId,Scope,Key);
        CREATE TABLE IF NOT EXISTS AutomationTokens (
          Id TEXT NOT NULL PRIMARY KEY, OwnerId TEXT NOT NULL, Name TEXT NOT NULL, Hash TEXT NOT NULL, Scopes TEXT NOT NULL,
          ExpiresAt INTEGER NOT NULL, CreatedAt INTEGER NOT NULL, Version INTEGER NOT NULL,
          FOREIGN KEY (OwnerId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_AutomationTokens_Hash ON AutomationTokens(Hash);
        CREATE TABLE IF NOT EXISTS DocumentJobs (
          Id TEXT NOT NULL PRIMARY KEY, OwnerId TEXT NOT NULL, Kind TEXT NOT NULL, Status TEXT NOT NULL, Work TEXT NOT NULL,
          Report TEXT NOT NULL, Result BLOB NOT NULL, Format TEXT NOT NULL, Error TEXT NOT NULL, Version INTEGER NOT NULL,
          CreatedAt INTEGER NOT NULL, UpdatedAt INTEGER NOT NULL,
          FOREIGN KEY (OwnerId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE);
        CREATE INDEX IF NOT EXISTS IX_DocumentJobs_OwnerId_CreatedAt ON DocumentJobs(OwnerId,CreatedAt);
        CREATE INDEX IF NOT EXISTS IX_DocumentJobs_Status_CreatedAt ON DocumentJobs(Status,CreatedAt);
        """);
}

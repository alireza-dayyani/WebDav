using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Data;

// Private reusable resources and letter drafts. Letters stay outside the ordinary document library.
public sealed class WorkspaceItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerId { get; set; }
    public required string Kind { get; set; }
    public required string Title { get; set; }
    public string Data { get; set; } = "{}";
    public string Snapshot { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public long Version { get; set; } = 1;
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public sealed class LetterArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LetterId { get; set; }
    public required string Key { get; set; }
    public required string Title { get; set; }
    public byte[] Model { get; set; } = [];
    public byte[] Docx { get; set; } = [];
    public byte[] Pdf { get; set; } = [];
}

public static class WorkspaceSchema
{
    public static async Task InitializeAsync(AppDbContext db)
    {
        // Additive upgrade for existing installations; never reset their document or Identity tables.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS WorkspaceItems (
              Id TEXT NOT NULL PRIMARY KEY, OwnerId TEXT NOT NULL, Kind TEXT NOT NULL, Title TEXT NOT NULL,
              Data TEXT NOT NULL, Snapshot TEXT NOT NULL, Content BLOB NOT NULL, Version INTEGER NOT NULL,
              CreatedAt INTEGER NOT NULL, UpdatedAt INTEGER NOT NULL,
              FOREIGN KEY (OwnerId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE);
            CREATE INDEX IF NOT EXISTS IX_WorkspaceItems_OwnerId_Kind ON WorkspaceItems(OwnerId, Kind);
            CREATE TABLE IF NOT EXISTS LetterArtifacts (
              Id TEXT NOT NULL PRIMARY KEY, LetterId TEXT NOT NULL, Key TEXT NOT NULL, Title TEXT NOT NULL,
              Model BLOB NOT NULL, Docx BLOB NOT NULL, Pdf BLOB NOT NULL,
              FOREIGN KEY (LetterId) REFERENCES WorkspaceItems(Id) ON DELETE CASCADE);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_LetterArtifacts_LetterId_Key ON LetterArtifacts(LetterId, Key);
            """);
    }
}

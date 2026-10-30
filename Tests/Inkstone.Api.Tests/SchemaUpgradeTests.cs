using Inkstone.Web.Data;
using Inkstone.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Api.Tests;

public sealed class SchemaUpgradeTests
{
    [Fact]
    public async Task Legacy_baseline_is_repeatable_and_preserves_document_and_identity_data()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new Microsoft.AspNetCore.Identity.IdentityUser { Id = "legacy", UserName = "legacy" });
        var document = new StoredDocument { OwnerId = "legacy", Title = "پیش‌نویس", Content = "{\"body\":[]}"u8.ToArray() };
        db.Documents.Add(document); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE TrashedDocuments");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE RetentionPolicies; DROP TABLE DocumentSearch");
        await SchemaUpgrades.InitializeAsync(db); await SchemaUpgrades.InitializeAsync(db);
        db.ChangeTracker.Clear();
        Assert.Equal("پیش‌نویس", (await db.Documents.SingleAsync()).Title);
        Assert.Single(await db.DocumentRevisions.ToListAsync());
        Assert.Single(await db.Users.ToListAsync());
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SchemaVersions WHERE Version = 1";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT COUNT(*) FROM DocumentSearch";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }
}

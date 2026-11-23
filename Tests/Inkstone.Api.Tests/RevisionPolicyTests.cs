using System.Text;
using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Api.Tests;

public sealed class RevisionPolicyTests
{
    [Fact]
    public async Task Identical_content_bumps_do_not_add_redundant_revisions()
    {
        await using var host = await EndpointHost.CreateAsync();
        var bytes = "{\"body\":[{\"text\":\"same\"}]}"u8.ToArray();
        await host.WithDatabase(async db =>
        {
            var doc = await db.Documents.SingleAsync();
            doc.Content = bytes;
            doc.Version = 2;
            await db.SaveChangesAsync();
            var baseline = await db.DocumentRevisions.CountAsync(r => r.DocumentId == doc.Id);
            doc.Version = 3;
            await db.SaveChangesAsync();
            doc.Version = 4;
            await db.SaveChangesAsync();
            Assert.Equal(baseline, await db.DocumentRevisions.CountAsync(r => r.DocumentId == doc.Id));
        });
    }

    [Fact]
    public async Task Title_or_content_changes_still_record_revision_history()
    {
        await using var host = await EndpointHost.CreateAsync();
        await host.WithDatabase(async db =>
        {
            var doc = await db.Documents.SingleAsync();
            doc.Content = Encoding.UTF8.GetBytes("{\"body\":[{\"text\":\"first\"}]}");
            doc.Version = 2;
            await db.SaveChangesAsync();
            doc.Title = "Renamed";
            doc.Version = 3;
            await db.SaveChangesAsync();
        });
        await host.WithDatabase(async db =>
            Assert.Equal(new[] { 1L, 2L, 3L }, await db.DocumentRevisions.Where(r => r.DocumentId == host.DocumentId).OrderBy(r => r.Version).Select(r => r.Version).ToArrayAsync()));
    }
}
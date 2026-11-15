using Inkstone.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Api.Tests;

public sealed class LetterDraftRecipientTests
{
    [Fact]
    public async Task WithResolvedEmails_persists_display_name_and_account_email()
    {
        await using var host = await EndpointHost.CreateAsync();
        await host.WithDatabase(async db =>
        {
            foreach (var user in db.Users)
            {
                user.NormalizedEmail = user.Email!.ToUpperInvariant();
                user.NormalizedUserName = user.UserName!.ToUpperInvariant();
            }
            await db.SaveChangesAsync();

            var draft = new LetterDraft(
                "Recipient round-trip",
                "Subject",
                "2026-11-15",
                null,
                null,
                null,
                null,
                null,
                "Body",
                [new LetterRecipient(EndpointHost.OtherOwner, DisplayName: "Dr. Ada Lovelace")],
                []);
            var hydrated = await LetterComposer.WithResolvedEmails(draft, db, CancellationToken.None);
            var recipient = hydrated.Recipients.Single();
            Assert.Equal("Dr. Ada Lovelace", recipient.DisplayName);
            Assert.Equal("other@example.test", recipient.ResolvedEmail);
        });
    }
}
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace Inkstone.Web.Services;

public sealed record LetterPlan(LetterDraft Draft, WorkspaceItem? Template, WorkspaceItem? Sender, WorkspaceItem? Signature, WorkspaceItem? Logo,
    StoredDocument? Body, StoredDocument[] Attachments, RecipientSnapshot[] Recipients, ValidationIssue[] Issues);

public sealed class LetterComposer(AppDbContext db, DocumentConverter converter)
{
    public static bool ValidShape(LetterDraft? draft) => draft is not null && Studio.Title(draft.Title) is not null
        && draft.Subject is { Length: <= 200 } && draft.BodyText is { Length: <= 512 * 1024 }
        && Encoding.UTF8.GetByteCount(draft.BodyText) <= 480 * 1024 && DocumentConverter.Languages.Contains(draft.Language)
        && !draft.Subject.Any(c => c == '\uFFFC' || char.IsControl(c))
        && draft.Date is { Length: <= 20 } && draft.Number is { Length: <= 80 } && !draft.Number.Any(c => c == '\uFFFC' || char.IsControl(c))
        && draft.Recipients is { Length: <= 20 } && draft.Recipients.All(x => x is not null && !string.IsNullOrWhiteSpace(x.UserId) && x.UserId.Length <= 128 && x.Kind is "to" or "cc")
        && draft.Recipients.Select(x => x.UserId).Distinct(StringComparer.Ordinal).Count() == draft.Recipients.Length
        && draft.Recipients.Count(x => x.Kind == "cc") <= 10 && draft.Attachments is { Length: <= 5 }
        && draft.Attachments.All(x => x is not null && x.Version > 0) && draft.Attachments.Select(x => x.Id).Distinct().Count() == draft.Attachments.Length
        && draft.Priority is "normal" or "urgent" or "immediate" && draft.Classification is "normal" or "confidential" or "strict";

    public async Task<LetterPlan> Prepare(WorkspaceItem letter, CancellationToken ct, bool forEditor = false)
    {
        var draft = Studio.Read<LetterDraft>(letter);
        var issues = new List<ValidationIssue>();
        void Issue(string code, string field, string message) => issues.Add(new(code, field, message));
        async Task<WorkspaceItem?> Resource(ResourceRef? reference, string kind, string field, bool required)
        {
            if (reference is null) { if (required) Issue("required", field, $"Choose {field}."); return null; }
            var found = await Studio.Find(db, letter.OwnerId, kind, reference.Id, ct, false);
            if (found is null) Issue("resource_missing", field, $"The selected {field} is unavailable.");
            else if (found.Version != reference.Version) Issue("resource_changed", field, $"The selected {field} changed. Select its current revision.");
            return found;
        }
        async Task<StoredDocument?> Document(ResourceRef? reference, string field)
        {
            if (reference is null) return null;
            var found = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == reference.Id && x.OwnerId == letter.OwnerId, ct);
            if (found is null) Issue("resource_missing", field, $"The selected {field} is unavailable.");
            else if (found.Version != reference.Version) Issue("resource_changed", field, $"The selected {field} changed. Select its current revision.");
            return found;
        }
        async Task Health(byte[] model, string field)
        {
            try
            {
                var h = await converter.PreflightAsync(model, ct);
                if (h.GetProperty("trackChanges").GetBoolean() || h.GetProperty("protected").GetBoolean()
                    || h.GetProperty("pendingRevisions").GetBoolean() || h.GetProperty("comments").GetInt32() > 0)
                    Issue("review", field, "Remove comments, resolve revisions, stop protection and disable Track Changes before generation.");
            }
            catch (ConversionException e) when (e.Status == 422) { Issue("invalid_model", field, "The selected document cannot be processed."); }
        }
        var edited = letter.Content.Length > 0;
        if (!forEditor && string.IsNullOrWhiteSpace(draft.Subject)) Issue("required", "subject", "A letter subject is required.");
        if (!forEditor && (!DateOnly.TryParseExact(draft.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var chosenDate)
            || (draft.Language == "fa-IR" && chosenDate.ToDateTime(TimeOnly.MinValue) < new PersianCalendar().MinSupportedDateTime)))
            Issue("date", "date", "Choose a valid ISO date supported by the document's calendar.");
        if (!forEditor && !draft.Recipients.Any(x => x.Kind == "to")) Issue("required", "recipients", "Choose at least one main recipient.");
        if (!edited && draft.BodyDocument is not null && !string.IsNullOrWhiteSpace(draft.BodyText)) Issue("body_conflict", "body", "Use a source document or plain text, not both.");
        if (!forEditor && !edited && draft.BodyDocument is null && string.IsNullOrWhiteSpace(draft.BodyText)) Issue("required", "body", "Letter body text or a source document is required.");
        if (draft.BodyText.Any(c => c == '\uFFFC' || (char.IsControl(c) && c is not '\r' and not '\n' and not '\t'))) Issue("invalid_text", "body", "Body contains unsupported control characters.");
        var template = await Resource(draft.Template, "template", "template", true);
        var sender = await Resource(draft.Sender, "sender", "sender", !forEditor);
        var signature = await Resource(draft.Signature, "image", "signature", !forEditor);
        var logo = await Resource(draft.Logo, "image", "logo", false);
        var body = edited ? null : await Document(draft.BodyDocument, "body");
        var attachments = new List<StoredDocument>();
        foreach (var reference in draft.Attachments)
        {
            var item = await Document(reference, "attachments");
            if (item is not null) attachments.Add(item);
        }
        var ids = draft.Recipients.Select(x => x.UserId).ToArray();
        var users = await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Email }).ToListAsync(ct);
        var recipients = draft.Recipients.Where(x => users.Any(u => u.Id == x.UserId)).Select(x => {
            var user = users.Single(u => u.Id == x.UserId);
            return new RecipientSnapshot(user.Id, user.Email!.Split('@')[0], user.Email!, x.Kind, x.PrintOnLetter);
        }).ToArray();
        if (recipients.Length != draft.Recipients.Length) Issue("recipient", "recipients", "One or more registered recipients no longer exist.");
        if (recipients.Any(x => x.Email.Contains('\uFFFC')) || attachments.Any(x => x.Title.Contains('\uFFFC')))
            Issue("invalid_text", "recipients", "A recipient or attachment name contains an unsupported object marker.");
        if (template is not null)
        {
            var config = Studio.Read<TemplateConfig>(template);
            if (config.Kind != "letter") Issue("template_kind", "template", "Choose a letter template.");
            else
            {
                var fields = await converter.ReadBookmarksAsync(edited ? letter.Content : template.Content, ct);
                var required = Studio.Required.Concat(config.RequiredFields).Distinct().ToArray();
                foreach (var field in required)
                    if (!config.Fields.ContainsKey(field)) Issue("bookmark", field, $"Map the required {field} bookmark in this template.");
                foreach (var field in config.Fields)
                    if (!fields.Any(x => x.Name == field.Value && (x.Replaceable || (edited && field.Key == "Body")))) Issue("bookmark", field.Key, $"Bookmark {field.Value} is missing or has an unsupported range.");
                if (edited && config.Fields.TryGetValue("Body", out var bodyBookmark))
                {
                    var state = await converter.BookmarkStateAsync(letter.Content, bodyBookmark, ct);
                    if (!state.GetProperty("valid").GetBoolean()) Issue("bookmark", "Body", "Restore the body bookmark and keep automatic fields outside it.");
                    else if (!forEditor && !state.GetProperty("hasContent").GetBoolean()) Issue("required", "body", "Write the letter inside its body bookmark before generation.");
                }
                if (draft.Logo is not null && !config.Fields.ContainsKey("Logo")) Issue("bookmark", "Logo", "Map a logo bookmark to use this image.");
                if (draft.IncludeQrCode && !config.Fields.ContainsKey("QRCode")) Issue("bookmark", "QRCode", "Map a QRCode bookmark or disable the QR code.");
                if (draft.CopyVariants && draft.Recipients.Any(x => x.Kind == "cc") && !config.Fields.ContainsKey("Copies")) Issue("bookmark", "Copies", "Map the copies bookmark to generate recipient variants.");
            }
            await Health(edited ? letter.Content : template.Content, "template");
        }
        if (body is not null)
        {
            if (body.Content.Length == 0) Issue("required", "body", "The body document is empty.");
            else await Health(body.Content, "body");
        }
        foreach (var attachment in attachments) await Health(attachment.Content, "attachments");
        var modelBytes = (long)(edited ? letter.Content.Length : template?.Content.Length ?? 0) + (body?.Content.Length ?? 0) + attachments.Sum(x => (long)x.Content.Length);
        if (modelBytes > 64 * FileLimits.MiB) Issue("generation_size", "body", "Template, body and attachment models together must fit within 64 MiB.");
        return new(draft, template, sender, signature, logo, body, attachments.ToArray(), recipients, issues.ToArray());
    }

    public string Number(WorkspaceItem letter, LetterDraft draft) => string.IsNullOrWhiteSpace(draft.Number) ? "L-" + letter.Id.ToString("N")[..12].ToUpperInvariant() : draft.Number.Trim();

    public async Task<byte[]> Compose(WorkspaceItem letter, LetterPlan plan, IEnumerable<RecipientSnapshot> copies, CancellationToken ct)
    {
        var draft = plan.Draft;
        var config = Studio.Read<TemplateConfig>(plan.Template!);
        var sender = plan.Sender is null ? new SenderProfile("", "", "", "") : Studio.Read<SenderProfile>(plan.Sender);
        var validDate = DateOnly.TryParseExact(draft.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);
        var persian = draft.Language == "fa-IR";
        var displayDate = validDate ? date.ToString(draft.Language == "en-US" ? "MMMM d, yyyy" : "yyyy/MM/dd", CultureInfo.GetCultureInfo("en-US")) : "";
        if (persian && validDate)
        {
            var pc = new PersianCalendar(); var dt = date.ToDateTime(TimeOnly.MinValue);
            displayDate = dt < pc.MinSupportedDateTime ? "" : $"{pc.GetYear(dt):0000}/{pc.GetMonth(dt):00}/{pc.GetDayOfMonth(dt):00}";
        }
        string Latin(string text) => draft.Language is "fa-IR" or "ar-SA" or "ur-PK" ? "\u2066" + text + "\u2069" : text;
        string Names(IEnumerable<RecipientSnapshot> list) => string.Join("\n", list.Select(x => Latin(x.Email)));
        var copyNames = Names(copies);
        var logical = new Dictionary<string, string> {
            ["Organization"] = sender.Organization, ["Date"] = Latin(displayDate), ["Number"] = Latin(Number(letter, draft)),
            ["Classification"] = draft.Classification switch { "confidential" => persian ? "محرمانه" : "Confidential", "strict" => persian ? "خیلی محرمانه" : "Strictly confidential", _ => "" },
            ["Priority"] = draft.Priority switch { "urgent" => persian ? "فوری" : "Urgent", "immediate" => persian ? "آنی" : "Immediate", _ => "" },
            ["Subject"] = draft.Subject, ["Recipients"] = Names(plan.Recipients.Where(x => x.Kind == "to")),
            ["SenderName"] = sender.FullName, ["SenderTitle"] = sender.Title, ["Copies"] = copyNames.Length == 0 ? "" : (persian ? "رونوشت:\n" : "Copies:\n") + copyNames,
            ["Attachments"] = string.Join("\n", plan.Attachments.Select(x => x.Title)), ["Address"] = sender.Address,
            ["Tracking"] = Latin(letter.Id.ToString()), ["Logo"] = "", ["Signature"] = "", ["QRCode"] = ""
        };
        if (plan.Body is null && letter.Content.Length == 0) logical["Body"] = draft.BodyText;
        var values = logical.Where(x => config.Fields.ContainsKey(x.Key)).ToDictionary(x => config.Fields[x.Key], x => x.Value, StringComparer.Ordinal);
        var images = new Dictionary<string, TemplateImage>();
        if (plan.Signature is not null) images[config.Fields["Signature"]] = new(Convert.ToBase64String(plan.Signature.Content), 120, "Signature");
        if (plan.Logo is not null) images[config.Fields["Logo"]] = new(Convert.ToBase64String(plan.Logo.Content), 90, "Organization logo");
        if (draft.IncludeQrCode)
        {
            using var data = QRCodeGenerator.GenerateQrCode($"Inkstone|{letter.Id}|{Number(letter, draft)}|{draft.Date}", QRCodeGenerator.ECCLevel.Q);
            using var qr = new PngByteQRCode(data);
            images[config.Fields["QRCode"]] = new(Convert.ToBase64String(qr.GetGraphic(4)), 54, "Letter tracking QR code");
        }
        return await converter.ComposeAsync(letter.Content.Length > 0 ? letter.Content : plan.Template!.Content, values, images, plan.Body?.Content, config.Fields.GetValueOrDefault("Body"), ct);
    }

    public static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));
}

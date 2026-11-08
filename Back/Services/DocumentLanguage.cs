using System.Text.Json;

namespace Inkstone.Web.Services;

/// <summary>Reads a document language from file text and language tags. Callers do not choose it.</summary>
public static class DocumentLanguage
{
    public const string English = "en-US";
    public const string Persian = "fa-IR";
    public const string Arabic = "ar-SA";
    public const string Urdu = "ur-PK";

    public static string FromText(string? text) => Count(text).Language() ?? English;

    public static string FromModel(byte[]? content)
    {
        if (content is not { Length: > 0 }) return English;
        try
        {
            using var json = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 256 });
            var tags = new Dictionary<string, int>(StringComparer.Ordinal);
            var counts = default(ScriptCounts);
            Walk(json.RootElement, tags, ref counts);
            var script = counts.Language();
            tags.TryGetValue(English, out var english);
            var rtl = tags.Where(pair => pair.Key != English).OrderByDescending(pair => pair.Value).FirstOrDefault();
            if (rtl.Key is not null && (script is not null || rtl.Value >= english)) return rtl.Key;
            if (english > 0) return script ?? English;
            return script ?? English;
        }
        catch (JsonException) { return English; }
    }

    public static string ForLetter(byte[]? editor, byte[]? body, byte[]? template, string? bodyText, string? subject, string? title)
    {
        if (editor is { Length: > 0 }) return FromModel(editor);
        if (body is { Length: > 0 }) return FromModel(body);
        if (!string.IsNullOrWhiteSpace(bodyText)) return FromText(bodyText);
        if (template is { Length: > 0 }) return FromModel(template);
        return FromText($"{subject}\n{title}");
    }

    private static void Walk(JsonElement element, Dictionary<string, int> tags, ref ScriptCounts counts)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name is "lang" or "langBidi" && property.Value.ValueKind == JsonValueKind.String)
                    {
                        var tag = Normalize(property.Value.GetString());
                        if (tag is not null) tags[tag] = tags.GetValueOrDefault(tag) + 1;
                    }
                    else if (property.Name == "text" && property.Value.ValueKind == JsonValueKind.String)
                        counts = counts.Add(Count(property.Value.GetString()));
                    else Walk(property.Value, tags, ref counts);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Walk(item, tags, ref counts);
                break;
        }
    }

    private readonly record struct ScriptCounts(int Persian, int Urdu, int PersianOrUrdu, int ArabicSpecific, int Arabic)
    {
        public ScriptCounts Add(ScriptCounts other) => new(Persian + other.Persian, Urdu + other.Urdu, PersianOrUrdu + other.PersianOrUrdu, ArabicSpecific + other.ArabicSpecific, Arabic + other.Arabic);

        public string? Language()
        {
            if (Urdu > 0 && Urdu >= Persian) return DocumentLanguage.Urdu;
            if (Persian > 0 || PersianOrUrdu > 0) return DocumentLanguage.Persian;
            if (ArabicSpecific > 0) return DocumentLanguage.Arabic;
            return Arabic > 0 ? DocumentLanguage.Persian : null;
        }
    }

    private static ScriptCounts Count(string? text)
    {
        if (string.IsNullOrEmpty(text)) return default;
        var persian = 0;
        var urdu = 0;
        var persianOrUrdu = 0;
        var arabicSpecific = 0;
        var arabic = 0;
        foreach (var c in text)
        {
            if (c is 'پ' or 'چ' or 'ژ' or 'گ') persian++;
            else if (c is 'ٹ' or 'ڈ' or 'ڑ' or 'ں' or 'ے' or 'ھ' or 'ہ') urdu++;
            else if (c is 'ک' or 'ی') persianOrUrdu++;
            else if (c is 'ك' or 'ي' or 'ة' or 'ى') arabicSpecific++;
            else if (IsArabicScript(c)) arabic++;
        }
        return new(persian, urdu, persianOrUrdu, arabicSpecific, arabic);
    }

    private static string? Normalize(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var primary = tag.Split('-', '_')[0].Trim().ToLowerInvariant();
        return primary switch
        {
            "fa" or "pes" or "prs" => Persian,
            "ar" => Arabic,
            "ur" => Urdu,
            "en" => English,
            _ => null
        };
    }

    private static bool IsArabicScript(char c) => c is >= '\u0600' and <= '\u06FF' or >= '\u0750' and <= '\u077F' or >= '\u08A0' and <= '\u08FF' or >= '\uFB50' and <= '\uFDFF' or >= '\uFE70' and <= '\uFEFF';
}

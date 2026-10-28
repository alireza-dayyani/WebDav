using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Inkstone.Web.Services;

/// <summary>Checks operation syntax and bounded values; the engine checks document-specific ranges and locks.</summary>
public static class OperationValidation
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Dictionary<string, string[]> Fields = new(StringComparer.Ordinal)
    {
        ["replaceText"] = ["find", "replacement", "scope", "matchCase", "wholeWord", "expectedMatches"],
        ["insertText"] = ["target", "text", "paragraphs"], ["replaceRange"] = ["target", "text", "paragraphs"],
        ["insertParagraph"] = ["target", "after", "text", "paragraphs", "style"], ["format"] = ["target", "values"],
        ["fillBookmarks"] = ["values"], ["updateTable"] = ["ref", "cells", "appendRows"], ["fillControls"] = ["values"],
        ["insertImage"] = ["target", "data", "width", "height", "alt"], ["replaceImage"] = ["ref", "data", "width", "height", "alt"],
        ["properties"] = ["values"], ["normalize"] = ["rules"], ["addComment"] = ["target", "text"],
        ["replyComment"] = ["id", "text"], ["resolveComment"] = ["id", "resolved"], ["deleteComment"] = ["id"],
        ["trackChanges"] = ["enabled"], ["resolveChanges"] = ["decision", "all", "target", "stopTracking"]
    };

    public static void Validate(JsonElement operation, string field = "operation")
    {
        RequestValidation.Object(operation, field);
        var type = String(operation, "type", field, 400);
        if (!Fields.TryGetValue(type, out var fields)) RequestValidation.Fail(field + ".type", "Unknown operation type.");
        Only(operation, fields!.Append("type"), field);
        switch (type)
        {
            case "replaceText":
                Text(String(operation, "find", field), field + ".find", false); Text(String(operation, "replacement", field), field + ".replacement");
                OptionalChoice(operation, "scope", field, "body", "all"); Flag(operation, "matchCase", field); Flag(operation, "wholeWord", field); OptionalInteger(operation, "expectedMatches", field); break;
            case "insertText": case "replaceRange": case "insertParagraph":
                Target(type == "insertParagraph" && operation.TryGetProperty("after", out var after) ? after : Get(operation, "target"), field + ".target");
                Text(String(operation, "text", field), field + ".text"); Flag(operation, "paragraphs", field); OptionalText(operation, "style", field, 256); break;
            case "format": Target(Get(operation, "target"), field + ".target"); Format(Get(operation, "values"), field + ".values"); break;
            case "fillBookmarks": BookmarkValues(Get(operation, "values"), field + ".values"); break;
            case "fillControls": Controls(Get(operation, "values"), field + ".values", 0); break;
            case "updateTable": Table(operation, field); break;
            case "insertImage": Target(Get(operation, "target"), field + ".target"); Image(operation, field); break;
            case "replaceImage": Reference(String(operation, "ref", field), true, field + ".ref"); Image(operation, field); break;
            case "properties": Properties(Get(operation, "values"), field + ".values"); break;
            case "normalize": Rules(operation, field); break;
            case "addComment": Target(Get(operation, "target"), field + ".target"); Text(String(operation, "text", field), field + ".text", false); break;
            case "replyComment": Integer(Get(operation, "id"), field + ".id", uint.MaxValue); Text(String(operation, "text", field), field + ".text", false); break;
            case "resolveComment": Integer(Get(operation, "id"), field + ".id", uint.MaxValue); Flag(operation, "resolved", field); break;
            case "deleteComment": Integer(Get(operation, "id"), field + ".id", uint.MaxValue); break;
            case "trackChanges": Boolean(Get(operation, "enabled"), field + ".enabled"); break;
            case "resolveChanges":
                RequiredChoice(operation, "decision", field, "accept", "reject"); Flag(operation, "all", field); Flag(operation, "stopTracking", field);
                if (Get(operation, "all").ValueKind != JsonValueKind.True) Target(Get(operation, "target"), field + ".target");
                break;
        }
    }

    public static void Text(string? text, string field, bool allowEmpty = true, int status = 422)
    {
        if (text is null || (!allowEmpty && text.Length == 0)) RequestValidation.Fail(field, "Text is required.", status);
        if (text!.Any(c => c == '\uFFFC' || (char.IsControl(c) && c is not ('\n' or '\r' or '\t')))) RequestValidation.Fail(field, "Text contains unsupported control characters.", status);
        try { if (Utf8.GetByteCount(text) > 512 * 1024) RequestValidation.Fail(field, "Text exceeds 512 KiB of UTF-8 data.", 413); }
        catch (EncoderFallbackException) { RequestValidation.Fail(field, "Text must contain valid Unicode.", status); }
    }

    public static void Target(JsonElement target, string field, int status = 422)
    {
        RequestValidation.Object(target, field, status);
        Only(target, ["bookmark", "ref", "start", "end", "position", "range", "story", "path"], field, status);
        var selectors = new[] { "bookmark", "ref", "position", "range", "path" }.Where(name => target.TryGetProperty(name, out _)).ToArray();
        if (selectors.Length != 1) RequestValidation.Fail(field, "Supply exactly one target selector.", status);
        var selector = selectors[0];
        if (selector != "ref" && (target.TryGetProperty("start", out _) || target.TryGetProperty("end", out _))) RequestValidation.Fail(field, "Start/end offsets require a paragraph reference.", status);
        if (selector != "path" && target.TryGetProperty("story", out _)) RequestValidation.Fail(field, "Story requires a path selector.", status);
        switch (selector)
        {
            case "bookmark": ShortText(Get(target, selector), field + ".bookmark", 256, status); break;
            case "ref":
                Reference(String(target, "ref", field, status), false, field + ".ref", status);
                OptionalInteger(target, "start", field, status: status); OptionalInteger(target, "end", field, status: status);
                if (Get(target, "start").TryGetUInt64Safe(out var start) && Get(target, "end").TryGetUInt64Safe(out var end) && start > end) RequestValidation.Fail(field, "Range starts after its end.", status);
                break;
            case "path": Path(Get(target, "path"), field + ".path", status); if (target.TryGetProperty("story", out var story)) Story(story, field + ".story", status); break;
            case "position": Position(Get(target, selector), field + ".position", status); break;
            case "range":
                var range = Get(target, selector); RequestValidation.Object(range, field + ".range", status); Only(range, ["start", "end"], field + ".range", status);
                Position(Get(range, "start"), field + ".range.start", status); Position(Get(range, "end"), field + ".range.end", status);
                if (!JsonElement.DeepEquals(Get(Get(range, "start"), "story"), Get(Get(range, "end"), "story"))) RequestValidation.Fail(field, "Ranges must stay in one story.", status);
                break;
        }
    }

    public static void Reference(string reference, bool image, string field, int status = 422)
    {
        if (reference.Length > 512) RequestValidation.Fail(field, "Reference is too long.", status);
        var pieces = reference.Split('@');
        if (pieces.Length != (image ? 2 : 1) || (image && !ulong.TryParse(pieces[1], NumberStyles.None, CultureInfo.InvariantCulture, out _))) RequestValidation.Fail(field, "Invalid object reference.", status);
        var path = pieces[0].Split('.'); var first = path[0];
        if (first != "b" && !(first.StartsWith('p') && uint.TryParse(first.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out _))) RequestValidation.Fail(field, "Invalid story reference.", status);
        if (path.Length < 2 || path.Length > 50 || (path.Length - 2) % 3 != 0 || path.Skip(1).Any(part => !uint.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _))) RequestValidation.Fail(field, "Invalid reference path.", status);
    }

    public static void Image(JsonElement image, string field, int status = 422)
    {
        var data = String(image, "data", field, status);
        if (data.Length > 4 * FileLimits.MiB) RequestValidation.Fail(field + ".data", "Encoded image exceeds the request limit.", 413);
        byte[] bytes;
        try { bytes = Convert.FromBase64String(data); } catch (FormatException) { RequestValidation.Fail(field + ".data", "Image must be base64 encoded.", status); throw; }
        if (bytes.Length == 0) RequestValidation.Fail(field + ".data", "Image must not be empty.", status);
        if (bytes.Length > 512 * 1024) RequestValidation.Fail(field + ".data", "Image exceeds 512 KiB.", 413);
        foreach (var name in new[] { "width", "height" }) if (image.TryGetProperty(name, out var size) && size.ValueKind != JsonValueKind.Null) Number(size, field + "." + name, 4, 4000, status);
        if (image.TryGetProperty("alt", out var alt)) { Text(alt.ValueKind == JsonValueKind.String ? alt.GetString() : null, field + ".alt", status: status); }
    }

    public static void BookmarkValues(JsonElement values, string field, bool allowEmpty = false, int status = 422)
    {
        RequestValidation.Object(values, field, status); var properties = values.EnumerateObject().ToArray();
        if (properties.Length > 256 || (!allowEmpty && properties.Length == 0)) RequestValidation.Fail(field, "Supply 1–256 bookmark values (or an empty object when copying a template).", status);
        long total = 0;
        foreach (var property in properties)
        {
            if (string.IsNullOrWhiteSpace(property.Name) || Encoding.UTF8.GetByteCount(property.Name) > 256 || property.Name.Any(char.IsControl)) RequestValidation.Fail(field, "Bookmark names must be 1–256 UTF-8 bytes without control characters.", status);
            var text = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            Text(text, field + "." + property.Name, status: status); total += Encoding.UTF8.GetByteCount(text!);
        }
        if (total > 512 * 1024) RequestValidation.Fail(field, "Combined bookmark text exceeds 512 KiB.", 413);
    }

    private static void Table(JsonElement operation, string field)
    {
        Reference(String(operation, "ref", field), false, field + ".ref"); var count = 0;
        if (Present(operation, "cells", out var cells))
        {
            Array(cells, field + ".cells", 0, 1024); count += cells.GetArrayLength();
            foreach (var cell in cells.EnumerateArray())
            {
                RequestValidation.Object(cell, field + ".cells", 422); Only(cell, ["row", "column", "text"], field + ".cells");
                Integer(Get(cell, "row"), field + ".row"); Integer(Get(cell, "column"), field + ".column"); Text(String(cell, "text", field), field + ".text");
            }
        }
        if (Present(operation, "appendRows", out var rows))
        {
            Array(rows, field + ".appendRows", 0, 256); count += rows.GetArrayLength();
            foreach (var row in rows.EnumerateArray()) { Array(row, field + ".appendRows", 1, 63); foreach (var cell in row.EnumerateArray()) Text(cell.ValueKind == JsonValueKind.String ? cell.GetString() : null, field + ".appendRows"); }
        }
        if (count == 0) RequestValidation.Fail(field, "Supply cells or appended rows.", 422);
    }

    private static void Format(JsonElement values, string field)
    {
        RequestValidation.Object(values, field, 422); if (!values.EnumerateObject().Any()) RequestValidation.Fail(field, "Supply formatting values.", 422);
        foreach (var property in values.EnumerateObject())
        {
            var key = property.Name; var value = property.Value; var name = field + "." + key;
            switch (key)
            {
                case "bold": case "italic": case "rtl": Boolean(value, name); break;
                case "style": case "font": case "language": ShortText(value, name, 256); break;
                case "size": Number(value, name, 1, 400); break;
                case "color":
                    var color = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                    if (color != "auto" && !(color?.Length == 6 && color.All(char.IsAsciiHexDigit))) RequestValidation.Fail(name, "Use six hexadecimal digits or auto.", 422); break;
                case "align": Choice(value, name, "left", "center", "right", "justify", "distribute", "start", "end"); break;
                case "spacing": case "lineSpacing":
                    RequestValidation.Object(value, name, 422);
                    var items = value.EnumerateObject().ToArray();
                    if (items.Length == 0 || (key == "lineSpacing" && items.Length != 1)) RequestValidation.Fail(name, "Supply supported spacing values.", 422);
                    Only(value, key == "spacing" ? ["before", "after"] : ["value", "atLeast", "exactly"], name);
                    foreach (var item in items) Number(item.Value, name + "." + item.Name, key == "spacing" ? 0 : double.Epsilon, 1584);
                    break;
                default: RequestValidation.Fail(name, "Unsupported formatting property.", 422); break;
            }
        }
    }

    private static void Properties(JsonElement values, string field)
    {
        RequestValidation.Object(values, field, 422); Only(values, ["title", "subject", "author", "keywords", "comments", "category", "custom", "language"], field);
        if (!values.EnumerateObject().Any()) RequestValidation.Fail(field, "Supply document properties.", 422);
        foreach (var property in values.EnumerateObject())
        {
            if (property.Name != "custom") { Text(property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null, field + "." + property.Name); continue; }
            RequestValidation.Object(property.Value, field + ".custom", 422);
            foreach (var custom in property.Value.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(custom.Name) || custom.Name.Length > 255) RequestValidation.Fail(field + ".custom", "Custom property names must be 1–255 characters.", 422);
                if (custom.Value.ValueKind != JsonValueKind.Null) Text(custom.Value.ValueKind == JsonValueKind.String ? custom.Value.GetString() : null, field + ".custom." + custom.Name);
            }
        }
    }

    private static void Controls(JsonElement values, string field, int depth)
    {
        RequestValidation.Object(values, field, 422); var properties = values.EnumerateObject().ToArray();
        if (depth > 8 || properties.Length is < 1 or > 256) RequestValidation.Fail(field, "Control values exceed the supported count/depth.", 422);
        foreach (var property in properties)
        {
            var name = field + "." + property.Name; RequestValidation.Title(property.Name, name, 1024);
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String: Text(property.Value.GetString(), name); break;
                case JsonValueKind.True: case JsonValueKind.False: break;
                case JsonValueKind.Object: Only(property.Value, ["data", "width", "alt"], name); Image(property.Value, name); break;
                case JsonValueKind.Array:
                    Array(property.Value, name, 1, 100); foreach (var record in property.Value.EnumerateArray()) Controls(record, name, depth + 1); break;
                default: RequestValidation.Fail(name, "Control values must be text, booleans, pictures or repeating records.", 422); break;
            }
        }
    }

    private static void Rules(JsonElement operation, string field)
    {
        var value = Get(operation, "rules"); Array(value, field + ".rules", 1, 5);
        var rules = value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : "").ToArray(); RequestValidation.Rules(rules, field + ".rules", 422);
    }
    private static void Position(JsonElement position, string field, int status)
    {
        RequestValidation.Object(position, field, status); Only(position, ["story", "path", "off"], field, status);
        Story(Get(position, "story"), field + ".story", status); Path(Get(position, "path"), field + ".path", status); Integer(Get(position, "off"), field + ".off", status: status);
    }
    private static void Story(JsonElement story, string field, int status)
    {
        if (story.ValueKind == JsonValueKind.String && story.GetString() == "body") return;
        RequestValidation.Object(story, field, status); Only(story, ["part"], field, status); Integer(Get(story, "part"), field + ".part", uint.MaxValue, status);
    }
    private static void Path(JsonElement path, string field, int status)
    {
        Array(path, field, 1, 49, status); if ((path.GetArrayLength() - 1) % 3 != 0) RequestValidation.Fail(field, "Invalid nested paragraph path.", status);
        foreach (var index in path.EnumerateArray()) Integer(index, field, uint.MaxValue, status);
    }
    private static void Only(JsonElement value, IEnumerable<string> allowed, string field, int status = 422)
    {
        foreach (var property in value.EnumerateObject()) if (!allowed.Contains(property.Name, StringComparer.Ordinal)) RequestValidation.Fail(field + "." + property.Name, "Unknown property.", status);
    }
    private static JsonElement Get(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var result) ? result : default;
    private static bool Present(JsonElement value, string key, out JsonElement result) => value.TryGetProperty(key, out result) && result.ValueKind != JsonValueKind.Null;
    private static string String(JsonElement value, string key, string field, int status = 422)
    {
        var item = Get(value, key); if (item.ValueKind != JsonValueKind.String) RequestValidation.Fail(field + "." + key, "A string is required.", status); return item.GetString()!;
    }
    private static void ShortText(JsonElement value, string field, int max, int status = 422)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > max || text.Any(char.IsControl)) RequestValidation.Fail(field, $"Use 1–{max} characters.", status);
    }
    private static void OptionalText(JsonElement value, string key, string field, int max) { if (value.TryGetProperty(key, out var item)) ShortText(item, field + "." + key, max); }
    private static void Boolean(JsonElement value, string field) { if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) RequestValidation.Fail(field, "A boolean is required.", 422); }
    private static void Flag(JsonElement value, string key, string field) { if (value.TryGetProperty(key, out var item)) Boolean(item, field + "." + key); }
    private static void Integer(JsonElement value, string field, ulong max = int.MaxValue, int status = 422) { if (!value.TryGetUInt64Safe(out var integer) || integer > max) RequestValidation.Fail(field, "A bounded nonnegative integer is required.", status); }
    private static void OptionalInteger(JsonElement value, string key, string field, ulong max = int.MaxValue, int status = 422) { if (value.TryGetProperty(key, out var item)) Integer(item, field + "." + key, max, status); }
    private static void Number(JsonElement value, string field, double min, double max, int status = 422) { if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var n) || !double.IsFinite(n) || n < min || n > max) RequestValidation.Fail(field, $"Use a finite number from {min} to {max}.", status); }
    private static void Array(JsonElement value, string field, int min, int max, int status = 422) { if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < min || value.GetArrayLength() > max) RequestValidation.Fail(field, $"Supply an array with {min}–{max} items.", status); }
    private static void RequiredChoice(JsonElement value, string key, string field, params string[] choices) => Choice(Get(value, key), field + "." + key, choices);
    private static void OptionalChoice(JsonElement value, string key, string field, params string[] choices) { if (value.TryGetProperty(key, out var item)) Choice(item, field + "." + key, choices); }
    private static void Choice(JsonElement value, string field, params string[] choices) { if (value.ValueKind != JsonValueKind.String || !choices.Contains(value.GetString())) RequestValidation.Fail(field, "Unsupported value.", 422); }
    private static bool TryGetUInt64Safe(this JsonElement value, out ulong number) { number = 0; return value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out number); }
}

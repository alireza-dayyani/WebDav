using System.Reflection;
using System.Text.Json;

namespace Inkstone.Web.Services;

public sealed class RequestValidationException(string field, string message, int status = 400) : Exception(message)
{
    public Dictionary<string, string[]> Errors { get; } = new() { [field] = [message] };
    public int Status { get; } = status;
    public IResult ToResult() => Results.ValidationProblem(Errors, statusCode: Status, title: "Request validation failed.");
}

/// <summary>Validates bound request contracts before a handler queries storage or starts engine work.</summary>
public static class RequestValidation
{
    public static RouteGroupBuilder ValidateRequests(this RouteGroupBuilder routes) { routes.WithMetadata(ResponseMetadata()); routes.AddEndpointFilterFactory(Create); return routes; }
    public static RouteHandlerBuilder ValidateRequests(this RouteHandlerBuilder route) { route.WithMetadata(ResponseMetadata()); route.AddEndpointFilterFactory(Create); return route; }

    private static object[] ResponseMetadata() => [
        new ProducesResponseTypeMetadata(400, typeof(HttpValidationProblemDetails), ["application/problem+json"]),
        new ProducesResponseTypeMetadata(413, typeof(HttpValidationProblemDetails), ["application/problem+json"]),
        new ProducesResponseTypeMetadata(422, typeof(HttpValidationProblemDetails), ["application/problem+json"])
    ];

    private static EndpointFilterDelegate Create(EndpointFilterFactoryContext factory, EndpointFilterDelegate next)
    {
        var parameters = factory.MethodInfo.GetParameters();
        return async context =>
        {
            try
            {
                for (var index = 0; index < parameters.Length; index++)
                {
                    Validate(context.Arguments[index]);
                    ValidateScalar(parameters[index], context.Arguments[index], context.HttpContext);
                }
                return await next(context);
            }
            catch (RequestValidationException error) { return error.ToResult(); }
        };
    }

    private static void ValidateScalar(ParameterInfo parameter, object? value, HttpContext context)
    {
        var name = parameter.Name ?? "request";
        if (value is Guid id && id == Guid.Empty) Fail(name, "Supply a non-empty resource ID.");
        if (value is long version && name is "version" or "revision") Version(version, name);
        if (value is int number)
        {
            if (name == "offset" && number < 0) Fail(name, "Offset must be nonnegative.");
            if (name == "limit" && number is < 1 or > 500) Fail(name, "Limit must be 1–500.");
            if (name == "page" && number is < 1 or > 100000) Fail(name, "Page must be 1–100000.");
        }
        if (name == "scale" && value is float scale) Scale(scale);
        if (name == "format" && value is string format && !DocumentConverter.ExportFormats.Contains(format)) Fail(name, "Unsupported export format.", 415);
        if (name == "reference" && value is string reference) OperationValidation.Reference(reference, context.Request.Path.Value?.Contains("/images/", StringComparison.OrdinalIgnoreCase) == true, name, 400);
    }

    public static void Validate(object? request)
    {
        switch (request)
        {
            case EditBatch input: Version(input.Version); Key(input.IdempotencyKey); Automation.ValidateOperations(input.Operations); break;
            case DocumentSearch input:
                Version(input.Version); Text(input.Text, "text", false); Choice(input.Scope, "scope", "body", "all"); break;
            case DocumentPreview input:
                Version(input.Version); Choice(input.Format, "format", "pdf", "png"); Version(input.Page, "page"); Scale(input.Scale); Automation.ValidateOperations(input.Operations); break;
            case DocumentValues input:
                Version(input.Version); Key(input.IdempotencyKey); Object(input.Values, "values"); break;
            case TableUpdate input:
                Version(input.Version); Key(input.IdempotencyKey);
                OperationValidation.Validate(Automation.Json(new { type = "updateTable", @ref = "b.0", input.Cells, input.AppendRows })); break;
            case DocumentImage input:
                Version(input.Version); Key(input.IdempotencyKey); Object(input.Target, "target");
                OperationValidation.Image(Automation.Json(new { input.Data, input.Width, input.Height, input.Alt }), "image", 400); break;
            case CommentAction input:
                Version(input.Version); Key(input.IdempotencyKey); Object(input.Target, "target"); Choice(input.Action, "action", "add", "reply", "resolve", "delete");
                if (input.Action is "add" or "reply") Text(input.Text, "text", false);
                if (input.Action != "add" && input.Id is null) Fail("id", "A comment ID is required.");
                if (input.Action == "add") OperationValidation.Target(input.Target, "target", 400);
                break;
            case ResolveChanges input:
                Version(input.Version); Key(input.IdempotencyKey); Choice(input.Decision, "decision", "accept", "reject"); Object(input.Target, "target");
                if (!input.All) OperationValidation.Target(input.Target, "target", 400);
                break;
            case Normalization input: Version(input.Version); Key(input.IdempotencyKey); Rules(input.Rules); break;
            case RestoreDocument input: Version(input.Version); Version(input.SourceVersion, "sourceVersion"); Key(input.IdempotencyKey); break;
            case CompareDocuments input:
                Source(input.Original, "original"); Source(input.Revised, "revised"); Title(input.Title); Choice(input.Level, "level", "word", "character"); Key(input.IdempotencyKey); break;
            case AssembleDocuments input:
                Sources(input.Sources); Title(input.Title); Assembly(input.Separator, input.StylePolicy); Key(input.IdempotencyKey); break;
            case BulkRender input: Version(input.Version); Records(input.Records); Key(input.IdempotencyKey); break;
            case TokenCreate input:
                Title(input.Name, "name", 80);
                if (input.Scopes is not { Length: > 0 and <= 6 } || input.Scopes.Any(scope => !Automation.Scopes.Contains(scope)) || input.Scopes.Distinct(StringComparer.Ordinal).Count() != input.Scopes.Length) Fail("scopes", "Supply distinct supported scopes (1–6).");
                if (input.ExpiresInDays is < 1 or > 365) Fail("expiresInDays", "Expiry must be 1–365 days.");
                break;
            case VersionRequest input: Version(input.Version); break;
            case JobRequest input: Job(input); break;
        }
    }

    private static void Job(JobRequest input)
    {
        Choice(input.Kind, "kind", "export", "batch", "compose", "renderBatch");
        if (!DocumentConverter.ExportFormats.Contains(input.Format)) Fail("format", "Unsupported job output format.");
        Key(input.IdempotencyKey);
        switch (input.Kind)
        {
            case "renderBatch": Source(input.Template, "template"); Records(input.Records); break;
            case "compose": Sources(input.Sources, 2); Assembly(input.Separator, "preserveSource"); break;
            case "batch": Source(input.Source, "source"); Automation.ValidateOperations(input.Operations); break;
            case "export": Source(input.Source, "source"); break;
        }
        if (input.Kind != "batch" && input.Operations is not null) Fail("operations", "Operations are accepted only for batch jobs.");
        if (input.Kind != "compose" && input.Sources is not null) Fail("sources", "Sources are accepted only for compose jobs.");
        if (input.Kind != "renderBatch" && (input.Template is not null || input.Records is not null)) Fail("template", "Template and records are accepted only for renderBatch jobs.");
        if (input.Kind is "compose" or "renderBatch" && input.Source is not null) Fail("source", "This job kind does not accept a single source.");
    }

    public static void Version(long version, string field = "version") { if (version <= 0) Fail(field, "Version must be a positive integer."); }
    public static void Source(ResourceRef? source, string field)
    {
        if (source is null || source.Id == Guid.Empty) Fail(field, "An owned resource ID and version are required.");
        Version(source!.Version, field + ".version");
    }
    public static void Sources(ResourceRef[]? sources, int minimum = 1)
    {
        if (sources is null || sources.Length < minimum || sources.Length > 32) Fail("sources", $"Supply {minimum}–32 source revisions.");
        for (var index = 0; index < sources!.Length; index++) Source(sources[index], $"sources[{index}]");
    }
    public static void Assembly(string? separator, string? policy) { Choice(separator, "separator", "paragraph", "pageBreak"); Choice(policy, "stylePolicy", "preserveSource"); }
    public static void Scale(float scale) { if (!float.IsFinite(scale) || scale is < 0.25f or > 2f) Fail("scale", "Scale must be 0.25–2."); }
    public static void Key(string? key) { if (key is not null && (key.Length is < 1 or > 128 || key.Any(char.IsControl))) Fail("idempotencyKey", "Use 1–128 printable characters."); }
    public static void Title(string? title, string field = "title", int max = 160)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > max || title.Any(c => char.IsControl(c) || c == '\uFFFC')) Fail(field, $"Use 1–{max} characters without control characters.");
    }
    public static void Text(string? text, string field, bool allowEmpty = true, int status = 400)
        => OperationValidation.Text(text, field, allowEmpty, status);
    public static void Object(JsonElement value, string field, int status = 400) { if (value.ValueKind != JsonValueKind.Object) Fail(field, "An object is required.", status); }
    public static void Choice(string? value, string field, params string[] choices) { if (!choices.Contains(value)) Fail(field, "Supported values: " + string.Join(", ", choices) + "."); }
    public static readonly string[] NormalizationRules = ["persianLetters", "persianDigits", "latinDigits", "trimTrailingSpaces", "halfSpace"];
    public static void Rules(string[]? rules, string field = "rules", int status = 400)
    {
        if (rules is not { Length: > 0 and <= 5 } || rules.Any(rule => !NormalizationRules.Contains(rule)) || rules.Distinct(StringComparer.Ordinal).Count() != rules.Length) Fail(field, "Supply distinct supported normalization rules.", status);
        if (rules!.Contains("persianDigits") && rules.Contains("latinDigits")) Fail(field, "Choose only one digit system.", status);
    }
    public static void Records(RenderRecord[]? records)
    {
        if (records is not { Length: > 0 and <= 100 }) Fail("records", "Supply 1–100 records.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < records!.Length; index++)
        {
            var field = $"records[{index}]"; var record = records[index];
            if (record is null) Fail(field, "A record is required.");
            Title(record!.Key, field + ".key", 128); Title(record.Title, field + ".title");
            if (!keys.Add(record.Key)) Fail(field + ".key", "Record keys must be unique.");
            if (record.Values is null || record.Values.Count > 256) Fail(field + ".values", "Supply a values object with at most 256 entries.");
            OperationValidation.BookmarkValues(Automation.Json(record.Values!), field + ".values", allowEmpty: true, status: 400);
        }
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    public static void Fail(string field, string message, int status = 400) => throw new RequestValidationException(field, message, status);
}

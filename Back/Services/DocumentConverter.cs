using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace Inkstone.Web.Services;

public sealed class ConversionException(string message, int status = 422, string? code = null, long? maxBytes = null) : Exception(message)
{
    public int Status { get; } = status;
    public string? Code { get; } = code;
    public long? MaxBytes { get; } = maxBytes;
}

/// <summary>Runs the existing Rust engine in a bounded, isolated job directory.</summary>
public sealed class DocumentConverter(IConfiguration configuration, ILogger<DocumentConverter> logger, ConverterCapacity capacity) : IDocumentProcessor
{
    public const int MaxDocumentBytes = FileLimits.MaxModelBytes;
    public static readonly string[] ImportFormats = ["docx", "odt", "rtf", "txt", "md", "json"];
    public static readonly string[] ExportFormats = ["docx", "pdf", "txt", "md", "json"];
    public static readonly string[] Languages = ["en-US", "fa-IR", "ar-SA", "ur-PK"];

    public async Task<byte[]> ImportAsync(byte[] content, string extension, long maxUploadBytes, CancellationToken ct)
    {
        if (!ImportFormats.Contains(extension)) throw new ConversionException("Unsupported upload format.", 415);
        if (content.Length == 0 || content.Length > maxUploadBytes) throw new ConversionException("Choose a non-empty file within the current upload limit.", 413, "upload_too_large", maxUploadBytes);
        if (extension == "json") ValidateModel(content);
        if (extension is "docx" or "odt") ValidateArchive(content);
        var result = await RunAsync(content, extension, "json", null, ct);
        ValidateModel(result);
        return result;
    }

    public Task<byte[]> CreateAsync(string language, CancellationToken ct)
    {
        if (!Languages.Contains(language)) throw new ConversionException("Unsupported document language.", 400);
        return RunAsync(null, "json", "json", language, ct);
    }

    public async Task<byte[]> ExportAsync(byte[] content, string format, CancellationToken ct)
    {
        if (!ExportFormats.Contains(format)) throw new ConversionException("Unsupported download format.", 415);
        if (content.Length == 0) content = await CreateAsync("en-US", ct);
        if (format == "json") return content;
        ValidateModel(content);
        return await RunAsync(content, "json", format, null, ct);
    }

    public async Task<BookmarkInfo[]> ReadBookmarksAsync(byte[] content, CancellationToken ct)
    {
        if (content.Length == 0) return [];
        ValidateModel(content);
        var result = await RunJobAsync(content, "json", null, null, ct, "document.bookmarks", new { });
        return result.Report.GetProperty("bookmarks").Deserialize<BookmarkInfo[]>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    public async Task<byte[]> FillBookmarksAsync(byte[] content, Dictionary<string, string> values, CancellationToken ct)
    {
        ValidateModel(content);
        var result = await RunJobAsync(content, "json", "json", null, ct, "document.fillBookmarks", new { values });
        ValidateModel(result.Content);
        return result.Content;
    }

    private static readonly byte[] BlankModel = "{\"body\":[{\"kind\":\"para\",\"text\":\"\",\"runs\":[]}]}"u8.ToArray();

    public async Task<byte[]> LetterTemplateAsync(string language, CancellationToken ct, string? paper = null)
        => (await RunJobAsync(BlankModel, "json", "json", null, ct, "document.letterTemplate", new { language, paper })).Content;

    public async Task<JsonElement> PreflightAsync(byte[] content, CancellationToken ct)
        => (await RunJobAsync(content.Length == 0 ? BlankModel : content, "json", null, null, ct, "document.preflight", new { })).Report;

    public async Task<JsonElement> BookmarkStateAsync(byte[] content, string name, CancellationToken ct)
        => (await RunJobAsync(content, "json", null, null, ct, "document.bookmarkState", new { name })).Report;

    public async Task<JsonElement> ImageInfoAsync(byte[] bytes, CancellationToken ct)
        => (await RunJobAsync(BlankModel, "json", null, null, ct, "document.imageInfo", new { data = Convert.ToBase64String(bytes) })).Report;

    public async Task<AutomationResult> AutomateAsync(byte[] content, string action, object parameters, CancellationToken ct, byte[][]? sources = null)
    {
        var command = action switch
        {
            "query" => "document.query", "batch" => "document.batch",
            "compare" => "review.compare", "assemble" => "document.assemble",
            _ => throw new ConversionException("Unsupported automation action.", 400)
        };
        var map = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(parameters, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
        var result = await RunJobAsync(content.Length == 0 ? BlankModel : content, "json", action == "query" ? null : "json", null, ct, command, map, sources: sources);
        if (action != "query") ValidateModel(result.Content);
        return new AutomationResult(result.Content, result.Report);
    }

    public async Task<byte[]> RenderPageAsync(byte[] content, int page, float scale, CancellationToken ct)
    {
        if (page < 1 || !float.IsFinite(scale) || scale is < 0.25f or > 2f) throw new ConversionException("Choose a 1-based page and scale from 0.25 to 2.", 400);
        var parameters = new Dictionary<string, object?> { ["page"] = page, ["scale"] = scale };
        return (await RunJobAsync(content.Length == 0 ? BlankModel : content, "json", "png", null, ct, "file.exportPng", parameters)).Content;
    }

    public async Task<byte[]> ComposeAsync(byte[] content, Dictionary<string, string> values, Dictionary<string, TemplateImage> images, byte[]? body, string? bodyBookmark, CancellationToken ct)
    {
        var parameters = new Dictionary<string, object?> { ["values"] = values, ["images"] = images, ["bodyBookmark"] = bodyBookmark };
        var result = await RunJobAsync(content, "json", "json", null, ct, "document.compose", parameters, body);
        ValidateModel(result.Content);
        return result.Content;
    }

    public static void ValidateModel(byte[] bytes)
    {
        if (bytes.Length > MaxDocumentBytes) throw new ConversionException("The editable document exceeds the 256 MiB processing limit.", 413, "model_too_large", MaxDocumentBytes);
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 256 });
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Array)
                throw new ConversionException("The document format is invalid.");
        }
        catch (JsonException) { throw new ConversionException("The document JSON is invalid."); }
    }

    private static void ValidateArchive(byte[] content)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
            if (archive.Entries.Count > 2048 || archive.Entries.Any(e => e.Length > FileLimits.MaxArchiveEntryBytes) || archive.Entries.Sum(e => e.Length) > FileLimits.MaxArchiveBytes)
                throw new ConversionException("The expanded archive exceeds the processing limits (256 MiB total, 128 MiB per entry, 2048 entries).", 413, "archive_too_large", FileLimits.MaxArchiveBytes);
        }
        catch (InvalidDataException) { throw new ConversionException("The document archive is invalid."); }
    }

    private async Task<byte[]> RunAsync(byte[]? input, string inputFormat, string outputFormat, string? language, CancellationToken ct)
        => (await RunJobAsync(input, inputFormat, outputFormat, language, ct)).Content;

    private sealed record JobResult(byte[] Content, JsonElement Report);

    private async Task<JobResult> RunJobAsync(byte[]? input, string inputFormat, string? outputFormat, string? language, CancellationToken ct, string? command = null, object? parameters = null, byte[]? body = null, byte[][]? sources = null)
    {
        using var permit = await capacity.EnterAsync(command == "document.preflight", ct);
        string? directory = null;
        try
        {
            var executable = configuration["Rust:Executable"] ?? Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "inkstone-cli.exe" : "inkstone-cli");
            if (!File.Exists(executable)) throw new ConversionException("The document converter is unavailable. Build the backend with Rust enabled.", 503);
            directory = Directory.CreateTempSubdirectory("inkstone-convert-").FullName;
            var source = Path.Combine(directory, "source." + inputFormat);
            var destination = Path.Combine(directory, "result." + outputFormat);
            var start = new ProcessStartInfo(Path.GetFullPath(executable))
            {
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true
            };
            if (command is not null)
            {
                await File.WriteAllBytesAsync(source, input!, ct);
                // Values stay in a temporary JSON script, never in shell syntax or process arguments.
                var script = Path.Combine(directory, "job.json");
                if (command == "file.exportPng" && parameters is Dictionary<string, object?> renderParameters) renderParameters["path"] = destination;
                if (sources is not null && parameters is Dictionary<string, object?> sourceMap)
                {
                    var paths = new List<string>();
                    foreach (var sourceBytes in sources)
                    {
                        ValidateModel(sourceBytes);
                        var sourcePath = Path.Combine(directory, $"part-{paths.Count}.json");
                        await File.WriteAllBytesAsync(sourcePath, sourceBytes, ct);
                        paths.Add(sourcePath);
                    }
                    sourceMap["sourcePaths"] = paths;
                    if (command == "review.compare") sourceMap["path"] = paths.Single();
                }
                if (body is not null && parameters is Dictionary<string, object?> map)
                {
                    var bodyFile = Path.Combine(directory, "body.json");
                    await File.WriteAllBytesAsync(bodyFile, body, ct);
                    map["sourcePath"] = bodyFile;
                }
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new { commands = new[] { new { command, @params = parameters } } }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (bytes.Length > 4 * FileLimits.MiB) throw new ConversionException("Bookmark values exceed the command size limit.", 413);
                await File.WriteAllBytesAsync(script, bytes, ct);
                foreach (var arg in new[] { "run", "--file", source, "--script", script, "--print" }) start.ArgumentList.Add(arg);
                if (outputFormat is not null && command != "file.exportPng") { start.ArgumentList.Add("--save"); start.ArgumentList.Add(destination); }
            }
            else if (input is not null)
            {
                await File.WriteAllBytesAsync(source, input, ct);
                foreach (var arg in new[] { "convert", source, destination }) start.ArgumentList.Add(arg);
            }
            else
            {
                foreach (var arg in new[] { "run", "--template", "blank", "--cmd", "review.language=" + JsonSerializer.Serialize(new { lang = language, @default = true }) }) start.ArgumentList.Add(arg);
                if (language is "fa-IR" or "ar-SA" or "ur-PK")
                {
                    start.ArgumentList.Add("--cmd"); start.ArgumentList.Add("para.rtl={\"value\":true}");
                }
                start.ArgumentList.Add("--save"); start.ArgumentList.Add(destination);
            }
            using var process = new Process { StartInfo = start };
            try { process.Start(); }
            catch (System.ComponentModel.Win32Exception) { throw new ConversionException("The document converter could not start.", 503); }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            // Drain output without retaining arbitrary converter output in memory or HTTP responses.
            var stdout = ReadOutputAsync(process.StandardOutput.BaseStream, command is not null, timeout.Token);
            var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            try { await Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdout, stderr); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                ct.ThrowIfCancellationRequested();
                throw new ConversionException("Document conversion timed out.");
            }
            if (process.ExitCode != 0 || (outputFormat is not null && !File.Exists(destination)))
                throw new ConversionException(command is null ? "This file could not be converted. Check the format and try again." : "The document operation failed. Check targets, expected matches, value types and supported ranges; the document or selected controls may be protected. No changes were saved.");
            JsonElement report = default;
            if (command is not null)
            {
                var output = await stdout;
                var prefix = command + ": ";
                if (output is null || !output.StartsWith(prefix, StringComparison.Ordinal)) throw new ConversionException("The bookmark report is invalid or too large.");
                try { report = JsonSerializer.Deserialize<JsonElement>(output[prefix.Length..]); }
                catch (JsonException) { throw new ConversionException("The bookmark report is invalid."); }
            }
            if (outputFormat is null) return new JobResult([], report);
            if (new FileInfo(destination).Length > (outputFormat == "json" ? MaxDocumentBytes : FileLimits.MaxModelBytes))
                throw new ConversionException("The converted file exceeds the 256 MiB processing limit.", 413, "model_too_large", MaxDocumentBytes);
            return new JobResult(await File.ReadAllBytesAsync(destination, ct), report);
        }
        finally
        {
            if (directory is not null)
            {
                try { Directory.Delete(directory, recursive: true); }
                catch (IOException) { logger.LogWarning("Could not remove a converter temporary directory."); }
                catch (UnauthorizedAccessException) { logger.LogWarning("Could not remove a converter temporary directory."); }
            }
        }
    }

    private static async Task<string?> ReadOutputAsync(Stream stream, bool capture, CancellationToken ct)
    {
        const int maxReportBytes = 2 * FileLimits.MiB;
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var overflow = false;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (!capture || overflow) continue;
            if (output.Length + read > maxReportBytes) { overflow = true; continue; }
            output.Write(buffer, 0, read);
        }
        return overflow ? null : System.Text.Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
    }

}

public sealed record BookmarkInfo(string Name, bool Replaceable, string? Issue);
public sealed record TemplateImage(string Data, float Width, string Alt = "");

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
public sealed class DocumentConverter(IConfiguration configuration, ILogger<DocumentConverter> logger) : IDisposable
{
    public const int MaxDocumentBytes = FileLimits.MaxModelBytes;
    public static readonly string[] ImportFormats = ["docx", "odt", "rtf", "txt", "md", "json"];
    public static readonly string[] ExportFormats = ["docx", "pdf", "txt", "md", "json"];
    public static readonly string[] Languages = ["en-US", "fa-IR", "ar-SA", "ur-PK"];
    private readonly SemaphoreSlim slots = new(2);

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
    {
        if (!await slots.WaitAsync(TimeSpan.FromSeconds(5), ct)) throw new ConversionException("The converter is busy. Try again shortly.", 429);
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
            if (input is not null)
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
            var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            try { await Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdout, stderr); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                ct.ThrowIfCancellationRequested();
                throw new ConversionException("Document conversion timed out.");
            }
            if (process.ExitCode != 0 || !File.Exists(destination)) throw new ConversionException("This file could not be converted. Check the format and try again.");
            if (new FileInfo(destination).Length > (outputFormat == "json" ? MaxDocumentBytes : FileLimits.MaxModelBytes))
                throw new ConversionException("The converted file exceeds the 256 MiB processing limit.", 413, "model_too_large", MaxDocumentBytes);
            return await File.ReadAllBytesAsync(destination, ct);
        }
        finally
        {
            if (directory is not null)
            {
                try { Directory.Delete(directory, recursive: true); }
                catch (IOException) { logger.LogWarning("Could not remove a converter temporary directory."); }
                catch (UnauthorizedAccessException) { logger.LogWarning("Could not remove a converter temporary directory."); }
            }
            slots.Release();
        }
    }

    public void Dispose() => slots.Dispose();
}

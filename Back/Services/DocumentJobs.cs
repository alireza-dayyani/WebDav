using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using Inkstone.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

public sealed class DocumentJobRunner(IServiceScopeFactory scopes, ILogger<DocumentJobRunner> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> running = new();
    public void Cancel(Guid id)
    {
        if (running.TryGetValue(id, out var cancellation))
            try { cancellation.Cancel(); } catch (ObjectDisposedException) { /* The worker completed concurrently. */ }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.DocumentJobs.Where(j => j.Status == "running").ExecuteUpdateAsync(set => set.SetProperty(j => j.Status, "failed").SetProperty(j => j.Error, "The server restarted during processing; submit a new job.").SetProperty(j => j.Work, "{}").SetProperty(j => j.Version, j => j.Version + 1), stoppingToken);
        }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunNext(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "A document job worker iteration failed."); }
            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunNext(CancellationToken stoppingToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expiry = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeMilliseconds();
        await db.DocumentJobs.Where(j => j.UpdatedAt < expiry && j.Status != "running" && j.Status != "queued").ExecuteDeleteAsync(stoppingToken);
        await db.OperationReceipts.Where(r => r.Scope == "job" && r.CreatedAt < expiry).ExecuteDeleteAsync(stoppingToken);
        var job = await db.DocumentJobs.Where(j => j.Status == "queued").OrderBy(j => j.CreatedAt).FirstOrDefaultAsync(stoppingToken);
        if (job is null) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        running[job.Id] = cancellation;
        try
        {
            var claimed = await db.DocumentJobs.Where(j => j.Id == job.Id && j.Status == "queued" && j.Version == job.Version).ExecuteUpdateAsync(set => set.SetProperty(j => j.Status, "running").SetProperty(j => j.Version, j => j.Version + 1).SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), stoppingToken);
            if (claimed == 0) return;
            var converter = scope.ServiceProvider.GetRequiredService<IDocumentProcessor>();
            var work = JsonSerializer.Deserialize<JobWork>(job.Work, Studio.Json)!;
            byte[] result; JsonElement report;
            if (job.Kind == "export") { result = await converter.ExportAsync(work.Input, job.Format, cancellation.Token); report = Automation.Json(new { exported = job.Format }); }
            else if (job.Kind == "renderBatch") { (result, report) = await RenderArchive(converter, work, cancellation.Token); }
            else
            {
                var output = await converter.AutomateAsync(work.Input, job.Kind == "batch" ? "batch" : "assemble", work.Parameters, cancellation.Token, work.Sources);
                result = await converter.ExportAsync(output.Content, job.Format, cancellation.Token); report = output.Report;
            }
            await Finish(db, job.Id, "succeeded", result, Studio.Encode(report), "", stoppingToken);
        }
        catch (OperationCanceledException) { await Finish(db, job.Id, "cancelled", [], "{}", "", CancellationToken.None); }
        catch (ConversionException error) { await Finish(db, job.Id, "failed", [], "{}", error.Message, stoppingToken); }
        catch (Exception error)
        {
            logger.LogError(error, "Document job {JobId} failed.", job.Id);
            await Finish(db, job.Id, "failed", [], "{}", "Document job failed.", stoppingToken);
        }
        finally { running.TryRemove(job.Id, out _); }
    }

    private static async Task<(byte[] Content, JsonElement Report)> RenderArchive(IDocumentProcessor converter, JobWork work, CancellationToken ct)
    {
        var records = work.Parameters.GetProperty("records").Deserialize<RenderRecord[]>(Studio.Json)!;
        var format = work.Parameters.GetProperty("format").GetString()!;
        using var output = new MemoryStream();
        var report = new List<object>(); long total = 0;
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            for (var index = 0; index < records.Length; index++)
            {
                ct.ThrowIfCancellationRequested(); var record = records[index];
                try
                {
                    var content = record.Values.Count == 0 ? work.Input : (await converter.AutomateAsync(work.Input, "batch", new { operations = new[] { new { type = "fillBookmarks", record.Values } } }, ct)).Content;
                    var bytes = await converter.ExportAsync(content, format, ct);
                    total += bytes.Length; if (total > FileLimits.MaxModelBytes) throw new ConversionException("Bulk output exceeds 256 MiB.", 413);
                    var name = $"document-{index + 1}.{format}";
                    await using var entry = archive.CreateEntry(name).Open(); await entry.WriteAsync(bytes, ct);
                    report.Add(new { record.Key, success = true, file = name, record.Title });
                }
                catch (ConversionException error) when (error.Status == 422) { report.Add(new { record.Key, success = false, error = error.Message }); }
            }
            await using var manifest = archive.CreateEntry("manifest.json").Open(); await JsonSerializer.SerializeAsync(manifest, report, Studio.Json, ct);
        }
        return (output.ToArray(), Automation.Json(new { records = report }));
    }

    private static Task<int> Finish(AppDbContext db, Guid id, string status, byte[] result, string report, string error, CancellationToken ct)
        => db.DocumentJobs.Where(j => j.Id == id && j.Status == "running").ExecuteUpdateAsync(set => set.SetProperty(j => j.Status, status).SetProperty(j => j.Result, result).SetProperty(j => j.Report, report).SetProperty(j => j.Error, error).SetProperty(j => j.Work, "{}").SetProperty(j => j.Version, j => j.Version + 1).SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), ct);
}

public static class DocumentJobEndpoints
{
    public static void MapDocumentJobs(this WebApplication app)
    {
        var routes = app.MapGroup("/api/document-jobs").RequireAuthorization().WithTags("Document jobs").WithMetadata(new BookmarkValuesRequest()).ValidateRequests();
        routes.MapPost("/", Submit);
        routes.MapGet("/", async (int? page, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var index = page ?? 1; if (index is < 1 or > 100000) return Studio.Bad("Invalid page.");
            var rows = await db.DocumentJobs.AsNoTracking().Where(j => j.OwnerId == Ownership.Owner(user)).OrderByDescending(j => j.CreatedAt).Skip((index - 1) * 50).Take(51)
                .Select(j => new { j.Id, j.Kind, j.Status, j.Version, j.CreatedAt, j.UpdatedAt, j.Format, j.Error }).ToListAsync(ct);
            return Results.Ok(new { page = index, hasMore = rows.Count > 50, items = rows.Take(50) });
        });
        routes.MapGet("/{id:guid}", async (Guid id, long version, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var job = await db.DocumentJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id && j.OwnerId == Ownership.Owner(user), ct);
            if (job is null) return Results.NotFound();
            // Poll with the known version: newer state is returned; future versions are rejected.
            if (version < 1 || version > job.Version) return Studio.Conflict();
            return Results.Ok(Automation.JobSummary(job));
        });
        routes.MapGet("/{id:guid}/download", async (Guid id, long version, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var job = await db.DocumentJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id && j.OwnerId == Ownership.Owner(user), ct);
            if (job is null) return Results.NotFound();
            if (job.Version != version || job.Status != "succeeded") return Studio.Conflict();
            return job.Format switch { "zip" => Results.File(job.Result, "application/zip", $"documents-{id}.zip"), "png" => Results.File(job.Result, "image/png", $"document-{id}.png"), _ => Studio.File(job.Result, "document-" + id, job.Format) };
        });
        routes.MapPost("/{id:guid}/cancel", async (Guid id, VersionRequest input, ClaimsPrincipal user, AppDbContext db, DocumentJobRunner runner, CancellationToken ct) =>
        {
            var job = await db.DocumentJobs.SingleOrDefaultAsync(j => j.Id == id && j.OwnerId == Ownership.Owner(user), ct);
            if (job is null) return Results.NotFound();
            if (job.Version != input.Version || job.Status is not ("queued" or "running")) return Studio.Conflict();
            job.Status = "cancelled"; job.Work = "{}"; job.Version++; job.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (!await Studio.Save(db, ct)) return Studio.Conflict();
            runner.Cancel(id); return Results.Ok(Automation.JobSummary(job));
        });
    }

    private static async Task<IResult> Submit(JobRequest input, ClaimsPrincipal user, AppDbContext db, IDocumentProcessor converter, CancellationToken ct)
    {
        var owner = Ownership.Owner(user);
        if (!AutomationTokens.HasScope(user, input.Kind == "renderBatch" ? "templates" : "documents")) return Results.Forbid();
        if (input.Kind is not ("export" or "batch" or "compose" or "renderBatch") || !DocumentConverter.ExportFormats.Contains(input.Format)) return Studio.Bad("Choose export, batch, compose or renderBatch and a supported format.");
        if (await Automation.Replay(db, owner, "job", input.IdempotencyKey, input, ct) is { } replay) return replay;
        if (await db.DocumentJobs.CountAsync(j => j.OwnerId == owner && (j.Status == "queued" || j.Status == "running"), ct) >= 10) return Studio.Bad("At most ten queued/running jobs per account.", 429);
        if (await db.DocumentJobs.CountAsync(j => j.OwnerId == owner, ct) >= 100) return Studio.Bad("At most 100 retained jobs/previews per account; results expire after 24 hours.", 429);
        byte[][] sources;
        if (input.Kind == "renderBatch")
        {
            if (input.Template is null || input.Records is not { Length: > 0 and <= 100 } || input.Records.Any(r => r is null || Studio.Title(r.Key, 128) is null || Studio.Title(r.Title) is null || r.Values is null) || input.Records.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count() != input.Records.Length) return Studio.Bad("Supply an owned template revision and 1–100 records with distinct keys, titles and values.");
            var template = await Studio.Find(db, owner, "template", input.Template.Id, ct, false);
            if (template is null) return Results.NotFound();
            if (template.Version != input.Template.Version) return Studio.Conflict();
            if (Studio.Read<TemplateConfig>(template).Kind != "document") return Studio.Bad("Bulk jobs use document templates.", 422);
            foreach (var record in input.Records) if (record.Values.Count > 0) Automation.ValidateOperations([Automation.Json(new { type = "fillBookmarks", record.Values })]);
            sources = [template.Content];
        }
        else
        {
            var refs = input.Kind == "compose" ? input.Sources : input.Source is null ? null : new[] { input.Source };
            sources = await Automation.ReadSources(db, owner, refs, ct);
        }
        if (input.Kind == "batch") Automation.ValidateOperations(input.Operations);
        if (input.Kind == "compose" && sources.Length < 2) return Studio.Bad("Composition jobs require at least two source documents.");
        var model = sources[0].Length == 0 ? await converter.CreateAsync("en-US", ct) : sources[0];
        var parameters = input.Kind switch { "batch" => Automation.Json(new { input.Operations }), "renderBatch" => Automation.Json(new { input.Records, input.Format }), _ => Automation.Json(new { input.Separator, stylePolicy = "preserveSource" }) };
        var work = new JobWork(model, input.Kind == "compose" ? sources.Skip(1).ToArray() : [], parameters);
        var encoded = Studio.Encode(work);
        if (encoded.Length > 350 * FileLimits.MiB) return Studio.Bad("Job input exceeds the processing limit.", 413);
        var job = new DocumentJob { OwnerId = owner, Kind = input.Kind, Format = input.Kind == "renderBatch" ? "zip" : input.Format, Work = encoded }; db.DocumentJobs.Add(job);
        var response = Automation.JobSummary(job); Automation.Receipt(db, owner, "job", input.IdempotencyKey, input, response);
        var saved = await Automation.Save(db, response, ct);
        return saved;
    }
}

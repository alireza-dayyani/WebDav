using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Inkstone.Web.Data;
using Inkstone.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inkstone.Api.Tests;

/// <summary>Maps the real endpoint handlers with SQLite and a controlled engine boundary.
/// Production cookie/CSRF behavior remains covered by the standalone API smoke tests.</summary>
internal sealed class EndpointHost : IAsyncDisposable
{
    public const string Owner = "owner", OtherOwner = "other-owner";
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private WebApplication app = null!;
    public HttpClient Client { get; private set; } = null!;
    public FakeProcessor Processor { get; } = new();
    public Guid DocumentId { get; } = Guid.NewGuid();
    public Guid TemplateId { get; } = Guid.NewGuid();

    public static async Task<EndpointHost> CreateAsync()
    {
        var host = new EndpointHost();
        await host.connection.OpenAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(host.connection));
        builder.Services.AddSingleton<IDocumentProcessor>(host.Processor);
        builder.Services.AddOptions<ConverterCapacityOptions>();
        builder.Services.AddSingleton<ConverterCapacity>();
        builder.Services.AddSingleton<DocumentConverter>();
        builder.Services.AddSingleton<DocumentJobRunner>(); // Cancellation only; the worker is deliberately not hosted.
        builder.Services.AddAuthentication(TestAuthentication.Scheme).AddScheme<AuthenticationSchemeOptions, TestAuthentication>(TestAuthentication.Scheme, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery();
        host.app = builder.Build();
        host.app.Use(async (context, next) =>
        {
            try { await next(context); }
            catch (RequestValidationException error) { await error.ToResult().ExecuteAsync(context); }
            catch (ConversionException error) { await Microsoft.AspNetCore.Http.Results.Problem(error.Message, statusCode: error.Status).ExecuteAsync(context); }
        });
        host.app.UseAuthentication();
        host.app.Use(AutomationTokens.Authenticate);
        host.app.UseAuthorization();
        host.app.UseAntiforgery();
        host.app.MapLetterResources();
        host.app.MapDocumentAutomation(); host.app.MapDocumentJobs(); host.app.MapAutomationTokens();
        host.app.MapLibraryRecovery();
        host.app.MapLibrarySearch(); host.app.MapRevisionRetention();
        host.app.MapCollaboration();
        await host.WithDatabase(async db =>
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new Microsoft.AspNetCore.Identity.IdentityUser { Id = Owner, UserName = Owner, Email = "owner@example.test" }, new Microsoft.AspNetCore.Identity.IdentityUser { Id = OtherOwner, UserName = OtherOwner, Email = "other@example.test" });
            db.Documents.Add(new StoredDocument { Id = host.DocumentId, OwnerId = Owner, Title = "Original", Content = FakeProcessor.Original });
            db.WorkspaceItems.Add(new WorkspaceItem { Id = host.TemplateId, OwnerId = Owner, Kind = "template", Title = "Template", Content = FakeProcessor.Original, Data = Studio.Encode(new TemplateConfig("document", "en-US", [], [])) });
            await db.SaveChangesAsync();
        });
        await host.app.StartAsync();
        host.Client = host.app.GetTestClient(); host.Client.DefaultRequestHeaders.Add("X-Test-Owner", Owner);
        return host;
    }

    public async Task WithDatabase(Func<AppDbContext, Task> action)
    {
        using var scope = app.Services.CreateScope(); await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose(); await app.DisposeAsync(); await connection.DisposeAsync();
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "TestCookie";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var owner = Request.Headers["X-Test-Owner"].ToString();
            if (owner.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new(ClaimTypes.NameIdentifier, owner), new(ClaimTypes.Name, owner + "@example.test")], Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
        }
    }
}

internal sealed class FakeProcessor : IDocumentProcessor
{
    public static byte[] Original => "{\"body\":[{\"kind\":\"para\",\"text\":\"Original\",\"runs\":[]}]}"u8.ToArray();
    public static byte[] Changed => "{\"body\":[{\"kind\":\"para\",\"text\":\"Changed\",\"runs\":[]}]}"u8.ToArray();
    public int Calls { get; private set; }
    public bool Fail { get; set; }
    public Func<Task>? BeforeAutomate { get; set; }
    public byte[]? LastInput { get; private set; }
    public object? LastParameters { get; private set; }
    public async Task<AutomationResult> AutomateAsync(byte[] content, string action, object parameters, CancellationToken ct, byte[][]? sources = null)
    {
        Calls++; LastInput = content.ToArray(); LastParameters = parameters;
        if (BeforeAutomate is not null) await BeforeAutomate();
        if (Fail) throw new ConversionException("Engine refused a document-specific edit.");
        return new AutomationResult(Changed, Automation.Json(new { action, results = new[] { new { replaced = 1 } } }));
    }
    public Task<byte[]> CreateAsync(string language, CancellationToken ct) { Calls++; return Task.FromResult(Original); }
    public Task<byte[]> ExportAsync(byte[] content, string format, CancellationToken ct) { Calls++; return Task.FromResult("%PDF-fixture"u8.ToArray()); }
    public Task<JsonElement> PreflightAsync(byte[] content, CancellationToken ct) { Calls++; return Task.FromResult(Automation.Json(new { @protected = false, pendingRevisions = false })); }
    public Task<byte[]> RenderPageAsync(byte[] content, int page, float scale, CancellationToken ct) { Calls++; return Task.FromResult(new byte[] { 137, 80, 78, 71 }); }
}

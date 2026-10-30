using static Ownership;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Inkstone.Web.Data;
using Inkstone.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var dataDirectory = Path.GetFullPath(builder.Configuration["Storage:DataDirectory"] ?? "App_Data", builder.Environment.ContentRootPath);
Directory.CreateDirectory(dataDirectory);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = FileLimits.MaxRequestBytes);
builder.Services.Configure<IISServerOptions>(o => o.MaxRequestBodySize = FileLimits.MaxRequestBytes);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = (long)FileLimits.MaxUploadMiB * FileLimits.MiB);
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(dataDirectory, "inkstone.db")}"));
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys"))).SetApplicationName("Inkstone");
builder.Services.AddIdentity<IdentityUser, IdentityRole>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 10;
    o.Password.RequireNonAlphanumeric = false;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "Inkstone.Session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromDays(7);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization(o => o.AddPolicy(AccessRoles.Policy, policy => policy.RequireAuthenticatedUser().AddRequirements(new AdministratorRequirement())));
builder.Services.AddScoped<IAuthorizationHandler, AdministratorHandler>();
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.SuppressReadingTokenFromFormBody = true;
    o.Cookie.Name = "Inkstone.Csrf";
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = ApiTraffic.Create(builder.Configuration.GetValue("Traffic:AuthenticatedPerMinute", 600), builder.Configuration.GetValue("Traffic:AnonymousPerMinute", 120));
    o.OnRejected = async (context, ct) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await Results.Problem("Too many requests. Wait before retrying.", statusCode: 429,
            extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" }).ExecuteAsync(context.HttpContext);
    };
    o.AddPolicy("accounts", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddProblemDetails();
builder.Services.AddOptions<ConverterCapacityOptions>().BindConfiguration("Conversion")
    .Validate(o => o.ValidationSlots is >= 1 and <= 16 && o.ConversionSlots is >= 1 and <= 16, "Converter slot counts must be 1–16.").ValidateOnStart();
builder.Services.AddSingleton<ConverterCapacity>();
builder.Services.AddSingleton<DocumentConverter>();
builder.Services.AddSingleton<IDocumentProcessor>(sp => sp.GetRequiredService<DocumentConverter>());
builder.Services.AddScoped<LetterComposer>();
builder.Services.AddSingleton<DocumentJobRunner>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DocumentJobRunner>());
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "Inkstone API";
    document.Info.Version = "v1";
    document.Info.Description = "Private document workspace. Register or log in to obtain a session cookie. Before every mutation, fetch /api/csrf with the same cookie jar and send its token in X-CSRF-TOKEN. Refresh the token after login/logout. Uploads accept multipart/form-data; downloads return file attachments. Documents are private by default; explicit reader/editor grants authorize cookie-session access. Sharing management and complete history remain owner-only; scoped tokens never inherit grants. Explicit /api/admin routes allow live-role-authorized administrators to inspect accounts/files, assign administrator roles and change upload limits.";
    return Task.CompletedTask;
}).AddSchemaTransformer((schema, context, _) =>
{
    if (context.JsonTypeInfo.Type == typeof(ReplaceBookmarks))
    {
        schema.Properties!["values"] = new Microsoft.OpenApi.OpenApiSchema
        {
            Type = Microsoft.OpenApi.JsonSchemaType.Object,
            AdditionalProperties = new Microsoft.OpenApi.OpenApiSchema { Type = Microsoft.OpenApi.JsonSchemaType.String },
            MinProperties = 1, MaxProperties = 256,
            Description = "Exact bookmark names mapped to literal text strings; at most 512 KiB of UTF-8 text in total."
        };
        schema.Examples = [System.Text.Json.Nodes.JsonNode.Parse("""{"version":1,"values":{"CustomerName":"علی رضایی","Notes":"First line\nSecond line"}}""")!];
    }
    return Task.CompletedTask;
}));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.MaxDepth = 256);
var app = builder.Build();
// Only explicitly configured infrastructure may supply client/protocol headers.
if (builder.Configuration["Proxy:KnownProxy"] is { Length: > 0 } knownProxy)
{
    var forwarding = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1
    };
    forwarding.KnownProxies.Add(System.Net.IPAddress.Parse(knownProxy));
    app.UseForwardedHeaders(forwarding);
}
// The in-process queue owns this data directory exclusively. A second API process
// must fail before startup recovery can invalidate the first process's running jobs.
using var instanceLease = new FileStream(Path.Combine(dataDirectory, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
using (var scope = app.Services.CreateScope())
    await BootstrapAdministration.InitializeAsync(scope.ServiceProvider, dataDirectory, builder.Configuration);

app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (RequestValidationException error) { await error.ToResult().ExecuteAsync(context); }
    catch (ConversionException error) { await Results.Problem(error.Message, statusCode: error.Status, extensions: new Dictionary<string, object?> { ["code"] = error.Code, ["maxBytes"] = error.MaxBytes }).ExecuteAsync(context); }
    catch (DbUpdateConcurrencyException) { await Results.Problem("The document changed. Refresh and retry with its current version.", statusCode: 409).ExecuteAsync(context); }
    catch (DbUpdateException error) when (error.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 } && error.Entries.Any(entry => entry.Entity is DocumentRevision or OperationReceipt))
    { await Results.Problem("A concurrent operation completed first. Retry with the same idempotency key or refresh the document version.", statusCode: 409).ExecuteAsync(context); }
    catch (BadHttpRequestException error) when (error.StatusCode is >= 400 and < 500 && !context.Response.HasStarted)
    {
        await Results.Problem(error.StatusCode == 413 ? "The request exceeds the current size limit." : "The request is invalid. Check its content type, JSON and required parameters.", statusCode: error.StatusCode,
            extensions: new Dictionary<string, object?> { ["code"] = error.StatusCode == 413 ? "request_too_large" : "invalid_request" }).ExecuteAsync(context);
    }
});
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseWhen(context => context.Request.Path != "/api/health" && context.Request.Path != "/api/ready", branch => branch.UseHttpsRedirection());
}
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self'; frame-src 'self' blob:; object-src 'none'; base-uri 'self'; frame-ancestors 'self'";
    if (app.Environment.IsDevelopment() && context.Request.Path.StartsWithSegments("/swagger"))
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'self'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseAuthentication();
app.Use(AutomationTokens.Authenticate);
app.UseAuthorization();
// An editor tab is bound to the account that opened it. This header is only an
// additional constraint; authentication and resource authorization still apply.
app.Use(async (context, next) =>
{
    if (context.Request.Headers.TryGetValue("X-Inkstone-Account", out var expected)
        && context.User.Identity?.IsAuthenticated == true
        && !string.Equals(expected.ToString(), context.User.Identity.Name, StringComparison.OrdinalIgnoreCase))
    {
        await Results.Problem("The signed-in account changed. Sign in to the original account before saving this document.", statusCode: 403,
            extensions: new Dictionary<string, object?> { ["code"] = "account_changed" }).ExecuteAsync(context);
        return;
    }
    await next(context);
});
app.UseRateLimiter();
// Set the transport allowance before form/CSRF binding; file bytes are checked separately in the handler.
app.Use(async (context, next) =>
{
    if (context.GetEndpoint()?.Metadata.GetMetadata<BookmarkValuesRequest>() is not null || context.GetEndpoint()?.Metadata.GetMetadata<LetterEditorRequest>() is not null)
    {
        var maxBookmarkRequestBytes = context.GetEndpoint()?.Metadata.GetMetadata<LetterEditorRequest>() is not null ? LetterEditorEndpoints.MaxEditorBytes + FileLimits.MiB : 4L * FileLimits.MiB;
        if (context.Request.ContentLength > maxBookmarkRequestBytes)
        {
            await Results.Problem("The request exceeds this endpoint's body limit.", statusCode: 413).ExecuteAsync(context);
            return;
        }
        var bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = maxBookmarkRequestBytes;
    }
    if (context.GetEndpoint()?.Metadata.GetMetadata<UploadPolicyRequest>() is not null)
    {
        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        var policy = await FileLimits.ReadAsync(db, context.RequestAborted);
        var maxFileBytes = (long)policy.MaxUploadMiB * FileLimits.MiB;
        context.Items["UploadLimitBytes"] = maxFileBytes;
        var allowance = maxFileBytes + FileLimits.MiB;
        if (context.Request.ContentLength > allowance)
        {
            await Results.Problem("The upload exceeds the current size limit.", statusCode: 413, extensions: new Dictionary<string, object?> { ["code"] = "upload_too_large", ["maxBytes"] = maxFileBytes }).ExecuteAsync(context);
            return;
        }
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = allowance;
    }
    await next(context);
});
// Validate every API mutation, including login/logout; raw JSON endpoints do not get automatic validation.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !context.User.HasClaim(c => c.Type == AutomationTokens.Claim))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { await Results.Problem("Refresh the page and try again.", statusCode: 400).ExecuteAsync(context); return; }
    }
    await next(context);
});
app.UseAntiforgery();
app.Use(async (context, next) =>
{
    // Header-authenticated machine requests do not rely on ambient browser cookies.
    // Cookie requests still pass both explicit CSRF validation and form endpoint checks.
    if (context.User.HasClaim(c => c.Type == AutomationTokens.Claim))
        context.Features.Set<Microsoft.AspNetCore.Antiforgery.IAntiforgeryValidationFeature>(new TokenAntiforgeryFeature());
    await next(context);
});
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "Inkstone API v1");
        o.DocumentTitle = "Inkstone API";
        o.UseRequestInterceptor("function (request) { request.credentials = 'same-origin'; if (['GET', 'HEAD'].includes((request.method || 'GET').toUpperCase())) return request; return fetch('/api/csrf', { credentials: 'same-origin' }).then(function (response) { if (!response.ok) throw new Error('Cannot obtain CSRF token'); return response.json(); }).then(function (data) { request.headers['X-CSRF-TOKEN'] = data.token; return request; }); }");
    });
}
app.MapGet("/api/health", () => TypedResults.Ok(new HealthResponse("ok"))).WithTags("Session");
app.MapGet("/api/ready", async (AppDbContext db, CancellationToken ct) =>
{
    var executable = builder.Configuration["Rust:Executable"] ?? Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "inkstone-cli.exe" : "inkstone-cli");
    try { return await db.Database.CanConnectAsync(ct) && File.Exists(executable) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503); }
    catch (Exception) when (!ct.IsCancellationRequested) { return Results.StatusCode(503); }
}).WithTags("Session");
app.MapGet("/api/csrf", (HttpContext c, IAntiforgery a) => TypedResults.Ok(new CsrfResponse(a.GetAndStoreTokens(c).RequestToken!))).WithTags("Session").WithSummary("Get a CSRF token for the current session");
var accounts = app.MapGroup("/api/account").WithTags("Accounts");
accounts.MapGet("/me", async (ClaimsPrincipal principal, UserManager<IdentityUser> users) =>
{
    var user = await users.GetUserAsync(principal);
    return user is null ? Results.Unauthorized() : Results.Ok(await AccessRoles.AccountAsync(user, users));
}).RequireAuthorization().Produces<AccountResponse>();
accounts.MapPost("/register", async (Credentials input, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) =>
{
    if (!builder.Configuration.GetValue("Accounts:AllowRegistration", true)) return Results.Problem("Registration is disabled by the operator.", statusCode: 403);
    if (input.Email is null || input.Email.Length > 254 || !new EmailAddressAttribute().IsValid(input.Email) || input.Password is null || input.Password.Length > 128)
        return Results.Problem("Enter a valid email and a password of 10–128 characters.", statusCode: 400);
    var user = new IdentityUser { UserName = input.Email.Trim(), Email = input.Email.Trim() };
    var result = await users.CreateAsync(user, input.Password);
    if (!result.Succeeded) return Results.ValidationProblem(result.Errors.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
    await signIn.SignInAsync(user, isPersistent: true);
    return Results.Ok(await AccessRoles.AccountAsync(user, users));
}).RequireRateLimiting("accounts").Produces<AccountResponse>().ProducesValidationProblem();
accounts.MapPost("/login", async (Credentials input, SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users) =>
{
    if (input.Email is null || input.Password is null || input.Email.Length > 254 || input.Password.Length > 128)
        return Results.Problem("Email or password is incorrect.", statusCode: 401);
    var result = await signIn.PasswordSignInAsync(input.Email.Trim(), input.Password, isPersistent: true, lockoutOnFailure: true);
    if (!result.Succeeded) return Results.Problem(result.IsLockedOut ? "Too many attempts. Try again in 15 minutes." : "Email or password is incorrect.", statusCode: 401);
    var user = await users.FindByEmailAsync(input.Email.Trim());
    return Results.Ok(await AccessRoles.AccountAsync(user!, users));
}).RequireRateLimiting("accounts").Produces<AccountResponse>().ProducesProblem(401);
accounts.MapPost("/logout", async (SignInManager<IdentityUser> signIn) => { await signIn.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization().RequireRateLimiting("accounts");

var documents = app.MapGroup("/api/documents").RequireAuthorization().WithTags("Documents");
documents.MapBookmarks();
documents.MapGet("/formats", async (AppDbContext db, CancellationToken ct) => Results.Ok(new { upload = DocumentConverter.ImportFormats, download = DocumentConverter.ExportFormats, languages = DocumentConverter.Languages, maxUploadBytes = (long)(await FileLimits.ReadAsync(db, ct)).MaxUploadMiB * FileLimits.MiB, maxDocumentBytes = FileLimits.MaxModelBytes }))
    .WithSummary("Supported file formats, document languages, and upload limit");
documents.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
    Results.Ok(await db.Documents.AsNoTracking().Where(d => d.OwnerId == Owner(user)).OrderByDescending(d => d.UpdatedAt)
        .Select(d => new DocumentMetadata(d.Id, d.Title, d.Version, d.CreatedAt, d.UpdatedAt, d.Content.Length)).ToListAsync())).Produces<List<DocumentMetadata>>();
documents.MapPost("/", async (DocumentTitle input, ClaimsPrincipal user, AppDbContext db, DocumentConverter converter, CancellationToken ct) =>
{
    var title = CleanTitle(input.Title);
    if (title is null) return Results.Problem("A title must contain 1–160 characters.", statusCode: 400);
    var document = new StoredDocument { OwnerId = Owner(user), Title = title };
    if (input.Language is not null) document.Content = await converter.CreateAsync(input.Language, ct);
    db.Documents.Add(document);
    await db.SaveChangesAsync();
    return Results.Created($"/api/documents/{document.Id}", Metadata(document));
}).Produces<DocumentMetadata>(201).ProducesProblem(400);

documents.MapPost("/upload", async ([FromForm] IFormFile file, [FromForm] string? title, ClaimsPrincipal user, AppDbContext db, DocumentConverter converter, HttpContext context, CancellationToken ct) =>
{
    var maxUploadBytes = (long)context.Items["UploadLimitBytes"]!;
    if (file.Length == 0 || file.Length > maxUploadBytes) return Results.Problem("Choose a non-empty file within the current upload limit.", statusCode: 413, extensions: new Dictionary<string, object?> { ["code"] = "upload_too_large", ["maxBytes"] = maxUploadBytes });
    var filename = file.FileName.Replace('\\', '/').Split('/').Last();
    var cleanTitle = CleanTitle(title ?? Path.GetFileNameWithoutExtension(filename));
    if (cleanTitle is null) return Results.Problem("A title must contain 1–160 characters.", statusCode: 400);
    var extension = Path.GetExtension(filename).TrimStart('.').ToLowerInvariant();
    using var buffer = new MemoryStream();
    await file.CopyToAsync(buffer, ct);
    var content = await converter.ImportAsync(buffer.ToArray(), extension, maxUploadBytes, ct);
    var document = new StoredDocument { OwnerId = Owner(user), Title = cleanTitle, Content = content };
    db.Documents.Add(document);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/documents/{document.Id}", Metadata(document));
}).WithMetadata(new UploadPolicyRequest()).WithSummary("Upload a file into the current account")
  .WithDescription("Multipart fields: file (required), title (optional). Supports DOCX, ODT, RTF, TXT, Markdown and native JSON. The current administrator-configured upload limit is returned by /api/documents/formats. The Rust engine converts uploads into editable native documents.")
  .Produces<DocumentMetadata>(201).ProducesProblem(400).ProducesProblem(413).ProducesProblem(415).ProducesProblem(422).ProducesProblem(503);

documents.MapGet("/{id:guid}/download", async (Guid id, string? format, long? version, ClaimsPrincipal user, AppDbContext db, DocumentConverter converter, CancellationToken ct) =>
{
    var document = await DocumentAccess.Visible(db, user).AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
    if (document is null) return Results.NotFound();
    return await DocumentDownloads.CreateAsync(document, format, version, converter, ct);
}).WithSummary("Download the saved document as an attachment")
  .WithDescription("format: docx (default), pdf, txt, md or json. Optional version returns 409 if the saved document has changed. The owner or an explicitly shared reader/editor can download the current document.")
  .Produces(200, contentType: "application/octet-stream").ProducesProblem(409).ProducesProblem(415).ProducesProblem(422).ProducesProblem(503);
documents.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, AppDbContext db) =>
{
    var document = await DocumentAccess.Visible(db, user).AsNoTracking().SingleOrDefaultAsync(d => d.Id == id);
    return document is null ? Results.NotFound() : Results.Ok(Metadata(document));
}).Produces<DocumentMetadata>().Produces(404);
documents.MapGet("/{id:guid}/content", async (Guid id, ClaimsPrincipal user, AppDbContext db) =>
{
    var document = await DocumentAccess.Visible(db, user).AsNoTracking().SingleOrDefaultAsync(d => d.Id == id);
    if (document is null) return Results.NotFound();
    return document.Content.Length == 0 ? Results.NoContent() : Results.Bytes(document.Content, "application/json");
}).Produces<System.Text.Json.JsonElement>().Produces(204).Produces(404);
// Metadata and content come from one database read, so an opening tab cannot mix revisions.
documents.MapGet("/{id:guid}/snapshot", async (Guid id, ClaimsPrincipal user, AppDbContext db) =>
{
    var document = await DocumentAccess.Visible(db, user).AsNoTracking().SingleOrDefaultAsync(d => d.Id == id);
    if (document is null) return Results.NotFound();
    JsonElement? content = document.Content.Length == 0 ? null : JsonSerializer.Deserialize<JsonElement>(document.Content, new JsonSerializerOptions { MaxDepth = 256 });
    return Results.Ok(new DocumentSnapshot(Metadata(document), content));
}).Produces<DocumentSnapshot>().Produces(404);
documents.MapPut("/{id:guid}/content", async (Guid id, SaveDocument input, ClaimsPrincipal user, AppDbContext db, DocumentConverter converter, CancellationToken ct) =>
{
    var document = await DocumentAccess.FindAsync(db, user, id, ct, true);
    if (document is null) return Results.NotFound();
    if (document.Version != input.Version) return Conflict();
    if (input.Document.ValueKind != JsonValueKind.Object || !input.Document.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Array)
        return Results.Problem("The document format is invalid.", statusCode: 400);
    // Preserve the submitted UTF-8 model; escaping Persian letters would inflate its storage size.
    var bytes = Encoding.UTF8.GetBytes(input.Document.GetRawText());
    if (bytes.Length > FileLimits.MaxModelBytes) return Results.Problem("The editable document exceeds the 256 MiB processing limit.", statusCode: 413, extensions: new Dictionary<string, object?> { ["code"] = "model_too_large", ["maxBytes"] = FileLimits.MaxModelBytes });
    // Validate with the same native parser used by the editor before replacing
    // a usable revision. Keep the submitted model, including extension metadata.
    await converter.PreflightAsync(bytes, ct);
    document.Content = bytes;
    document.Version++;
    DocumentAccess.Audit(db, document, Owner(user), "edit");
    document.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateConcurrencyException) { return Conflict(); }
    return Results.Ok(Metadata(document));
}).Produces<DocumentMetadata>().ProducesProblem(409).ProducesProblem(400).ProducesProblem(413).Produces(404);
documents.MapPatch("/{id:guid}", async (Guid id, RenameDocument input, ClaimsPrincipal user, AppDbContext db) =>
{
    var title = CleanTitle(input.Title);
    if (title is null) return Results.Problem("A title must contain 1–160 characters.", statusCode: 400);
    var document = await DocumentAccess.FindAsync(db, user, id, CancellationToken.None, true);
    if (document is null) return Results.NotFound();
    if (document.Version != input.Version) return Conflict();
    document.Title = title;
    document.Version++;
    DocumentAccess.Audit(db, document, Owner(user), "edit");
    document.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    try { await db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { return Conflict(); }
    return Results.Ok(Metadata(document));
}).Produces<DocumentMetadata>().ProducesProblem(409).ProducesProblem(400).Produces(404);
documents.MapDelete("/{id:guid}", async (Guid id, long version, ClaimsPrincipal user, AppDbContext db) =>
{
    var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == id && d.OwnerId == Owner(user));
    if (document is null) return Results.NotFound();
    if (document.Version != version) return Conflict();
    document.Version++; document.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    db.TrashedDocuments.Add(new TrashedDocument { DocumentId = id });
    await db.SaveChangesAsync();
    return Results.NoContent();
}).Produces(204).ProducesProblem(409).Produces(404);
app.MapLibraryRecovery();
app.MapLibrarySearch();
app.MapRevisionRetention();
app.MapCollaboration();
app.MapAdministration();
app.MapTemplates();
app.MapLetterResources();
app.MapLetters();
app.MapLetterEditor();
app.MapDocumentAutomation();
app.MapAutomationTokens();
app.MapDocumentJobs();
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.Run();

static string? CleanTitle(string? title) => title?.Trim() is { Length: > 0 and <= 160 } value && !value.Any(char.IsControl) ? value : null;
static DocumentMetadata Metadata(StoredDocument d) => new(d.Id, d.Title, d.Version, d.CreatedAt, d.UpdatedAt, d.Content.Length);
static IResult Conflict() => Results.Problem("This document changed in another tab. Download your current work before reopening the saved copy.", statusCode: 409);

public sealed record HealthResponse(string Status);
public sealed record CsrfResponse(string Token);
public sealed record AccountResponse(string Email, bool IsAdmin = false, bool IsSuperAdmin = false);
public sealed record DocumentSnapshot(DocumentMetadata Metadata, JsonElement? Document);
public sealed record Credentials(string? Email, string? Password);
public sealed record DocumentTitle(string? Title, string? Language = null);
public sealed record DocumentMetadata(Guid Id, string Title, long Version, long CreatedAt, long UpdatedAt, int Size);
public sealed record RenameDocument(string? Title, long Version);
public sealed record SaveDocument(long Version, JsonElement Document);
public partial class Program;
public sealed class TokenAntiforgeryFeature : Microsoft.AspNetCore.Antiforgery.IAntiforgeryValidationFeature
{
    public bool IsValid => true;
    public Exception? Error => null;
}

static class Ownership
{
    public static string Owner(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;
}

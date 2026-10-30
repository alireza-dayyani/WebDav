using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Inkstone.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Inkstone.Web.Services;

public static class AccessRoles
{
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";
    public const string Policy = "Administrator";

    public static async Task<AccountResponse> AccountAsync(IdentityUser user, UserManager<IdentityUser> users)
    {
        var roles = await users.GetRolesAsync(user);
        return new AccountResponse(user.Email!, roles.Contains(Admin) || roles.Contains(SuperAdmin), roles.Contains(SuperAdmin));
    }
}

public sealed class AdministratorRequirement : IAuthorizationRequirement;

// Consult persisted roles on every privileged request: demotion takes effect for existing cookies.
public sealed class AdministratorHandler(UserManager<IdentityUser> users) : AuthorizationHandler<AdministratorRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdministratorRequirement requirement)
    {
        var user = await users.GetUserAsync(context.User);
        if (user is not null && (await users.IsInRoleAsync(user, AccessRoles.Admin) || await users.IsInRoleAsync(user, AccessRoles.SuperAdmin)))
            context.Succeed(requirement);
    }
}

public static class BootstrapAdministration
{
    public static async Task InitializeAsync(IServiceProvider services, string dataDirectory, IConfiguration configuration)
    {
        var db = services.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        await WorkspaceSchema.InitializeAsync(db);
        await AutomationSchema.InitializeAsync(db);
        // Add the policy table to existing installations without replacing their Identity/document database.
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS UploadPolicies (Id INTEGER NOT NULL PRIMARY KEY, MaxUploadMiB INTEGER NOT NULL, Version INTEGER NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("INSERT OR IGNORE INTO UploadPolicies (Id, MaxUploadMiB, Version) VALUES (1, 10, 1)");
        await SchemaUpgrades.InitializeAsync(db);
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { AccessRoles.Admin, AccessRoles.SuperAdmin })
            if (!await roleManager.RoleExistsAsync(role)) Check(await roleManager.CreateAsync(new IdentityRole(role)));

        var file = configuration["BootstrapAdmin:File"] ?? Path.Combine(dataDirectory, "bootstrap", "superadmin.json");
        if (!File.Exists(file)) return;
        var credentials = JsonSerializer.Deserialize<BootstrapCredentials>(await File.ReadAllTextAsync(file));
        if (credentials?.Email is not { Length: > 0 and <= 254 } email || !new EmailAddressAttribute().IsValid(email) || credentials.Password is not { Length: >= 10 and <= 128 })
            throw new InvalidOperationException("The private bootstrap account configuration is invalid.");
        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is not null)
        {
            if (!await users.IsInRoleAsync(user, AccessRoles.SuperAdmin))
                throw new InvalidOperationException("The configured bootstrap email already belongs to an ordinary account; refusing to elevate it automatically.");
            return; // Never reset the administrator password on restart.
        }
        user = new IdentityUser { UserName = email.Trim(), Email = email.Trim(), EmailConfirmed = true };
        // Only this private bootstrap path accepts the operator's initial password without the registration composition rules.
        user.PasswordHash = users.PasswordHasher.HashPassword(user, credentials.Password);
        Check(await users.CreateAsync(user));
        Check(await users.AddToRolesAsync(user, [AccessRoles.Admin, AccessRoles.SuperAdmin]));
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("Could not initialize administrator roles/account: " + string.Join(", ", result.Errors.Select(e => e.Code)));
    }

    private sealed record BootstrapCredentials(string Email, string Password);
}

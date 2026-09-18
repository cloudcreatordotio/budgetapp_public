using System.Globalization;
using System.Net;
using System.Security.Claims;
using AspNet.Security.OAuth.GitHub;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using BudgetApp.Components;
using BudgetApp.Data;
using BudgetApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// -- Configuration ------------------------------------------------------------
// Defaults so far: appsettings.json → appsettings.{Environment}.json → user-secrets
// (Development) → environment variables. Key Vault is appended only when KeyVault:Uri
// is non-empty; offline mode opts out by blanking it (user-secrets), containers by env var.
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUri),
        new DefaultAzureCredential(),
        new BudgetAppSecretManager());

    if (builder.Environment.IsDevelopment())
    {
        // Trap (configuration precedence): re-add user-secrets after Key Vault so local
        // overrides — e.g. the local GitHub OAuth secret — beat vault values in Development.
        builder.Configuration.AddUserSecrets<Program>(optional: true);
    }
}

// -- Services -----------------------------------------------------------------
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Trap (host header and public origin): behind nginx the app must honor
// X-Forwarded-Host/-Proto so OAuth redirect URIs use the public host. Only headers from
// senders in ForwardedHeaders:KnownProxies / :KnownNetworks are trusted; with both keys
// unset (local runs) nothing beyond loopback is trusted and the middleware is a no-op.
// KnownNetworks matters on App Service: the container's TCP peer is the platform
// frontend on a link-local address, not nginx, so trusting 10.0.0.10 alone would leave
// every X-Forwarded-* header ignored. ForwardLimit must cover the whole chain
// (App Service frontend, private-endpoint NAT, nginx) before the real client appears.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    var knownProxies = builder.Configuration["ForwardedHeaders:KnownProxies"] ?? "";
    foreach (var ip in knownProxies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        options.KnownProxies.Add(IPAddress.Parse(ip));
    }
    var knownNetworks = builder.Configuration["ForwardedHeaders:KnownNetworks"] ?? "";
    foreach (var cidr in knownNetworks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
    }
    if (int.TryParse(builder.Configuration["ForwardedHeaders:ForwardLimit"], out var forwardLimit))
    {
        options.ForwardLimit = forwardLimit;
    }
});

builder.Services.AddAuthentication(options =>
    {
        // Cookie is both the session and the challenge scheme: an unauthenticated request
        // redirects to /signin (the page), and only its POST explicitly challenges GitHub.
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "BudgetApp.Auth";
        options.LoginPath = "/signin";
        options.AccessDeniedPath = "/access-denied";

        // Defense in depth: a valid cookie must carry the internal user-id claim. Any cookie
        // without it (e.g. one left over from before provisioning existed) is rejected and
        // cleared, so the holder is treated as signed out rather than hitting a 500.
        options.Events.OnValidatePrincipal = async context =>
        {
            if (context.Principal?.FindFirst(AppClaims.UserId) is null)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    })
    .AddGitHub(options =>
    {
        // The remote handler probes its callback path on every request, which validates
        // these options — empty strings would 500 the whole app. The sentinel keeps the
        // app bootable (and /healthz honest) when OAuth isn't configured yet; sign-in
        // itself fails until real values exist, and startup logs a warning.
        static string NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? "unconfigured" : value;
        options.ClientId = NonEmpty(builder.Configuration["Authentication:GitHub:ClientId"]);
        options.ClientSecret = NonEmpty(builder.Configuration["Authentication:GitHub:ClientSecret"]);
        options.Scope.Add("read:user");
        options.Scope.Add("user:email");
        options.AccessDeniedPath = "/access-denied"; // user cancelled on GitHub's page

        // Allowlist + provisioning run in OnTicketReceived, NOT OnCreatingTicket: the OAuth
        // handler's CreateTicketAsync ignores a Fail() raised in OnCreatingTicket and signs
        // the cookie anyway (a rejected login would still get an authenticated cookie).
        // OnTicketReceived runs *before* SignInAsync, so HandleResponse() here actually blocks
        // the cookie. The principal already carries GitHub's claims (RunClaimActions ran).
        options.Events.OnTicketReceived = async context =>
        {
            var services = context.HttpContext.RequestServices;
            var identity = (ClaimsIdentity)context.Principal!.Identity!;
            var login = identity.FindFirst(ClaimTypes.Name)?.Value;

            var checker = new AllowlistChecker(
                services.GetRequiredService<IConfiguration>()["Authentication:GitHub:AllowedLogins"]);
            if (!checker.IsAllowed(login))
            {
                services.GetRequiredService<ILogger<Program>>()
                    .LogWarning("GitHub login '{Login}' is not on the allowlist — sign-in rejected, no cookie or user row created.", login);
                context.HandleResponse(); // stops the pipeline before SignInAsync → no cookie
                context.Response.Redirect("/access-denied?reason=allowlist");
                return;
            }

            var gitHubId = long.Parse(
                identity.FindFirst(ClaimTypes.NameIdentifier)!.Value, CultureInfo.InvariantCulture);
            var user = await services.GetRequiredService<UserProvisioner>().EnsureUserAsync(
                gitHubId,
                login!,
                identity.FindFirst(ClaimTypes.Email)?.Value,
                identity.FindFirst(GitHubAuthenticationConstants.Claims.Name)?.Value,
                context.HttpContext.RequestAborted);
            // Added to the principal that OnTicketReceived hands to SignInAsync, so it lands in the cookie.
            identity.AddClaim(new Claim(AppClaims.UserId, user.Id.ToString(CultureInfo.InvariantCulture)));
        };

        // Genuine remote failures only (e.g. the user cancels on GitHub's consent screen).
        options.Events.OnRemoteFailure = context =>
        {
            context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>()
                .LogWarning(context.Failure, "GitHub sign-in failed remotely.");
            context.Response.Redirect("/access-denied");
            context.HandleResponse();
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

var connectionString = builder.Configuration.GetConnectionString("BudgetApp")
    ?? throw new InvalidOperationException(
        "Connection string 'BudgetApp' is not configured. Connected mode: set KeyVault:Uri and connect the VPN. " +
        "Offline mode: dotnet user-secrets set \"ConnectionStrings:BudgetApp\" \"<local SQL connection string>\".");

// A factory, not AddDbContext: interactive server components must never share a scoped DbContext.
builder.Services.AddDbContextFactory<BudgetDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<BudgetDbContext>()
    .SetApplicationName("BudgetApp");

builder.Services.AddHealthChecks()
    .AddDbContextCheck<BudgetDbContext>();

builder.Services.AddScoped<ICurrentUserService, ClaimsCurrentUserService>();
builder.Services.AddScoped<UserProvisioner>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<SummaryService>();

var app = builder.Build();

// -- Startup diagnostics: name (never the value) of the resolved connection string
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BudgetApp.Startup");
startupLogger.LogInformation(
    string.IsNullOrWhiteSpace(keyVaultUri)
        ? "Key Vault provider not configured (KeyVault:Uri is empty) — local configuration only."
        : $"Key Vault configuration provider enabled for {keyVaultUri}.");
LogConnectionStringSource(app.Configuration, startupLogger);
startupLogger.LogInformation(
    string.IsNullOrWhiteSpace(app.Configuration["Authentication:GitHub:ClientId"])
        ? "GitHub OAuth is NOT configured (Authentication:GitHub:ClientId is empty) — sign-in will fail until the OAuth app hand-off completes."
        : $"GitHub OAuth configured; allowed logins: {app.Configuration["Authentication:GitHub:AllowedLogins"]}.");

// -- Database migration and optional sample seeding ---------------------------
if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: true))
{
    var dbFactory = app.Services.GetRequiredService<IDbContextFactory<BudgetDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();

    var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
    if (pending.Count > 0)
    {
        startupLogger.LogInformation("Applying {Count} pending migration(s): {Migrations}.",
            pending.Count, string.Join(", ", pending));
    }

    await db.Database.MigrateAsync();
    startupLogger.LogInformation("Database schema is up to date ({Count} migration(s) applied on this start).",
        pending.Count);

    if (app.Configuration.GetValue("Database:SeedSampleData", defaultValue: false))
    {
        startupLogger.LogInformation("Database:SeedSampleData=true — ensuring dev user and sample data exist.");
        await SeedData.SeedSampleDataAsync(db, startupLogger);
    }
    else
    {
        startupLogger.LogInformation("Sample-data seeding is disabled (Database:SeedSampleData=false).");
    }
}
else
{
    startupLogger.LogInformation("Database:MigrateOnStartup=false — skipping migrations.");
}

// -- Pipeline -----------------------------------------------------------------
// Trap: forwarded headers must be processed before authentication so OAuth redirects
// are built from the public host, not the internal one. First middleware in the pipeline.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery(); // must come after authentication/authorization

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Plain HTML form posts from the sign-in page and the layout's sign-out button.
// Both bind form data, which makes the antiforgery middleware validate the token.
app.MapPost("/auth/signin-github", ([FromForm] string? returnUrl) =>
        Results.Challenge(
            new AuthenticationProperties { RedirectUri = SafeLocalUrl(returnUrl) },
            [GitHubAuthenticationDefaults.AuthenticationScheme]))
    .AllowAnonymous();

app.MapPost("/auth/signout", async (HttpContext context, IFormCollection _) =>
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/signin");
    })
    .RequireAuthorization();

// Health probes are unauthenticated by design (App Service health check, nginx).
app.MapHealthChecks("/healthz").AllowAnonymous();

await app.RunAsync();
return;

// Open-redirect guard for the post-sign-in destination: only app-local paths pass.
static string SafeLocalUrl(string? url) =>
    !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\")
        ? url
        : "/";

static void LogConnectionStringSource(IConfiguration configuration, ILogger logger)
{
    const string key = "ConnectionStrings:BudgetApp";
    var provider = ((IConfigurationRoot)configuration).Providers
        .Reverse()
        .FirstOrDefault(p => p.TryGet(key, out _));

    if (provider is null)
    {
        logger.LogWarning("No configuration provider supplies '{Key}'.", key);
    }
    else if (provider.GetType().Name.Contains("KeyVault", StringComparison.OrdinalIgnoreCase))
    {
        logger.LogInformation(
            "Connection string 'BudgetApp' resolved from Key Vault secret 'ConnectionStrings--BudgetApp' ({Provider}).",
            provider);
    }
    else
    {
        logger.LogInformation("Connection string 'BudgetApp' resolved from {Provider}.", provider);
    }
}

/// <summary>
/// Restricts the Key Vault provider to BudgetApp's own secrets — the vault also holds
/// unrelated ones (e.g. ConnectionStrings--KeyvaultTest) that must never be loaded.
/// </summary>
internal sealed class BudgetAppSecretManager : KeyVaultSecretManager
{
    public override bool Load(SecretProperties secret) =>
        secret.Name.Equals("ConnectionStrings--BudgetApp", StringComparison.OrdinalIgnoreCase)
        || secret.Name.StartsWith("Authentication--", StringComparison.OrdinalIgnoreCase);
}

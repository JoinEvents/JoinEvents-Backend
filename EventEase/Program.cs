using EventEase.Api;
using EventEase.Api.Hubs;
using EventEase.Application.Auth;
using EventEase.Application.Blob;
using EventEase.Application.Checkout;
using EventEase.Application.Payments;
using EventEase.Application.Pricing;
using EventEase.Application.Services;
using EventEase.Application.Vendors;
using EventEase.Application.Loyalty;
using EventEase.Application.Tiers;
using EventEase.Core.Constants;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();

// ---------------------------------------------------------------------------
// Configuration: fail fast on anything missing rather than falling back to a
// default that would silently weaken security.
// ---------------------------------------------------------------------------

// [SECURITY] The signing key must be a real secret. A missing key, or an unexpanded
// "${VAR}" placeholder left over from the config template, would otherwise become the
// HMAC key — a value committed to the repository, letting anyone forge admin tokens.
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Contains("${") || jwtKey.Contains("dummy"))
{
    throw new InvalidOperationException(
        "[SECURITY] JWT signing key is not configured. Set the Jwt__Key environment variable " +
        "(or Jwt:Key in configuration) to a random secret of at least 32 characters.");
}
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "[SECURITY] JWT signing key is too short. HMAC-SHA256 requires at least 32 bytes of key material.");
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("${"))
{
    throw new InvalidOperationException(
        "[STARTUP] Database connection string is not configured. Set the " +
        "ConnectionStrings__DefaultConnection environment variable.");
}

var configuredOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
                        ?? Array.Empty<string>();
if (configuredOrigins.Length == 0)
{
    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException(
            "[SECURITY] No AllowedOrigins configured. Set the AllowedOrigins array so CORS " +
            "does not fall back to an unintended default.");
    }
    configuredOrigins = new[] { "http://localhost:4200" };
}

// The Capacitor mobile app's WebView is not served over the network, so it has no
// deployable origin to configure: Android serves the bundle from https://localhost and
// iOS from capacitor://localhost, and those are the Origin headers the backend sees.
// They are appended rather than left to configuration because a deployment that sets
// only AllowedOrigins__0 (as azure/provision.sh does) would otherwise silently break
// every request from the app at the CORS preflight, which in JavaScript is
// indistinguishable from the server being down.
//
// [SECURITY] These name the app's own WebView, not a site an attacker can serve from:
// a page on the public internet cannot claim either origin.
string[] mobileAppOrigins = { "https://localhost", "capacitor://localhost" };

var allowedOrigins = configuredOrigins
    .Concat(mobileAppOrigins)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

// ---------------------------------------------------------------------------
// Authentication / authorization
// ---------------------------------------------------------------------------

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "EventEase",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "EventEase",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            RoleClaimType = ClaimTypes.Role,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // Browsers cannot set an Authorization header on a WebSocket handshake, so SignalR
        // sends the token as a query string parameter. Without this the chat hub can never
        // authenticate a browser client.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

// Role checks accept either the standard role claim URI or the short "role" claim, since
// tokens issued by this API carry both.
static bool HasAnyRole(ClaimsPrincipal user, params string[] roles) =>
    user.HasClaim(c =>
        (c.Type == ClaimTypes.Role || c.Type == "role") &&
        roles.Any(r => c.Value.Equals(r, StringComparison.OrdinalIgnoreCase)));

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthPolicies.User, p => p.RequireAssertion(ctx =>
        HasAnyRole(ctx.User, AuthRoles.User, AuthRoles.Customer, AuthRoles.Admin, AuthRoles.Vendor, AuthRoles.Support)));
    options.AddPolicy(AuthPolicies.Vendor, p => p.RequireAssertion(ctx =>
        HasAnyRole(ctx.User, AuthRoles.Vendor, AuthRoles.Admin)));
    options.AddPolicy(AuthPolicies.Admin, p => p.RequireAssertion(ctx =>
        HasAnyRole(ctx.User, AuthRoles.Admin)));
    options.AddPolicy(AuthPolicies.SupportOrAdmin, p => p.RequireAssertion(ctx =>
        HasAnyRole(ctx.User, AuthRoles.Admin, AuthRoles.Support)));
});

// ---------------------------------------------------------------------------
// Rate limiting — unauthenticated auth endpoints get a far tighter budget.
// ---------------------------------------------------------------------------

static string ClientKey(HttpContext http) =>
    http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
    ?? http.Connection.RemoteIpAddress?.ToString()
    ?? "unknown";

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(RateLimitPolicies.Authentication, http =>
        RateLimitPartition.GetFixedWindowLimiter(ClientKey(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetFixedWindowLimiter(ClientKey(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many requests", message = "Please slow down and try again shortly." },
            cancellationToken: token);
    };
});

// ---------------------------------------------------------------------------
// MVC, Swagger, persistence, application services
// ---------------------------------------------------------------------------

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.CustomSchemaIds(type => type.FullName);
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Paste the JWT access token (without the \"Bearer \" prefix)."
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddDbContext<EventEaseDbContext>(o =>
  o.UseSqlServer(connectionString, sql =>
  {
      // Compatibility level 120 keeps OPENJSON-based translations off, which SQL Server 2014
      // cannot parse in Contains() queries.
      sql.UseCompatibilityLevel(120);
      sql.EnableRetryOnFailure(
          maxRetryCount: 5,
          maxRetryDelay: TimeSpan.FromSeconds(30),
          errorNumbersToAdd: null);
  }));

builder.Services.AddHttpClient();

builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IVendorService, VendorService>();
builder.Services.AddScoped<IVendorCalendarService, VendorCalendarService>();
builder.Services.AddScoped<IPricingEngine, SimplePricingEngine>();
builder.Services.AddScoped<IBookingPricingService, BookingPricingService>();
builder.Services.AddScoped<ICartService, CartService>();
// [SECURITY] There is no real payment integration in this codebase yet. Rather than let a
// production deployment quietly run on the stub, startup fails unless the operator opts in.
if (builder.Environment.IsProduction() && !builder.Configuration.GetValue("Payments:AllowSimulator", false))
{
    SimulatorGateway.ThrowIfProduction(isProduction: true);
}
builder.Services.AddSingleton<IPaymentGateway, SimulatorGateway>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IServices, Services>();
builder.Services.AddScoped<IPortalsService, PortalsService>();
builder.Services.AddScoped<IVendorDocumentService, VendorDocumentService>();
builder.Services.AddScoped<EventEase.Application.Chat.IMessengerService, EventEase.Application.Chat.MessengerService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<EventEase.Application.Categories.IEventCategoryService, EventEase.Application.Categories.EventCategoryService>();
builder.Services.AddScoped<EventEase.Application.SupportTicket.ISupportService, EventEase.Application.SupportTicket.SupportService>();
builder.Services.AddScoped<ILoyaltyService, LoyaltyService>();
builder.Services.AddScoped<ITierService, TierService>();
// --- Object storage -------------------------------------------------------------------
// Uploads (avatars, package galleries, verification documents, support attachments) go to
// Azure Storage. Container filesystems here are ephemeral and not shared between replicas,
// so anything written to local disk is lost on restart and invisible to the other instances.
//
// "Gcp" keeps the previous Google Cloud Storage path working for deployments that have not
// moved yet. Set Storage:Provider to pick.
builder.Services.Configure<AzureStorageOptions>(
    builder.Configuration.GetSection(AzureStorageOptions.SectionName));

var storageProvider = builder.Configuration["Storage:Provider"] ?? "Azure";

if (storageProvider.Equals("Gcp", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IBlobService, GcpBucketService>();
    builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
}
else
{
    // Fail at startup rather than on the first upload: an API that accepts pictures and
    // silently has nowhere to put them is worse than one that refuses to boot.
    var azureConfigured =
        !string.IsNullOrWhiteSpace(builder.Configuration["AzureStorage:ConnectionString"]) ||
        !string.IsNullOrWhiteSpace(builder.Configuration["AzureStorage:AccountName"]);

    if (!azureConfigured && builder.Environment.IsProduction())
    {
        throw new InvalidOperationException(
            "Storage:Provider is Azure but no account is configured. Set AzureStorage__ConnectionString, " +
            "or AzureStorage__AccountName to authenticate with the app's managed identity.");
    }

    // Outside Production the app still starts, and only the upload endpoints fail. For local
    // work, point AzureStorage__ConnectionString at Azurite with "UseDevelopmentStorage=true".

    builder.Services.AddSingleton<AzureBlobClientProvider>();
    builder.Services.AddSingleton<IBlobService, AzureBlobService>();
    builder.Services.AddSingleton<IFileStorage, AzureBlobFileStorage>();
}
builder.Services.AddSignalR();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" });

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// Cloud Run and most ingress proxies terminate TLS, so the client's real scheme and IP
// arrive in forwarded headers. Without this, HTTPS redirection and per-IP rate limiting
// both see the proxy instead of the caller.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// ---------------------------------------------------------------------------
// Pipeline. Order matters: the exception handler is first so it also covers the
// middleware below it, and the audit log runs after authentication so it can see
// who the caller is.
// ---------------------------------------------------------------------------

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();

        // The client is told nothing about the failure, so without a shared identifier a
        // reported 500 cannot be matched to the exception that caused it. TraceIdentifier
        // is already on every log line written for this request.
        var traceId = context.TraceIdentifier;
        Log.Error(feature?.Error, "Unhandled exception while processing {Path} (trace {TraceId})",
            context.Request.Path, traceId);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        // [SECURITY] Never expose internal exception details to clients — the trace id is an
        // opaque per-request handle, not a description of what went wrong.
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Internal Server Error",
            message = "An unexpected error occurred. Please try again later.",
            traceId
        });
    });
});

app.UseForwardedHeaders();
app.UseHttpsRedirection();

// [SECURITY] Baseline response headers.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.Append("X-Content-Type-Options", "nosniff");
    headers.Append("X-Frame-Options", "DENY");
    headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=()");
    headers.Append("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'");
    await next();
});

app.UseSerilogRequestLogging();
app.UseStaticFiles();

// Vendor documents written by LocalFileStorage are served from /files.
var storagePath = Path.Combine(builder.Environment.ContentRootPath, "storage");
Directory.CreateDirectory(storagePath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(storagePath),
    RequestPath = "/files"
});

app.UseRouting();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AuditMiddleware>();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHub<ChatHub>("/hubs/chat");
app.MapControllers();

// Liveness: the process is up. Readiness: dependencies are reachable.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    // The default writer emits the overall status and nothing else, so a probe that is
    // merely degraded is indistinguishable from a healthy one. Each check's description
    // says which dependency is in what state, which is what someone opening /health in a
    // browser actually needs. Descriptions are written for this audience and carry no
    // internal detail — see DatabaseHealthCheck.
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description
            })
        });
    }
}).AllowAnonymous();

// ---------------------------------------------------------------------------
// Schema and seed data.
//
// Migrations are opt-in: EF Core's Migrate() is not safe to run concurrently from
// several instances, so a multi-instance deployment should run it as a separate
// release step and leave Database:MigrateOnStartup false.
// ---------------------------------------------------------------------------

if (builder.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();

    var pending = db.Database.GetPendingMigrations().ToList();
    Log.Information("[Migration] {Count} pending migration(s).", pending.Count);
    db.Database.Migrate();
    Log.Information("[Migration] Database migration completed.");
}

try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();
    var seedNotes = DbInitializer.Seed(db, new SeedOptions
    {
        AdminEmail = builder.Configuration["Bootstrap:AdminEmail"],
        AdminPassword = builder.Configuration["Bootstrap:AdminPassword"],
        SupportEmail = builder.Configuration["Bootstrap:SupportEmail"],
        SupportPassword = builder.Configuration["Bootstrap:SupportPassword"],
        // [SECURITY] Demo fixtures use well-known credentials and are never seeded in production.
        SeedDemoData = !app.Environment.IsProduction()
                       && builder.Configuration.GetValue("Database:SeedDemoData", false)
    });

    // One line per step. "Reference data verified" said nothing about whether a staff
    // account had been created, skipped or had failed to be created, which left a login
    // that says invalid credentials with nowhere to look.
    foreach (var note in seedNotes)
    {
        Log.Information("[Seed] {Outcome}", note);
    }
}
catch (Exception ex)
{
    // A transient database problem should not stop the process from starting: the readiness
    // probe reports the dependency as down and the instance is kept out of rotation until it
    // recovers, which is more useful than a crash loop.
    Log.Error(ex, "[Seed] Seeding failed; continuing startup. The readiness probe will report database health.");
}

try
{
    Log.Information("Starting web host");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }

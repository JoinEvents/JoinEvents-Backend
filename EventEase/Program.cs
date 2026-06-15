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
using EventEase.Infrastructure;
using EventEase.Infrastructure.Data;
using EventEase.Infrastructure.Otp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();


// Add services to the container.
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer(options =>
    {
        // [SECURITY] JWT key must be configured — fail loudly if missing
        var jwtKey = builder.Configuration["Jwt:Key"];
        if (string.IsNullOrEmpty(jwtKey) || jwtKey.Contains("dummy"))
        {
            jwtKey = Environment.GetEnvironmentVariable("EVENT_EASE_JWT_KEY")
                     ?? throw new InvalidOperationException("[SECURITY] JWT signing key is not configured. Set 'Jwt:Key' in appsettings or EVENT_EASE_JWT_KEY environment variable.");
        }
        if (jwtKey.Contains("${"))
        {
            foreach (System.Collections.DictionaryEntry ev in Environment.GetEnvironmentVariables())
            {
                var key = ev.Key.ToString();
                var value = ev.Value?.ToString();
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                {
                    jwtKey = jwtKey.Replace($"${{{key}}}", value);
                    jwtKey = jwtKey.Replace($"${key}", value);
                }
            }
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true, // [SECURITY] Tokens must expire
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "EventEase",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "EventEase",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("User", p => p.RequireAssertion(context => {
        var claims = context.User.Claims.Select(c => $"{c.Type}={c.Value}").ToList();
        Serilog.Log.Information("[User Policy] Evaluating claims: {Claims}", string.Join(", ", claims));
        return context.User.HasClaim(c => (c.Type == System.Security.Claims.ClaimTypes.Role || c.Type == "role") && (c.Value.Equals("User", StringComparison.OrdinalIgnoreCase) || c.Value.Equals("Customer", StringComparison.OrdinalIgnoreCase) || c.Value.Equals("Admin", StringComparison.OrdinalIgnoreCase) || c.Value.Equals("Vendor", StringComparison.OrdinalIgnoreCase) || c.Value.Equals("Support", StringComparison.OrdinalIgnoreCase)));
    }));
    options.AddPolicy("Vendor", p => p.RequireAssertion(context => context.User.HasClaim(c => (c.Type == System.Security.Claims.ClaimTypes.Role || c.Type == "role") && (c.Value.Equals("Vendor", StringComparison.OrdinalIgnoreCase) || c.Value.Equals("Admin", StringComparison.OrdinalIgnoreCase)))));
    options.AddPolicy("Admin", p => p.RequireAssertion(context => context.User.HasClaim(c => (c.Type == System.Security.Claims.ClaimTypes.Role || c.Type == "role") && c.Value.Equals("Admin", StringComparison.OrdinalIgnoreCase))));
});
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.CustomSchemaIds(type => type.FullName);
});

var envJwtKey = Environment.GetEnvironmentVariable("EVENT_EASE_JWT_KEY");
if (!string.IsNullOrEmpty(envJwtKey))
{
    builder.Configuration["Jwt:Key"] = envJwtKey;
}
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

// --- SEAMLESS CONNECTIVITY LOGIC ---
// 1. Try standard .NET Environment Variable: ConnectionStrings__DefaultConnection
// 2. Try your specific Secret variable: EVENT_EASE_DB_CONNECTION
// 3. Fallback to appsettings.json
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrEmpty(connectionString) || connectionString.Contains("$"))
{
    connectionString = Environment.GetEnvironmentVariable("EVENT_EASE_DB_CONNECTION");
}

if (string.IsNullOrEmpty(connectionString))
{
    Console.WriteLine("[Critical] Database connection string is missing!");
}
else
{
    // Securely log the connection string (masking the password) to prove it's being read
    var maskedConnectionString = System.Text.RegularExpressions.Regex.Replace(
        connectionString,
        @"Password=[^;]+",
        "Password=*****");
    Console.WriteLine($"[Startup] Successfully loaded Connection String: {maskedConnectionString}");
}

// ✅ FIXED: Configured UseCompatibilityLevel(120) to support SQL Server 2014 compatibility mode and prevent 'Incorrect syntax near WITH' (OPENJSON) errors on Contains queries
builder.Services.AddDbContext<EventEaseDbContext>(o =>
  o.UseSqlServer(connectionString, sql => {
      sql.UseCompatibilityLevel(120);
      sql.EnableRetryOnFailure(
          maxRetryCount: 5,
          maxRetryDelay: TimeSpan.FromSeconds(30),
          errorNumbersToAdd: null);
  }));
// -----------------------------------
//builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(builder.Configuration["Redis:Connection"]));
//builder.Services.AddScoped<IOtpService, RedisOtpService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IVendorService, VendorService>();
builder.Services.AddScoped<IVendorCalendarService, VendorCalendarService>();
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<IPricingEngine, SimplePricingEngine>();
builder.Services.AddScoped<ICartService, CartService>();
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
builder.Services.AddSingleton<IBlobService, GcpBucketService>();
builder.Services.AddSignalR();
//builder.Services.AddStackExchangeRedisCache(options =>
//{
//    options.Configuration = builder.Configuration.GetConnectionString("Redis");
//    options.InstanceName = "EventEase_";
//});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
    });


// [SECURITY] Restrict CORS to known frontend origins only
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy =>
        {
            var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
                ?? new[] { "https://joinevents.com", "https://www.joinevents.com", "http://localhost:4200" };
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
});

var app = builder.Build();

// Ensure verification columns exist in Packages table and notification columns exist in Users table
try
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();
        
        // Subscription columns in Vendors
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Vendors]') AND name = 'SubscriptionTier')
            BEGIN
                ALTER TABLE [dbo].[Vendors] ADD [SubscriptionTier] NVARCHAR(50) NOT NULL DEFAULT 'free';
                ALTER TABLE [dbo].[Vendors] ADD [SubscriptionBadge] NVARCHAR(100) NULL;
                ALTER TABLE [dbo].[Vendors] ADD [SubscriptionExpiry] DATETIME2 NULL;
            END");

        // Platform fee, Escrow, and Guarantee columns in Bookings
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Bookings]') AND name = 'PlatformFeeRate')
            BEGIN
                ALTER TABLE [dbo].[Bookings] ADD [PlatformFeeRate] DECIMAL(18,4) NOT NULL DEFAULT 0;
                ALTER TABLE [dbo].[Bookings] ADD [PlatformFeeAmount] DECIMAL(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE [dbo].[Bookings] ADD [TdsDeducted] DECIMAL(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE [dbo].[Bookings] ADD [VendorPayoutAmount] DECIMAL(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE [dbo].[Bookings] ADD [EscrowStatus] NVARCHAR(50) NOT NULL DEFAULT 'held';
                ALTER TABLE [dbo].[Bookings] ADD [GuaranteeStatus] NVARCHAR(50) NOT NULL DEFAULT 'active';
                ALTER TABLE [dbo].[Bookings] ADD [VendorConfirmedAt] DATETIME2 NULL;
                ALTER TABLE [dbo].[Bookings] ADD [VendorConfirmationDue] DATETIME2 NULL;
            END");

        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT * FROM sys.columns 
                WHERE object_id = OBJECT_ID(N'[dbo].[Packages]') 
                AND name = 'VerificationStatus'
            )
            BEGIN
                ALTER TABLE [dbo].[Packages] ADD [VerificationStatus] NVARCHAR(MAX) NOT NULL DEFAULT 'Pending';
            END");

        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT * FROM sys.columns 
                WHERE object_id = OBJECT_ID(N'[dbo].[Packages]') 
                AND name = 'VerificationComment'
            )
            BEGIN
                ALTER TABLE [dbo].[Packages] ADD [VerificationComment] NVARCHAR(MAX) NULL;
            END");

        // Add EmailNotifications, InAppNotifications, SmsNotifications to Users
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT * FROM sys.columns 
                WHERE object_id = OBJECT_ID(N'[dbo].[Users]') 
                AND name = 'EmailNotifications'
            )
            BEGIN
                ALTER TABLE [dbo].[Users] ADD [EmailNotifications] BIT NOT NULL DEFAULT 1;
            END");

        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT * FROM sys.columns 
                WHERE object_id = OBJECT_ID(N'[dbo].[Users]') 
                AND name = 'InAppNotifications'
            )
            BEGIN
                ALTER TABLE [dbo].[Users] ADD [InAppNotifications] BIT NOT NULL DEFAULT 1;
            END");

        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT * FROM sys.columns 
                WHERE object_id = OBJECT_ID(N'[dbo].[Users]') 
                AND name = 'SmsNotifications'
            )
            BEGIN
                ALTER TABLE [dbo].[Users] ADD [SmsNotifications] BIT NOT NULL DEFAULT 0;
            END");

        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT * FROM sys.columns 
                WHERE object_id = OBJECT_ID(N'[dbo].[Users]') 
                AND name = 'Avatar'
            )
            BEGIN
                ALTER TABLE [dbo].[Users] ADD [Avatar] NVARCHAR(MAX) NULL;
            END");

        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT * FROM sys.columns 
                WHERE object_id = OBJECT_ID(N'[dbo].[SupportTickets]') 
                AND name = 'AttachmentUrl'
            )
            BEGIN
                ALTER TABLE [dbo].[SupportTickets] ADD [AttachmentUrl] NVARCHAR(MAX) NULL;
            END");

        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT * FROM sys.columns 
                WHERE object_id = OBJECT_ID(N'[dbo].[SupportTickets]') 
                AND name = 'BookingId'
            )
            BEGIN
                ALTER TABLE [dbo].[SupportTickets] ADD [BookingId] UNIQUEIDENTIFIER NULL;
            END");

        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT * FROM sys.columns 
                WHERE object_id = OBJECT_ID(N'[dbo].[SupportTickets]') 
                AND name = 'Priority'
            )
            BEGIN
                ALTER TABLE [dbo].[SupportTickets] ADD [Priority] NVARCHAR(50) NOT NULL DEFAULT 'Medium';
            END");

        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT * FROM sys.columns 
                WHERE object_id = OBJECT_ID(N'[dbo].[ChatMessages]') 
                AND name = 'IsInternal'
            )
            BEGIN
                ALTER TABLE [dbo].[ChatMessages] ADD [IsInternal] BIT NOT NULL DEFAULT 0;
            END");
            
        // Repair any broken ChatThreads that store VendorId instead of UserId
        db.Database.ExecuteSqlRaw(@"
            IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ChatThreads' AND type = 'U')
            BEGIN
                UPDATE ChatThreads
                SET VendorId = v.UserId
                FROM ChatThreads t
                JOIN Vendors v ON t.VendorId = v.Id
                WHERE t.VendorId NOT IN (SELECT Id FROM Users);
            END");
            
        Console.WriteLine("[Startup DB Schema Check] Package verification status, User notifications, Avatar, AttachmentUrl, BookingId, Priority, and IsInternal columns verified/added successfully.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[Startup DB Schema Check Error] Failed to update schema: {ex.Message}");
}

app.UseCors("AllowAll");

// [SECURITY] Add security headers to all responses
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=()");
    await next();
});
app.UseSerilogRequestLogging();
app.UseStaticFiles();

var storagePath = Path.Combine(builder.Environment.ContentRootPath, "storage");
if (!Directory.Exists(storagePath))
{
    Directory.CreateDirectory(storagePath);
}
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(storagePath),
    RequestPath = "/files"
});

try
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();
        
        // --- DIAGNOSTICS FOR MIGRATION ---
        var pendingMigrations = db.Database.GetPendingMigrations().ToList();
        var appliedMigrations = db.Database.GetAppliedMigrations().ToList();
        
        Log.Information($"[Migration Diagnostics] Found {appliedMigrations.Count} applied migrations.");
        Log.Information($"[Migration Diagnostics] Found {pendingMigrations.Count} pending migrations.");
        
        if (pendingMigrations.Any())
        {
            Log.Information($"[Migration] First pending migration is: {pendingMigrations.First()}");
        }
        else
        {
            Log.Warning("[Migration] No pending migrations found! EF Core thinks the database is fully up to date.");
        }
        // ---------------------------------

        Log.Information("[Migration] Starting database migration...");
        db.Database.Migrate();
        Log.Information("[Migration] Database migration completed successfully.");
        DbInitializer.Seed(db);
        Log.Information("[Migration] Database seeding completed successfully.");
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "[Migration] Database Migration Failed — {Message}", ex.Message);
    throw; // ← crash visibly so Cloud Run logs show the real error
}

app.MapHub<ChatHub>("/hubs/chat");
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseMiddleware<AuditMiddleware>();
// [SECURITY] Always enforce HTTPS redirection
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
// Health Check
app.MapGet("/health", async (EventEaseDbContext db) => {
    try {
        await db.Database.CanConnectAsync();
        return Results.Ok(new { status = "Healthy", database = "Connected" });
    } catch (Exception ex) {
        return Results.Problem($"Database Unreachable: {ex.Message}");
    }
});

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionHandlerPathFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        var exception = exceptionHandlerPathFeature?.Error;

        Log.Error(exception, "Unhandled exception occurred while processing the request");

        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";

        // [SECURITY] Never expose internal exception details to clients
        await context.Response.WriteAsJsonAsync(new {
            error = "Internal Server Error",
            message = "An unexpected error occurred. Please try again later."
        });
    });
});

app.MapControllers();

try
{
    Log.Information("Starting web host");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
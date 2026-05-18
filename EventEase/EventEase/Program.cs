using EventEase.Api;
using EventEase.Api.Hubs;
using EventEase.Application.Auth;
using EventEase.Application.Blob;
using EventEase.Application.Checkout;
using EventEase.Application.Payments;
using EventEase.Application.Pricing;
using EventEase.Application.Services;
using EventEase.Application.Vendors;
using EventEase.Infrastructure;
using EventEase.Infrastructure.Data;
using EventEase.Infrastructure.Otp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Text;
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
        var jwtKey = builder.Configuration["Jwt:Key"] ?? "a_very_long_dummy_key_for_startup_purposes_only_12345";
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
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "EventEase",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "EventEase",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("User", p => p.RequireAssertion(context => context.User.HasClaim(c => (c.Type == System.Security.Claims.ClaimTypes.Role || c.Type == "role") && (c.Value.Equals("User", StringComparison.OrdinalIgnoreCase) || c.Value.Equals("Admin", StringComparison.OrdinalIgnoreCase) || c.Value.Equals("Vendor", StringComparison.OrdinalIgnoreCase)))));
    options.AddPolicy("Vendor", p => p.RequireAssertion(context => context.User.HasClaim(c => (c.Type == System.Security.Claims.ClaimTypes.Role || c.Type == "role") && (c.Value.Equals("Vendor", StringComparison.OrdinalIgnoreCase) || c.Value.Equals("Admin", StringComparison.OrdinalIgnoreCase)))));
    options.AddPolicy("Admin", p => p.RequireAssertion(context => context.User.HasClaim(c => (c.Type == System.Security.Claims.ClaimTypes.Role || c.Type == "role") && c.Value.Equals("Admin", StringComparison.OrdinalIgnoreCase))));
});
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
    });


builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

var app = builder.Build();

app.UseCors("AllowAll");
app.UseSerilogRequestLogging();

//try
//{
//    using (var scope = app.Services.CreateScope())
//    {
//        var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();
        
//        // --- DIAGNOSTICS FOR MIGRATION ---
//        var pendingMigrations = db.Database.GetPendingMigrations().ToList();
//        var appliedMigrations = db.Database.GetAppliedMigrations().ToList();
        
//        Log.Information($"[Migration Diagnostics] Found {appliedMigrations.Count} applied migrations.");
//        Log.Information($"[Migration Diagnostics] Found {pendingMigrations.Count} pending migrations.");
        
//        if (pendingMigrations.Any())
//        {
//            Log.Information($"[Migration] First pending migration is: {pendingMigrations.First()}");
//        }
//        else
//        {
//            Log.Warning("[Migration] No pending migrations found! EF Core thinks the database is fully up to date.");
//        }
//        // ---------------------------------

//        Log.Information("[Migration] Starting database migration...");
//        db.Database.Migrate();
//        Log.Information("[Migration] Database migration completed successfully.");
//        DbInitializer.Seed(db);
//        Log.Information("[Migration] Database seeding completed successfully.");
//    }
//}
//catch (Exception ex)
//{
//    Log.Fatal(ex, "[Migration] Database Migration Failed — {Message}", ex.Message);
//    throw; // ← crash visibly so Cloud Run logs show the real error
//}

app.MapHub<ChatHub>("/hubs/chat");
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseMiddleware<AuditMiddleware>();
if (!app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}
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
        // Ensure CORS is present even on errors
        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");

        await context.Response.WriteAsJsonAsync(new {
            error = "Internal Server Error",
            details = exception?.Message
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
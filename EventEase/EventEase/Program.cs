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

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
Console.WriteLine($"[Startup] Initial Connection String from Config: {connectionString ?? "NULL"}");

if (!string.IsNullOrEmpty(connectionString) && (connectionString.Contains("${") || connectionString.Contains("$")))
{
    // Try expanding using standard Environment.ExpandEnvironmentVariables first
    // Note: This expects %VAR% on Windows but we are likely on Linux in Cloud Run
    
    foreach (System.Collections.DictionaryEntry ev in Environment.GetEnvironmentVariables())
    {
        var key = ev.Key.ToString();
        var value = ev.Value?.ToString();
        if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
        {
            // Handle both ${VAR} and $VAR formats
            connectionString = connectionString.Replace($"${{{key}}}", value);
            connectionString = connectionString.Replace($"${key}", value);
        }
    }
}

if (string.IsNullOrEmpty(connectionString) || connectionString.Contains("$"))
{
    Console.WriteLine("[Startup] WARNING: Connection string still contains placeholders or is empty!");
}
else
{
    Console.WriteLine("[Startup] Connection string expanded successfully (length: " + connectionString.Length + ")");
}

builder.Services.AddDbContext<EventEaseDbContext>(o =>
  o.UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(120)));
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

try
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();
        db.Database.Migrate();
        DbInitializer.Seed(db);
    }
}
catch (Exception ex)
{
    // Log the error but allow the app to start so Cloud Run doesn't kill the container
    Log.Error(ex, "Database Migration Failed");
}
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

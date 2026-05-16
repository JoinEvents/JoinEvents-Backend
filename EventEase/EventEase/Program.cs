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

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddAuthentication("Bearer")
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
          IssuerSigningKey = new SymmetricSecurityKey(
              Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? "a_very_long_dummy_key_for_startup_purposes_only_12345")),
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
builder.Services.AddDbContext<EventEaseDbContext>(o =>
  o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"), sql => sql.UseCompatibilityLevel(120)));
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
    Console.WriteLine($"Database Migration Failed: {ex.Message}");
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
app.MapControllers();

app.Run();

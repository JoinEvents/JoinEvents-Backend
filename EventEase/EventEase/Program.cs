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
          ValidIssuer = builder.Configuration["Jwt:Issuer"],
          ValidAudience = builder.Configuration["Jwt:Audience"],
          IssuerSigningKey = new SymmetricSecurityKey(
              Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])),
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
    options.AddPolicy("AllowDevEventPlanner",
        policy =>
        {
            policy.WithOrigins("http://localhost:4200", "http://localhost:8200/")  // Angular dev server URL
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials(); // optional if using cookies/auth
        });
});

var app = builder.Build();

app.UseCors("AllowDevEventPlanner");
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();
    db.Database.Migrate();
    DbInitializer.Seed(db);
}
app.MapHub<ChatHub>("/hubs/chat");
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseMiddleware<AuditMiddleware>();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

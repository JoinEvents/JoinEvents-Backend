using EventEase.Core.Constants;
using EventEase.Core.Entities;
using EventEase.Core.Enums;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;

namespace EventEase.Api
{
    // You may need to install the Microsoft.AspNetCore.Http.Abstractions package into your project
    public class AuditMiddleware
    {
        private readonly RequestDelegate _next;

        public AuditMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context, EventEaseDbContext db)
        {
            if (context.User.IsInRole(AuthRoles.Admin) && context.Request.Method != "GET")
            {
                var adminId = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                              ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                // [SECURITY] Guard against missing claims to prevent crash
                if (!string.IsNullOrEmpty(adminId))
                {
                    var name = context.User.FindFirstValue(ClaimTypes.Name) ?? "Admin User";
                    var log = new AuditLog
                    {
                        Id = Guid.NewGuid(),
                        Timestamp = DateTime.UtcNow,
                        ActorId = adminId,
                        ActorName = name,
                        ActorRole = AuthRoles.Admin.ToLowerInvariant(),
                        Action = $"{context.Request.Method} {context.Request.Path}",
                        Description = $"Admin {name} performed {context.Request.Method} action on {context.Request.Path}",
                        EntityType = "system",
                        EntityId = context.Request.Path.Value?.Split('/').LastOrDefault() ?? "system",
                        EntityName = context.Request.Path.Value ?? "System",
                        Severity = AuditSeverity.Info.ToString().ToLowerInvariant(),
                        MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { query = context.Request.QueryString.ToString() })
                    };
                    db.AuditLogs.Add(log);
                    await db.SaveChangesAsync();
                }
            }
            await _next(context);
        }
    }

    // Extension method used to add the middleware to the HTTP request pipeline.
    public static class AuditMiddlewareExtensions
    {
        public static IApplicationBuilder UseAuditMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<AuditMiddleware>();
        }
    }
}

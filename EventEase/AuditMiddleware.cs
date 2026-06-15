using EventEase.Core.Entities;
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
            if (context.User.IsInRole("Admin") && context.Request.Method != "GET")
            {
                var adminId = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
                // [SECURITY] Guard against missing claims to prevent crash
                if (!string.IsNullOrEmpty(adminId) && Guid.TryParse(adminId, out var parsedAdminId))
                {
                    var log = new AuditLog
                    {
                        Id = Guid.NewGuid(),
                        AdminId = parsedAdminId,
                        Action = $"{context.Request.Method} {context.Request.Path}",
                        Target = context.Request.QueryString.ToString()
                    };
                    db.Set<AuditLog>().Add(log);
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

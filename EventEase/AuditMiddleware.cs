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
    /// <summary>
    /// Records administrator write actions.
    ///
    /// Two things were wrong before: this middleware ran before UseAuthentication, so
    /// context.User was always anonymous and no audit row was ever written; and it wrote the
    /// row before invoking the rest of the pipeline, so it recorded attempts that went on to
    /// fail validation as though they had succeeded. It is now registered after authentication
    /// and logs once the response status is known.
    /// </summary>
    public class AuditMiddleware
    {
        private readonly RequestDelegate _next;

        public AuditMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context, EventEaseDbContext db)
        {
            var isAuditable = context.User.Identity?.IsAuthenticated == true
                              && context.User.IsInRole(AuthRoles.Admin)
                              && context.Request.Method != HttpMethods.Get;

            // The path is captured up front: endpoint routing may rewrite it downstream.
            var method = context.Request.Method;
            var path = context.Request.Path.Value ?? "/";
            var query = context.Request.QueryString.ToString();

            await _next(context);

            if (!isAuditable) return;

            // Only successful writes are recorded; a rejected request did not change anything.
            if (context.Response.StatusCode >= 400) return;

            var adminId = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                          ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(adminId)) return;

            var name = context.User.FindFirstValue(ClaimTypes.Name) ?? "Admin User";

            try
            {
                db.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow,
                    ActorId = adminId,
                    ActorName = name,
                    ActorRole = AuthRoles.Admin.ToLowerInvariant(),
                    Action = $"{method} {path}",
                    Description = $"Admin {name} performed {method} on {path}",
                    EntityType = "system",
                    EntityId = path.Split('/').LastOrDefault() ?? "system",
                    EntityName = path,
                    Severity = AuditSeverity.Info.ToString().ToLowerInvariant(),
                    MetadataJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        query,
                        status = context.Response.StatusCode
                    })
                });

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // The response has already been sent; a failed audit write must not surface as a
                // second error to the client, but it must be visible in the logs.
                Serilog.Log.Error(ex, "Failed to write audit log for {Method} {Path}", method, path);
            }
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

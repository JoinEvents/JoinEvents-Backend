using EventEase.Core.Constants;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1/admin/audit-logs")]
    [Authorize(Policy = AuthPolicies.Admin)]
    public class AdminAuditLogsController : ControllerBase
    {
        private readonly EventEaseDbContext _db;

        public AdminAuditLogsController(EventEaseDbContext db)
        {
            _db = db;
        }

        /// <summary>The audit trail, newest first.</summary>
        /// <remarks>
        /// Bounded. AuditMiddleware writes a row for every admin write, so this table only
        /// grows, and reading all of it to render a screen that shows the recent entries gets
        /// slower for as long as the system is used.
        /// </remarks>
        [HttpGet]
        public async Task<IActionResult> GetAuditLogs([FromQuery] int take = 200)
        {
            take = Math.Clamp(take, 1, 1000);

            var logs = await _db.AuditLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(take)
                .ToListAsync();

            var response = logs.Select(l => new
            {
                id = l.Id.ToString(),
                timestamp = l.Timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                actorId = l.ActorId,
                actorName = l.ActorName,
                actorRole = l.ActorRole.ToLower(),
                action = l.Action,
                description = l.Description,
                entityType = l.EntityType.ToLower(),
                entityId = l.EntityId,
                entityName = l.EntityName,
                severity = l.Severity.ToLower(),
                metadata = ParseMetadata(l.MetadataJson)
            }).ToList();

            return Ok(response);
        }

        /// <summary>
        /// Reads a row's metadata, or nothing at all if it will not parse.
        /// </summary>
        /// <remarks>
        /// Deserialising inside the projection meant one row holding something that is not
        /// JSON threw while the response was being built, and the whole audit trail answered
        /// 500 — the entries either side of it are perfectly readable and were lost with it.
        /// </remarks>
        private static object? ParseMetadata(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<object>(json);
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

        public class CreateAuditLogDto
        {
            public string ActorName { get; set; } = string.Empty;
            public string ActorRole { get; set; } = string.Empty;
            public string Action { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string EntityType { get; set; } = string.Empty;
            public string EntityId { get; set; } = string.Empty;
            public string EntityName { get; set; } = string.Empty;
            public string Severity { get; set; } = string.Empty;
            public object? Metadata { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> CreateAuditLog([FromBody] CreateAuditLogDto dto)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "system";
            
            var log = new AuditLog
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                ActorId = userId,
                ActorName = dto.ActorName,
                ActorRole = dto.ActorRole,
                Action = dto.Action,
                Description = dto.Description,
                EntityType = dto.EntityType,
                EntityId = dto.EntityId,
                EntityName = dto.EntityName,
                Severity = dto.Severity,
                MetadataJson = dto.Metadata != null 
                    ? System.Text.Json.JsonSerializer.Serialize(dto.Metadata) 
                    : null
            };

            _db.AuditLogs.Add(log);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                id = log.Id.ToString(),
                timestamp = log.Timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                actorId = log.ActorId,
                actorName = log.ActorName,
                actorRole = log.ActorRole.ToLower(),
                action = log.Action,
                description = log.Description,
                entityType = log.EntityType.ToLower(),
                entityId = log.EntityId,
                entityName = log.EntityName,
                severity = log.Severity.ToLower(),
                metadata = dto.Metadata
            });
        }
    }
}

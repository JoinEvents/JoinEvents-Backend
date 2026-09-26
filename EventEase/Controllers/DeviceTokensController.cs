using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EventEase.Core.Constants;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventEase.Api.Controllers
{
    /// <summary>
    /// The phones that should receive a user's push notifications. The app registers its Firebase
    /// token after sign-in (any role) and removes it on sign-out.
    /// </summary>
    [ApiController]
    [Authorize(Policy = AuthPolicies.User)]
    public class DeviceTokensController : ControllerBase
    {
        private readonly EventEaseDbContext _db;

        public DeviceTokensController(EventEaseDbContext db) => _db = db;

        public record DeviceTokenRequest(string? Token, string? Platform);

        private Guid GetUserId()
        {
            var val = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return Guid.TryParse(val, out var id) ? id : Guid.Empty;
        }

        [HttpPost("/api/v1/profile/device-token")]
        public async Task<IActionResult> Register([FromBody] DeviceTokenRequest request)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized();
            var token = request?.Token?.Trim();
            if (string.IsNullOrEmpty(token) || token.Length > 512) return BadRequest(new { error = "A device token is required." });
            var platform = request!.Platform?.Trim().ToLowerInvariant() == "ios" ? "ios" : "android";

            // An install's token follows whoever signed in on it last, so a shared phone never
            // shows the previous user's notifications.
            var existing = await _db.DeviceTokens.FirstOrDefaultAsync(t => t.Token == token);
            if (existing == null)
            {
                _db.DeviceTokens.Add(new DeviceToken { Id = Guid.NewGuid(), UserId = userId, Token = token, Platform = platform });
            }
            else
            {
                existing.UserId = userId;
                existing.Platform = platform;
                existing.LastSeenAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();
            return Ok(new { registered = true });
        }

        /// <summary>Called on sign-out: this phone stops receiving the user's notifications.</summary>
        [HttpDelete("/api/v1/profile/device-token")]
        public async Task<IActionResult> Unregister([FromBody] DeviceTokenRequest request)
        {
            var userId = GetUserId();
            var token = request?.Token?.Trim();
            if (string.IsNullOrEmpty(token)) return BadRequest(new { error = "A device token is required." });
            await _db.DeviceTokens.Where(t => t.Token == token && t.UserId == userId).ExecuteDeleteAsync();
            return NoContent();
        }
    }
}

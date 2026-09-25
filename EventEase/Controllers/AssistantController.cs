using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EventEase.Api.Assistant;
using EventEase.Core.Constants;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EventEase.Api.Controllers
{
    /// <summary>
    /// Roshi, the customer's event concierge. Answers from the signed-in customer's own bookings and
    /// rewards and the listed packages; uses Claude when a key is configured, the rules engine otherwise.
    /// </summary>
    [ApiController]
    [Route("api/v1/assistant")]
    [Authorize(Policy = AuthPolicies.User)]
    [EnableRateLimiting(RateLimitPolicies.Assistant)]
    public class AssistantController : ControllerBase
    {
        private readonly RoshiTools _tools;
        private readonly RoshiRules _rules;
        private readonly RoshiClaude _claude;
        private readonly EventEaseDbContext _db;

        public AssistantController(RoshiTools tools, RoshiRules rules, RoshiClaude claude, EventEaseDbContext db)
        {
            _tools = tools;
            _rules = rules;
            _claude = claude;
            _db = db;
        }

        private Guid GetUserId()
        {
            var val = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return Guid.TryParse(val, out var id) ? id : Guid.Empty;
        }

        private async Task<string> FirstNameAsync(Guid userId)
        {
            var name = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Name).FirstOrDefaultAsync();
            return (name ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        }

        /// <summary>Roshi's opening message: personal, from the customer's next booking.</summary>
        [HttpGet("welcome")]
        public async Task<IActionResult> Welcome()
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized();
            var reply = await _rules.WelcomeAsync(userId, await FirstNameAsync(userId));
            reply.PoweredBy = _claude.Enabled ? "claude" : "rules";
            return Ok(reply);
        }

        /// <summary>The conversation so far, oldest first; the last turn is the customer's new message.</summary>
        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] RoshiRequest request, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized();
            var turns = request?.Messages?.Where(m => !string.IsNullOrWhiteSpace(m?.Content)).ToList() ?? new();
            var last = turns.LastOrDefault();
            if (last == null || string.Equals(last.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = "Send the conversation ending with the customer's message." });
            if (last.Content!.Length > 2000)
                return BadRequest(new { error = "Please keep messages under 2,000 characters." });

            var firstName = await FirstNameAsync(userId);
            var reply = await _claude.AnswerAsync(_tools, userId, firstName, turns, ct)
                        ?? await _rules.AnswerAsync(userId, firstName, last.Content);
            return Ok(reply);
        }
    }
}

using EventEase.Application.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace EventEase.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly IMessengerService _messengerService;

        public ChatController(IMessengerService messengerService)
        {
            _messengerService = messengerService;
        }

        [HttpGet("threads")]
        public async Task<IActionResult> GetThreads()
        {
            var threads = await _messengerService.GetThreadsAsync(GetUserId());
            return Ok(threads);
        }

        [HttpGet("threads/{threadId}/alive")]
        public async Task<IActionResult> IsThreadAlive(Guid threadId)
        {
            if (!await _messengerService.IsParticipantAsync(threadId, GetUserId())) return Forbid();
            var isAlive = await _messengerService.IsChatSessionAliveAsync(threadId);
            return Ok(new { IsAlive = isAlive });
        }

        [HttpGet("threads/{threadId}/messages")]
        public async Task<IActionResult> GetMessages(Guid threadId)
        {
            // [SECURITY] Verify the current user is a participant in this thread
            if (!await _messengerService.IsParticipantAsync(threadId, GetUserId())) return Forbid();

            var messages = await _messengerService.GetMessagesAsync(threadId);
            return Ok(messages);
        }

        /// <summary>
        /// The caller's id. The sub claim is mapped to NameIdentifier by the JWT handler, so reading
        /// "sub" alone found nothing and Guid.Parse threw on every call.
        /// </summary>
        private Guid GetUserId()
        {
            var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }
    }
}

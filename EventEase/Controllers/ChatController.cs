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
            var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub));
            var threads = await _messengerService.GetThreadsAsync(userId);
            return Ok(threads);
        }

        [HttpGet("threads/{threadId}/alive")]
        public async Task<IActionResult> IsThreadAlive(Guid threadId)
        {
            var isAlive = await _messengerService.IsChatSessionAliveAsync(threadId);
            return Ok(new { IsAlive = isAlive });
        }

        [HttpGet("threads/{threadId}/messages")]
        public async Task<IActionResult> GetMessages(Guid threadId)
        {
            var messages = await _messengerService.GetMessagesAsync(threadId);
            return Ok(messages);
        }
    }
}

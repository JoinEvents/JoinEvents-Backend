using EventEase.Application.SupportTicket;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using static EventEase.Application.SupportTicket.Dtos;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("support")]
    public class SupportController : ControllerBase
    {
        private readonly ISupportService _service;
        public SupportController(ISupportService service) => _service = service;

        [Authorize(Policy = "User")]
        [HttpPost("ticket")]
        public async Task<IActionResult> Create([FromBody] CreateTicketDto dto)
        {
            var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub));
            var ticket = await _service.CreateAsync(userId, dto);
            return Ok(ticket);
        }

        [Authorize(Policy = "Admin")]
        [HttpGet("tickets")]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

        [Authorize(Policy = "Admin")]
        [HttpPut("ticket/{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTicketDto dto)
        {
            var ticket = await _service.UpdateStatusAsync(id, dto.Status);
            return ticket is null ? NotFound() : Ok(ticket);
        }
    }

}

using EventEase.Application.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using static EventEase.Application.Checkout.Dtos;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("cart")]
    public class CartController : ControllerBase
    {
        private readonly ICartService _cart;
        public CartController(ICartService cart) => _cart = cart;

        [Authorize(Policy = "User")]
        [HttpPost]
        public async Task<IActionResult> Post([FromBody] CartRequest req)
        {
            var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub));
            var preview = await _cart.PreviewAsync(userId, req);
            return Ok(preview);
        }

        [Authorize(Policy = "User")]
        [HttpGet("preview")]
        public IActionResult Preview() => BadRequest(new { message = "Use POST /cart with items to preview." });
    }
}

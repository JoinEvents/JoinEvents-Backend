using EventEase.Application.Auth;
using EventEase.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using static EventEase.Application.Auth.Dtos;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("auth")]
    public class AuthController : ControllerBase
    {
        //private readonly IOtpService _otpService;
        private readonly IAuthService _auth;
        public AuthController (IAuthService auth)
        {
            //_otpService = otpService;
            _auth = auth;
        }

        //[HttpPost("register")]
        //public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        //{
        //    var User = await _auth.RegisterAsync(dto);
        //    return Ok(new {  message = "OTP sent", user = User });
        //}

        //[HttpPost("verify")]
        //public async Task<IActionResult> Verify([FromBody] VerifyDto dto)
        //{
        //    var tokens = await _auth.VerifyAsync(dto);
        //    if (tokens is null) return Unauthorized(new { error = "Invalid OTP" });
        //    return Ok(tokens);
        //}

        [HttpPost("/api/v1/auth/register")]
        public async Task<IActionResult> RegisterWithPassword([FromBody] RegisterWithPasswordDto dto)
        {
            try
            {
                if (dto == null || string.IsNullOrEmpty(dto.email) || string.IsNullOrEmpty(dto.password))
                {
                    return BadRequest(new { error = "Email and Password are required." });
                }

                var tokens = await _auth.RegisterWithPasswordAsync(dto);
                return Ok(new { 
                    token = tokens.AccessToken, 
                    user = new { 
                        id = tokens.User.Id.ToString(), 
                        name = tokens.User.Name, 
                        email = tokens.User.Email, 
                        role = (tokens.User.Role ?? "Customer").ToLower() 
                    } 
                });
            }
            catch (InvalidOperationException ex)
            {
                Serilog.Log.Warning("Registration attempt failed (User exists): {Email}", dto?.email);
                return BadRequest(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                Serilog.Log.Warning("Registration attempt failed (Invalid data): {Message}", ex.Message);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Registration failed for email: {Email}", dto?.email);
                return StatusCode(500, new { error = "Registration failed", details = ex.Message });
            }
        }

        [HttpPost("/api/v1/auth/login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var tokens = await _auth.LoginAsync(dto);
            if (tokens is null) return Unauthorized(new { error = "Invalid credentials" });
            return Ok(new { token = tokens.AccessToken, user = new { id = tokens.User.Id.ToString(), name = tokens.User.Name, email = tokens.User.Email, role = tokens.User.Role.ToLower() } });
        }

        [Microsoft.AspNetCore.Authorization.Authorize]
        [HttpGet("/api/v1/profile")]
        public async Task<IActionResult> GetProfile()
        {
            var userId = GetUserId();
            var profile = await _auth.GetProfileAsync(userId);
            if (profile is null) return NotFound(new { error = "User not found" });
            return Ok(profile);
        }

        [Microsoft.AspNetCore.Authorization.Authorize]
        [HttpPut("/api/v1/profile/password")]
        public async Task<IActionResult> UpdatePassword([FromBody] dynamic body)
        {
            var userId = GetUserId();
            string currentPassword = body.currentPassword;
            string newPassword = body.newPassword;
            var ok = await _auth.UpdatePasswordAsync(userId, currentPassword, newPassword);
            if (!ok) return BadRequest(new { error = "Invalid current password" });
            return Ok(new { success = true, message = "Password updated successfully." });
        }

        private Guid GetUserId()
        {
            var val = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                      ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            return Guid.TryParse(val, out var guid) ? guid : Guid.Empty;
        }
    }
}

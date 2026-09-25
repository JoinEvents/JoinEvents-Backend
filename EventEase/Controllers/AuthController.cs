using EventEase.Application.Auth;
using EventEase.Application.Blob;
using EventEase.Core.Constants;
using EventEase.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using static EventEase.Application.Auth.Dtos;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _auth;
        private readonly IBlobService _blobs;

        public AuthController(IAuthService auth, IBlobService blobs)
        {
            _auth = auth;
            _blobs = blobs;
        }

        [EnableRateLimiting(RateLimitPolicies.Authentication)]
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
                return Ok(AuthResponse(tokens));
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
                // [SECURITY] The exception detail goes to the log, not to the caller.
                Serilog.Log.Error(ex, "Registration failed for email: {Email}", dto?.email);
                return StatusCode(500, new { error = "Registration failed. Please try again later." });
            }
        }

        [EnableRateLimiting(RateLimitPolicies.Authentication)]
        [HttpPost("/api/v1/auth/login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.email) || string.IsNullOrWhiteSpace(dto.password))
                return BadRequest(new { error = "Email and password are required." });

            var tokens = await _auth.LoginAsync(dto);
            if (tokens is null) return Unauthorized(new { error = "Invalid credentials" });
            return Ok(AuthResponse(tokens));
        }

        /// <summary>
        /// Exchanges a refresh token for a new pair. Refresh tokens were previously created and
        /// stored but never returned or redeemable, so sessions simply expired after an hour.
        /// </summary>
        [EnableRateLimiting(RateLimitPolicies.Authentication)]
        [HttpPost("/api/v1/auth/refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenDto dto)
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.refreshToken))
                return BadRequest(new { error = "A refresh token is required." });

            var tokens = await _auth.RefreshAsync(dto.refreshToken);
            if (tokens is null) return Unauthorized(new { error = "Invalid or expired refresh token" });
            return Ok(AuthResponse(tokens));
        }

        /// <summary>Revokes the supplied refresh token. Always reports success so it cannot be
        /// used to probe which tokens exist.</summary>
        [HttpPost("/api/v1/auth/logout")]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenDto dto)
        {
            if (dto is not null && !string.IsNullOrWhiteSpace(dto.refreshToken))
            {
                await _auth.LogoutAsync(dto.refreshToken);
            }
            return Ok(new { success = true });
        }

        /// <summary>Signs the current user out of every device.</summary>
        [Authorize]
        [HttpPost("/api/v1/auth/logout-all")]
        public async Task<IActionResult> LogoutAll()
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized(new { error = "Invalid token." });

            await _auth.RevokeAllAsync(userId);
            return Ok(new { success = true });
        }

        /// <summary>Shared shape for every endpoint that issues tokens.</summary>
        private static object AuthResponse(AuthTokens tokens) => new
        {
            token = tokens.AccessToken,
            refreshToken = tokens.RefreshToken,
            refreshTokenExpiresAt = tokens.RefreshExpires,
            user = new
            {
                id = tokens.User.Id.ToString(),
                name = tokens.User.Name,
                email = tokens.User.Email,
                role = (tokens.User.Role ?? "Customer").ToLower(),
                avatar = tokens.User.Avatar
            }
        };

        [EnableRateLimiting(RateLimitPolicies.Authentication)]
        [HttpPost("/api/v1/auth/social-login")]
        public async Task<IActionResult> SocialLogin([FromBody] SocialLoginDto dto)
        {
            try
            {
                if (dto == null || string.IsNullOrEmpty(dto.token) || string.IsNullOrEmpty(dto.provider))
                {
                    return BadRequest(new { error = "Token and provider are required." });
                }

                var tokens = await _auth.SocialLoginAsync(dto);
                return Ok(AuthResponse(tokens));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException)
            {
                return Unauthorized(new { error = "Invalid or expired social login token." });
            }
            catch (Exception ex)
            {
                // [SECURITY] The exception detail goes to the log, not to the caller.
                Serilog.Log.Error(ex, "Social login failed with provider {Provider}", dto?.provider);
                return StatusCode(500, new { error = "Social login failed. Please try again later." });
            }
        }

        [Authorize]
        [HttpGet("/api/v1/profile")]
        public async Task<IActionResult> GetProfile()
        {
            var userId = GetUserId();
            var profile = await _auth.GetProfileAsync(userId);
            if (profile is null) return NotFound(new { error = "User not found" });
            return Ok(profile);
        }

        [Authorize]
        [HttpPatch("/api/v1/profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
        {
            var userId = GetUserId();
            var profile = await _auth.UpdateProfileAsync(userId, dto);
            if (profile is null) return NotFound(new { error = "User not found" });
            return Ok(profile);
        }

        [Authorize]
        [HttpPut("/api/v1/profile/password")]
        public async Task<IActionResult> UpdatePassword([FromBody] UpdatePasswordDto dto)
        {
            var userId = GetUserId();
            var ok = await _auth.UpdatePasswordAsync(userId, dto.currentPassword, dto.newPassword);
            if (!ok) return BadRequest(new { error = "Invalid current password" });
            return Ok(new { success = true, message = "Password updated successfully." });
        }

        [Authorize]
        [HttpDelete("/api/v1/profile")]
        public async Task<IActionResult> DeleteAccount()
        {
            var userId = GetUserId();
            var ok = await _auth.DeleteAccountAsync(userId);
            if (!ok) return NotFound(new { error = "User not found" });
            return Ok(new { success = true, message = "Account deleted successfully." });
        }

        [Authorize]
        [HttpPost("/api/v1/profile/avatar")]
        public async Task<IActionResult> UploadAvatar(IFormFile file)
        {
            var validationError = await ImageUploadValidator.ValidateAsync(file, MaxAvatarBytes);
            if (validationError is not null)
            {
                return BadRequest(new { error = validationError });
            }

            try
            {
                var userId = GetUserId();
                if (userId == Guid.Empty) return Unauthorized(new { error = "Invalid token." });

                // Stored in object storage rather than the container's local disk: instances are
                // ephemeral and do not share a filesystem, so local uploads were lost on restart
                // and invisible to other instances.
                var blobName = await _blobs.UploadAsync(file, userId.ToString());

                // The URL rather than the blob name: Avatar is read back in a dozen places
                // (reviews, chat, support queues) that hand it straight to an <img>. That only
                // holds while the media container serves anonymous reads, which is why
                // AzureStorage:MediaPublicAccess defaults to true.
                var avatarUrl = await _blobs.GetUrlAsync(blobName);

                // The previous avatar is now unreferenced; drop it rather than pay to keep every
                // picture the user has ever set.
                var previous = await _auth.GetAvatarAsync(userId);

                await _auth.UpdateAvatarAsync(userId, avatarUrl);

                var previousBlob = _blobs.TryResolveBlobName(previous);
                if (previousBlob is not null && previousBlob != blobName)
                {
                    try
                    {
                        await _blobs.DeleteAsync(previousBlob);
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "Could not delete replaced avatar blob {Blob}", previousBlob);
                    }
                }

                return Ok(new { avatarUrl });
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to upload avatar");
                return StatusCode(500, new { error = "Failed to upload avatar." });
            }
        }

        private const long MaxAvatarBytes = 5 * 1024 * 1024;

        private Guid GetUserId()
        {
            var val = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                      ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            return Guid.TryParse(val, out var guid) ? guid : Guid.Empty;
        }
    }
}

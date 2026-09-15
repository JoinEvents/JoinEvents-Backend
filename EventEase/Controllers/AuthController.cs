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
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No image file provided." });
            }
            if (file.Length > MaxAvatarBytes)
            {
                return BadRequest(new { error = "File size exceeds the 5MB limit." });
            }

            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || !AllowedAvatarExtensions.Contains(ext))
            {
                return BadRequest(new { error = "Invalid file type. Only JPG, PNG, WebP, and GIF are allowed." });
            }
            if (!AllowedAvatarMimeTypes.Contains(file.ContentType?.ToLowerInvariant()))
            {
                return BadRequest(new { error = "Invalid file content type." });
            }

            // [SECURITY] The extension and declared content type are both attacker-controlled, so
            // the bytes themselves are checked before the file is stored and served back.
            if (!await HasImageMagicBytesAsync(file))
            {
                return BadRequest(new { error = "The uploaded file is not a valid image." });
            }

            try
            {
                var userId = GetUserId();
                if (userId == Guid.Empty) return Unauthorized(new { error = "Invalid token." });

                // Stored in object storage rather than the container's local disk: instances are
                // ephemeral and do not share a filesystem, so local uploads were lost on restart
                // and invisible to other instances.
                var avatarUrl = await _blobs.UploadAsync(file, userId.ToString());
                await _auth.UpdateAvatarAsync(userId, avatarUrl);

                return Ok(new { avatarUrl });
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to upload avatar");
                return StatusCode(500, new { error = "Failed to upload avatar." });
            }
        }

        private const long MaxAvatarBytes = 5 * 1024 * 1024;

        private static readonly string[] AllowedAvatarExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

        private static readonly string[] AllowedAvatarMimeTypes = { "image/jpeg", "image/png", "image/webp", "image/gif" };

        /// <summary>
        /// Confirms the leading bytes match JPEG, PNG, GIF or WebP before we accept the upload.
        /// </summary>
        private static async Task<bool> HasImageMagicBytesAsync(IFormFile file)
        {
            var header = new byte[12];
            await using var stream = file.OpenReadStream();

            var read = await stream.ReadAsync(header.AsMemory(0, header.Length));
            if (read < 12) return false;

            // JPEG: FF D8 FF
            if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return true;

            // PNG: 89 50 4E 47 0D 0A 1A 0A
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A) return true;

            // GIF: "GIF8"
            if (header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38) return true;

            // WebP: "RIFF" .... "WEBP"
            if (header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
                header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50) return true;

            return false;
        }

        private Guid GetUserId()
        {
            var val = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                      ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            return Guid.TryParse(val, out var guid) ? guid : Guid.Empty;
        }
    }
}

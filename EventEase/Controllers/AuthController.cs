using EventEase.Application.Auth;
using EventEase.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using static EventEase.Application.Auth.Dtos;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _auth;
        private readonly IWebHostEnvironment _env;

        public AuthController (IAuthService auth, IWebHostEnvironment env)
        {
            _auth = auth;
            _env = env;
        }

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
                        role = (tokens.User.Role ?? "Customer").ToLower(),
                        avatar = tokens.User.Avatar
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
            return Ok(new { token = tokens.AccessToken, user = new { id = tokens.User.Id.ToString(), name = tokens.User.Name, email = tokens.User.Email, role = tokens.User.Role.ToLower(), avatar = tokens.User.Avatar } });
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
        [HttpPatch("/api/v1/profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
        {
            var userId = GetUserId();
            var profile = await _auth.UpdateProfileAsync(userId, dto);
            if (profile is null) return NotFound(new { error = "User not found" });
            return Ok(profile);
        }

        [Microsoft.AspNetCore.Authorization.Authorize]
        [HttpPut("/api/v1/profile/password")]
        public async Task<IActionResult> UpdatePassword([FromBody] UpdatePasswordDto dto)
        {
            var userId = GetUserId();
            var ok = await _auth.UpdatePasswordAsync(userId, dto.currentPassword, dto.newPassword);
            if (!ok) return BadRequest(new { error = "Invalid current password" });
            return Ok(new { success = true, message = "Password updated successfully." });
        }

        [Microsoft.AspNetCore.Authorization.Authorize]
        [HttpDelete("/api/v1/profile")]
        public async Task<IActionResult> DeleteAccount()
        {
            var userId = GetUserId();
            var ok = await _auth.DeleteAccountAsync(userId);
            if (!ok) return NotFound(new { error = "User not found" });
            return Ok(new { success = true, message = "Account deleted successfully." });
        }

        [Microsoft.AspNetCore.Authorization.Authorize]
        [HttpPost("/api/v1/profile/avatar")]
        public async Task<IActionResult> UploadAvatar(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No image file provided." });
            }

            // [SECURITY] Validate file type and size for avatar uploads
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || !allowedExtensions.Contains(ext))
            {
                return BadRequest(new { error = "Invalid file type. Only JPG, PNG, WebP, and GIF are allowed." });
            }
            if (file.Length > 5 * 1024 * 1024) // 5MB max
            {
                return BadRequest(new { error = "File size exceeds the 5MB limit." });
            }
            var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/webp", "image/gif" };
            if (!allowedMimeTypes.Contains(file.ContentType?.ToLowerInvariant()))
            {
                return BadRequest(new { error = "Invalid file content type." });
            }

            try
            {
                var userId = GetUserId();
                var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
                var uploadsDir = Path.Combine(webRoot, "uploads", "profiles");
                if (!Directory.Exists(uploadsDir))
                {
                    Directory.CreateDirectory(uploadsDir);
                }

                // Clean existing avatars for this user to avoid conflicts (e.g. extension changes)
                var baseName = $"{userId}";
                foreach (var existingFile in Directory.GetFiles(uploadsDir, $"{baseName}.*"))
                {
                    System.IO.File.Delete(existingFile);
                }

                ext = Path.GetExtension(file.FileName);
                if (string.IsNullOrEmpty(ext)) ext = ".jpg"; // fallback

                var fileName = $"{userId}{ext}";
                var filePath = Path.Combine(uploadsDir, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var avatarUrl = $"{Request.Scheme}://{Request.Host}/uploads/profiles/{fileName}";
                await _auth.UpdateAvatarAsync(userId, avatarUrl);

                return Ok(new { avatarUrl });
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to upload avatar");
                return StatusCode(500, new { error = "Failed to upload avatar." });
            }
        }

        private Guid GetUserId()
        {
            var val = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                      ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            return Guid.TryParse(val, out var guid) ? guid : Guid.Empty;
        }
    }
}

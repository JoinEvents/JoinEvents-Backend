using EventEase.Application.Blob;
using EventEase.Core.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace EventEase.Api.Controllers
{
    [Authorize]
    [Route("api/v1/files")]
    public class UserController : Controller
    {
        private readonly IBlobService _blobService;

        public UserController(IBlobService blobService)
        {
            _blobService = blobService;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "File is empty" });

            // Previously User.Identity?.Name, which is not populated by these tokens — every
            // upload landed in a shared "guest" folder.
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized(new { error = "Invalid token." });

            var filePath = await _blobService.UploadAsync(file, userId.ToString());
            return Ok(new { FilePath = filePath });
        }

        /// <summary>
        /// Uploads an image and returns a URL that can be stored on a record and rendered
        /// directly — a vendor's portfolio shot, an inclusion photo.
        ///
        /// Distinct from /support/upload, which these went through before: that endpoint writes to
        /// the private documents container, where a link expires. Anything meant to stay on a
        /// record and be displayed belongs here.
        /// </summary>
        [HttpPost("images")]
        public async Task<IActionResult> UploadImage(IFormFile file)
        {
            var validationError = await ImageUploadValidator.ValidateAsync(file);
            if (validationError is not null)
                return BadRequest(new { error = validationError });

            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized(new { error = "Invalid token." });

            try
            {
                var blobName = await _blobService.UploadAsync(file, userId.ToString());
                var url = await _blobService.GetUrlAsync(blobName);
                return Ok(new { url, path = blobName });
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to upload image for user {UserId}", userId);
                return StatusCode(502, new { error = "Image storage is unavailable. Please try again." });
            }
        }

        [HttpGet("download")]
        public async Task<IActionResult> Download([FromQuery] string path)
        {
            // [SECURITY] Blob paths are "{userId}/{guid}{ext}". Without this check any
            // authenticated caller could read any other user's uploads by passing their path.
            if (!IsOwnedByCaller(path)) return Forbid();

            var stream = await _blobService.DownloadAsync(path);
            if (stream == null) return NotFound();

            return File(stream, "application/octet-stream", Path.GetFileName(path));
        }

        [HttpDelete("delete")]
        public async Task<IActionResult> Delete([FromQuery] string path)
        {
            // [SECURITY] Same ownership rule as Download — this could previously delete anyone's file.
            if (!IsOwnedByCaller(path)) return Forbid();

            var deleted = await _blobService.DeleteAsync(path);
            return deleted ? Ok() : NotFound();
        }

        /// <summary>
        /// True when the blob sits under the caller's own prefix, or the caller is staff.
        /// Rejects traversal attempts rather than trying to normalise them.
        ///
        /// Note what this does and does not buy: it stops one account reading another's uploads
        /// through this API. It is not confidentiality for the object itself — these blobs live in
        /// the media container, which serves anonymous reads so that image URLs stay permanent
        /// (AzureStorage:MediaPublicAccess). The blob name carries a GUID and is never handed out
        /// by this endpoint, but anything genuinely sensitive belongs in the private documents
        /// container via IFileStorage, not here.
        /// </summary>
        private bool IsOwnedByCaller(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.Contains("..", StringComparison.Ordinal)) return false;

            var role = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");
            if (role is not null &&
                (role.Equals(AuthRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
                 role.Equals(AuthRoles.Support, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var userId = GetUserId();
            if (userId == Guid.Empty) return false;

            return path.StartsWith($"{userId}/", StringComparison.OrdinalIgnoreCase);
        }

        private Guid GetUserId()
        {
            var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }
    }
}

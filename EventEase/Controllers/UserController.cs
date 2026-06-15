using EventEase.Application.Blob;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventEase.Api.Controllers
{
    [Authorize]
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
                return BadRequest("File is empty");

            var userId = User.Identity?.Name ?? "guest"; // Or from JWT claim
            var filePath = await _blobService.UploadAsync(file, userId);
            return Ok(new { FilePath = filePath });
        }

        [HttpGet("download")]
        public async Task<IActionResult> Download([FromQuery] string path)
        {
            var stream = await _blobService.DownloadAsync(path);
            if (stream == null) return NotFound();

            return File(stream, "application/octet-stream", Path.GetFileName(path));
        }

        [HttpDelete("delete")]
        public async Task<IActionResult> Delete([FromQuery] string path)
        {
            var deleted = await _blobService.DeleteAsync(path);
            return deleted ? Ok() : NotFound();
        }
    }
}

using Microsoft.AspNetCore.Http;

namespace EventEase.Application.Blob
{
    /// <summary>
    /// The checks every image upload has to pass before it reaches storage.
    ///
    /// Shared rather than copied per controller: an endpoint that skips the magic-byte check
    /// stores whatever was sent under an image extension and then serves it back from our own
    /// domain, which is how an "avatar" becomes hosted malware or a stored XSS payload.
    /// </summary>
    public static class ImageUploadValidator
    {
        public const long DefaultMaxBytes = 5 * 1024 * 1024;

        public static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

        public static readonly string[] AllowedMimeTypes = { "image/jpeg", "image/png", "image/webp", "image/gif" };

        /// <summary>
        /// Returns null when the file is acceptable, or a message safe to hand back to the caller.
        /// </summary>
        public static async Task<string?> ValidateAsync(IFormFile? file, long maxBytes = DefaultMaxBytes)
        {
            if (file is null || file.Length == 0)
                return "No image file provided.";

            if (file.Length > maxBytes)
                return $"File size exceeds the {maxBytes / (1024 * 1024)}MB limit.";

            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
                return "Invalid file type. Only JPG, PNG, WebP, and GIF are allowed.";

            if (!AllowedMimeTypes.Contains(file.ContentType?.ToLowerInvariant()))
                return "Invalid file content type.";

            // The extension and the declared content type are both attacker-controlled, so the
            // bytes decide.
            if (!await HasImageMagicBytesAsync(file))
                return "The uploaded file is not a valid image.";

            return null;
        }

        /// <summary>
        /// Confirms the leading bytes match JPEG, PNG, GIF or WebP.
        /// </summary>
        public static async Task<bool> HasImageMagicBytesAsync(IFormFile file)
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
    }
}

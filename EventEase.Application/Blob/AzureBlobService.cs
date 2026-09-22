using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;

namespace EventEase.Application.Blob
{
    /// <summary>
    /// Stores uploads in an Azure Storage blob container.
    ///
    /// Blob names are "{userId}/{guid}{ext}". The prefix is what the ownership checks in
    /// UserController match on, so it must stay the first path segment.
    /// </summary>
    public class AzureBlobService : IBlobService
    {
        private readonly AzureBlobClientProvider _provider;

        public AzureBlobService(AzureBlobClientProvider provider)
        {
            _provider = provider;
        }

        public async Task<string> UploadAsync(IFormFile file, string userId)
        {
            if (file is null || file.Length == 0)
                throw new ArgumentException("File is empty", nameof(file));

            var maxBytes = _provider.Options.MaxUploadBytes;
            if (maxBytes > 0 && file.Length > maxBytes)
                throw new ArgumentException($"File exceeds the {maxBytes / (1024 * 1024)} MB limit.", nameof(file));

            // The extension is attacker-controlled, so it is whitelisted rather than trusted. The
            // callers that serve images back (avatars, package galleries) also check magic bytes.
            var extension = SanitizeExtension(Path.GetExtension(file.FileName));
            var prefix = string.IsNullOrWhiteSpace(userId) ? "shared" : userId;
            var blobName = $"{prefix}/{Guid.NewGuid()}{extension}";

            var container = await _provider.GetMediaContainerAsync();
            var blob = container.GetBlobClient(blobName);

            await using var stream = file.OpenReadStream();
            await blob.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                        ? "application/octet-stream"
                        : file.ContentType,
                    // Blob names carry a GUID and are never rewritten, so the content at a given
                    // name cannot change and may be cached indefinitely.
                    CacheControl = _provider.Options.CacheControl
                }
            });

            return blobName;
        }

        public async Task<Stream?> DownloadAsync(string blobName)
        {
            if (string.IsNullOrWhiteSpace(blobName))
                throw new ArgumentException("Blob name cannot be empty", nameof(blobName));

            var container = await _provider.GetMediaContainerAsync();
            var blob = container.GetBlobClient(blobName);

            try
            {
                // Buffered rather than handing back the network stream: the caller returns it
                // through a FileResult that outlives this scope.
                var buffer = new MemoryStream();
                await blob.DownloadToAsync(buffer);
                buffer.Position = 0;
                return buffer;
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task<bool> DeleteAsync(string blobName)
        {
            if (string.IsNullOrWhiteSpace(blobName))
                throw new ArgumentException("Blob name cannot be empty", nameof(blobName));

            var container = await _provider.GetMediaContainerAsync();
            return await container.GetBlobClient(blobName).DeleteIfExistsAsync();
        }

        public Task<string> GetUrlAsync(string blobName) => _provider.GetMediaUrlAsync(blobName);

        public string? TryResolveBlobName(string? url) => _provider.TryResolveMediaBlobName(url);

        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif", ".heic", ".pdf", ".doc", ".docx", ".xls", ".xlsx"
        };

        private static string SanitizeExtension(string? extension)
        {
            if (string.IsNullOrWhiteSpace(extension)) return string.Empty;
            return AllowedExtensions.Contains(extension) ? extension.ToLowerInvariant() : ".bin";
        }
    }
}

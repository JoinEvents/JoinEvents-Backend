using Azure.Storage.Blobs.Models;
using EventEase.Application.Vendors;

namespace EventEase.Application.Blob
{
    /// <summary>
    /// <see cref="IFileStorage"/> on top of the same Azure container the rest of the uploads use.
    ///
    /// Replaces LocalFileStorage in any deployment with more than one instance: container
    /// filesystems are ephemeral and not shared, so a vendor document written by one replica was
    /// gone on restart and invisible to the others.
    ///
    /// Returns the blob path, not a URL. Read paths turn it into a URL through
    /// <see cref="IBlobService.GetUrlAsync"/> at the moment of use, because a SAS URL expires and
    /// must never be written to the database.
    /// </summary>
    public class AzureBlobFileStorage : IFileStorage
    {
        private readonly AzureBlobClientProvider _provider;

        public AzureBlobFileStorage(AzureBlobClientProvider provider)
        {
            _provider = provider;
        }

        public async Task<string> SaveAsync(string container, string fileName, Stream content, string contentType)
        {
            var prefix = SanitizeSegment(container, fallback: "misc");
            var extension = Path.GetExtension(fileName);
            if (extension.Length > 12) extension = string.Empty;

            // The original name is kept only as a suffix for readability; the GUID is what makes
            // the path unique and unguessable.
            var stem = SanitizeSegment(Path.GetFileNameWithoutExtension(fileName), fallback: "file");
            if (stem.Length > 40) stem = stem[..40];

            var blobName = $"{prefix}/{Guid.NewGuid()}-{stem}{extension}";

            var blobContainer = await _provider.GetDocumentsContainerAsync();
            var blob = blobContainer.GetBlobClient(blobName);

            if (content.CanSeek) content.Position = 0;

            await blob.UploadAsync(content, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
                    CacheControl = _provider.Options.CacheControl
                }
            });

            return blobName;
        }

        public Task<string> GetUrlAsync(string? storedPath)
            => _provider.GetDocumentUrlAsync(storedPath ?? string.Empty);

        /// <summary>
        /// Keeps a path segment to characters that are safe in a blob name, and refuses traversal
        /// outright instead of trying to normalise it. Slashes inside <paramref name="value"/> are
        /// preserved so callers can pass nested prefixes like "vendors/{id}/docs".
        /// </summary>
        private static string SanitizeSegment(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (value.Contains("..", StringComparison.Ordinal)) return fallback;

            var cleaned = new string(value
                .Trim('/')
                .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '/' or '.' ? c : '-')
                .ToArray());

            return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
        }
    }
}

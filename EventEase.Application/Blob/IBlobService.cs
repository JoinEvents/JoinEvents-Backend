using Microsoft.AspNetCore.Http;

namespace EventEase.Application.Blob
{
    public interface IBlobService
    {
        Task<Stream?> DownloadAsync(string blobName);

        /// <summary>
        /// Stores the file under the caller's own prefix and returns the blob name — the durable
        /// identifier that belongs in the database. Call <see cref="GetUrlAsync"/> to turn it into
        /// something a browser can load; URLs may be time-limited and must not be persisted.
        /// </summary>
        Task<string> UploadAsync(IFormFile file, string userId);

        Task<bool> DeleteAsync(string blobName);

        /// <summary>
        /// A URL the client can load for this blob. Absolute URLs are returned unchanged, so rows
        /// written before object storage was in place keep working.
        /// </summary>
        Task<string> GetUrlAsync(string blobName);

        /// <summary>
        /// Maps a URL this service produced back to the blob it points at, so a deleted row can
        /// take its file with it. Null when the URL did not come from our own storage — an image
        /// hosted elsewhere, or a social-login avatar.
        /// </summary>
        string? TryResolveBlobName(string? url);
    }
}

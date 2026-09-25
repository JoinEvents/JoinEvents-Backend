using Google.Cloud.Storage.V1;
using Microsoft.AspNetCore.Http;

namespace EventEase.Application.Blob
{
    /// <summary>
    /// Stores uploads as objects in a Google Cloud Storage bucket.
    ///
    /// The legacy provider, kept so a deployment that has not moved to Azure yet still works.
    /// Object names are "{userId}/{guid}{ext}" — the prefix is what the ownership checks in
    /// UserController match on, so it must stay the first path segment.
    /// </summary>
    public class GcpBucketService : IBlobService
    {
        private readonly GcsClientProvider _provider;

        public GcpBucketService(GcsClientProvider provider)
        {
            _provider = provider;
        }

        public async Task<string> UploadAsync(IFormFile file, string userId)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is empty", nameof(file));

            var extension = StoragePaths.SanitizeExtension(Path.GetExtension(file.FileName));
            var prefix = string.IsNullOrWhiteSpace(userId) ? "shared" : userId;
            var objectName = $"{prefix}/{Guid.NewGuid()}{extension}";

            try
            {
                using var stream = file.OpenReadStream();

                await _provider.Storage.UploadObjectAsync(
                    _provider.BucketName,
                    objectName,
                    file.ContentType,
                    stream,
                    new UploadObjectOptions { PredefinedAcl = PredefinedObjectAcl.Private });

                return objectName;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to upload file to GCP bucket: {ex.Message}", ex);
            }
        }

        public async Task<Stream?> DownloadAsync(string blobName)
        {
            if (string.IsNullOrEmpty(blobName))
                throw new ArgumentException("Blob name cannot be empty", nameof(blobName));

            try
            {
                var memoryStream = new MemoryStream();
                await _provider.Storage.DownloadObjectAsync(_provider.BucketName, blobName, memoryStream);
                memoryStream.Position = 0;
                return memoryStream;
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to download file from GCP bucket: {ex.Message}", ex);
            }
        }

        public async Task<bool> DeleteAsync(string blobName)
        {
            if (string.IsNullOrEmpty(blobName))
                throw new ArgumentException("Blob name cannot be empty", nameof(blobName));

            try
            {
                await _provider.Storage.DeleteObjectAsync(_provider.BucketName, blobName);
                return true;
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to delete file from GCP bucket: {ex.Message}", ex);
            }
        }

        public Task<string> GetUrlAsync(string blobName) => _provider.GetUrlAsync(blobName);

        public string? TryResolveBlobName(string? url) => _provider.TryResolveObjectName(url);
    }
}

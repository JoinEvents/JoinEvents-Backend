using EventEase.Application.Vendors;
using Google.Cloud.Storage.V1;

namespace EventEase.Application.Blob
{
    /// <summary>
    /// <see cref="IFileStorage"/> backed by the same Google Cloud Storage bucket the rest of the
    /// GCP uploads use, so vendor documents and support attachments are objects rather than files
    /// on a container's disk.
    ///
    /// Returns the object name, not a URL. Read paths turn it into a URL through
    /// <see cref="GetUrlAsync"/> at the moment of use, because a signed URL expires and must never
    /// be written to the database.
    /// </summary>
    public class GcsFileStorage : IFileStorage
    {
        private readonly GcsClientProvider _provider;

        public GcsFileStorage(GcsClientProvider provider)
        {
            _provider = provider;
        }

        public async Task<string> SaveAsync(string container, string fileName, Stream content, string contentType)
        {
            var objectName = StoragePaths.BuildDocumentName(container, fileName);

            if (content.CanSeek) content.Position = 0;

            await _provider.Storage.UploadObjectAsync(
                _provider.BucketName,
                objectName,
                string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
                content,
                new UploadObjectOptions { PredefinedAcl = PredefinedObjectAcl.Private });

            return objectName;
        }

        public Task<string> GetUrlAsync(string? storedPath) => _provider.GetUrlAsync(storedPath ?? string.Empty);
    }
}

using Azure.Storage.Blobs.Models;
using EventEase.Application.Vendors;

namespace EventEase.Application.Blob
{
    /// <summary>
    /// <see cref="IFileStorage"/> on top of the Azure documents container.
    ///
    /// There is no local-disk implementation any more: container filesystems are ephemeral and
    /// not shared, so a vendor document written by one replica was gone on restart and invisible
    /// to the others.
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
            var blobName = StoragePaths.BuildDocumentName(container, fileName);

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

    }
}

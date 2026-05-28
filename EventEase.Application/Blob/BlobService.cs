using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace EventEase.Application.Blob
{
    public class BlobService : IBlobService
    {
        private readonly BlobContainerClient _blobContainerClient;
        public BlobService(IConfiguration config) {
            var connectionString = config["AzureStorage:ConnectionString"];
            var containerName = config["AzureStorage:ContainerName"];
            _blobContainerClient = new BlobContainerClient(connectionString, containerName);
        }
        public async Task<string> UploadAsync(IFormFile file, string userId)
        {
            var fileName = $"{userId}/{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var blobClient = _blobContainerClient.GetBlobClient(fileName);

            using var stream = file.OpenReadStream();
            await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = file.ContentType });

            return fileName;
        }
        public async Task<Stream?> DownloadAsync(string blobName)
        {
            var blobClient = _blobContainerClient.GetBlobClient(blobName);
            if (await blobClient.ExistsAsync())
            {
                var response = await blobClient.DownloadAsync();
                return response.Value.Content;
            }
            return null;
        }
        public async Task<bool> DeleteAsync(string blobName)
        {
            var blobClient = _blobContainerClient.GetBlobClient(blobName);
            return await blobClient.DeleteIfExistsAsync();
        }
    }
}

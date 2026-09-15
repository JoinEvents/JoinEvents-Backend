using Google.Cloud.Storage.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;

namespace EventEase.Application.Blob
{
    public class GcpBucketService : IBlobService
    {
        private readonly Lazy<StorageClient> _lazyStorageClient;
        private readonly string _bucketName;

        private StorageClient Storage => _lazyStorageClient.Value;

        public GcpBucketService(IConfiguration config)
        {
            _bucketName = config["Gcp:BucketName"] ?? string.Empty;
            var credentialsPath = config["Gcp:CredentialsPath"];

            if (!string.IsNullOrEmpty(credentialsPath))
            {
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialsPath);
            }

            // Constructed on first use rather than at registration. This service is a singleton
            // injected into several controllers, so throwing in the constructor took down
            // endpoints that never touch object storage. ProjectId is not required for object
            // operations — application default credentials supply it.
            _lazyStorageClient = new Lazy<StorageClient>(() =>
            {
                if (string.IsNullOrEmpty(_bucketName))
                    throw new InvalidOperationException("Gcp:BucketName is not configured.");

                return StorageClient.Create();
            }, isThreadSafe: true);
        }

        public async Task<string> UploadAsync(IFormFile file, string userId)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is empty", nameof(file));

            // Create a unique blob name with user folder structure
            var fileName = $"{userId}/{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";

            try
            {
                using var stream = file.OpenReadStream();
                
                // Upload to GCS with metadata
                var obj = await Storage.UploadObjectAsync(
                    _bucketName,
                    fileName,
                    file.ContentType,
                    stream,
                    new UploadObjectOptions
                    {
                        PredefinedAcl = PredefinedObjectAcl.Private
                    }
                );

                return fileName;
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
                var obj = await Storage.GetObjectAsync(_bucketName, blobName);
                if (obj == null)
                    return null;

                var memoryStream = new MemoryStream();
                await Storage.DownloadObjectAsync(_bucketName, blobName, memoryStream);
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
                await Storage.DeleteObjectAsync(_bucketName, blobName);
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
    }
}

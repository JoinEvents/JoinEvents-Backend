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
        private readonly StorageClient _storageClient;
        private readonly string _bucketName;
        private readonly string _projectId;

        public GcpBucketService(IConfiguration config)
        {
            _projectId = config["Gcp:ProjectId"];
            _bucketName = config["Gcp:BucketName"];
            var credentialsPath = config["Gcp:CredentialsPath"];

            if (string.IsNullOrEmpty(_projectId))
                throw new InvalidOperationException("Gcp:ProjectId is not configured");
            if (string.IsNullOrEmpty(_bucketName))
                throw new InvalidOperationException("Gcp:BucketName is not configured");

            // Initialize GCP Storage Client
            if (!string.IsNullOrEmpty(credentialsPath))
            {
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialsPath);
            }

            _storageClient = StorageClient.Create();
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
                var obj = await _storageClient.UploadObjectAsync(
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
                var obj = await _storageClient.GetObjectAsync(_bucketName, blobName);
                if (obj == null)
                    return null;

                var memoryStream = new MemoryStream();
                await _storageClient.DownloadObjectAsync(_bucketName, blobName, memoryStream);
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
                await _storageClient.DeleteObjectAsync(_bucketName, blobName);
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

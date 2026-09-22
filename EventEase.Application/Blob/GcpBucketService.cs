using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System;
using System.IO;
using System.Threading.Tasks;

namespace EventEase.Application.Blob
{
    public class GcpBucketService : IBlobService
    {
        private readonly Lazy<StorageClient> _lazyStorageClient;
        private readonly Lazy<UrlSigner?> _lazyUrlSigner;
        private readonly string _bucketName;
        private readonly int _signedUrlTtlMinutes;

        private StorageClient Storage => _lazyStorageClient.Value;

        public GcpBucketService(IConfiguration config)
        {
            _bucketName = config["Gcp:BucketName"] ?? string.Empty;
            _signedUrlTtlMinutes = config.GetValue("Gcp:SignedUrlTtlMinutes", 60);
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

            // Objects are uploaded private, so a browser needs a signed URL. Signing requires a
            // service account key; on a runtime that only has a metadata-server credential there
            // is nothing to sign with, and GetUrlAsync falls back to proxying through the API.
            _lazyUrlSigner = new Lazy<UrlSigner?>(() =>
            {
                try
                {
                    return string.IsNullOrEmpty(credentialsPath)
                        ? UrlSigner.FromCredential(GoogleCredential.GetApplicationDefault())
                        : UrlSigner.FromCredentialFile(credentialsPath);
                }
                catch
                {
                    return null;
                }
            }, isThreadSafe: true);
        }

        public async Task<string> GetUrlAsync(string blobName)
        {
            if (string.IsNullOrWhiteSpace(blobName)) return string.Empty;

            if (blobName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                blobName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return blobName;
            }

            // Rows written before object storage hold "/files/..." paths served by the API's own
            // static file middleware. They are already usable as-is.
            if (blobName.StartsWith("/", StringComparison.Ordinal))
                return blobName;

            var signer = _lazyUrlSigner.Value;
            if (signer is not null)
            {
                try
                {
                    return await signer.SignAsync(
                        _bucketName, blobName, TimeSpan.FromMinutes(Math.Max(1, _signedUrlTtlMinutes)), HttpMethod.Get);
                }
                catch
                {
                    // Fall through to the proxy route below.
                }
            }

            return $"/api/v1/files/download?path={Uri.EscapeDataString(blobName)}";
        }

        public string? TryResolveBlobName(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            const string proxyPrefix = "/api/v1/files/download?path=";
            if (url.StartsWith(proxyPrefix, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(url[proxyPrefix.Length..]);

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

            var path = uri.AbsolutePath.TrimStart('/');
            var prefix = _bucketName + "/";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

            var objectName = Uri.UnescapeDataString(path[prefix.Length..]);
            return string.IsNullOrWhiteSpace(objectName) ? null : objectName;
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

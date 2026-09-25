using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Configuration;
using System.Net.Http;

namespace EventEase.Application.Blob
{
    /// <summary>
    /// Owns the Google Cloud Storage client and URL signer for the process.
    ///
    /// Shared by <see cref="GcpBucketService"/> and <see cref="GcsFileStorage"/> so the bucket,
    /// the credential and the signing behaviour are defined once.
    ///
    /// Both are constructed on first use rather than at registration: this is a singleton
    /// injected into several controllers, and a misconfigured bucket should fail the one request
    /// that touches storage, not every endpoint in the API.
    /// </summary>
    public class GcsClientProvider
    {
        private readonly Lazy<StorageClient> _lazyStorageClient;
        private readonly Lazy<UrlSigner?> _lazyUrlSigner;
        private readonly int _signedUrlTtlMinutes;

        public GcsClientProvider(IConfiguration config)
        {
            BucketName = config["Gcp:BucketName"] ?? string.Empty;
            _signedUrlTtlMinutes = config.GetValue("Gcp:SignedUrlTtlMinutes", 60);

            var credentialsPath = config["Gcp:CredentialsPath"];
            if (!string.IsNullOrEmpty(credentialsPath))
            {
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialsPath);
            }

            _lazyStorageClient = new Lazy<StorageClient>(() =>
            {
                if (string.IsNullOrEmpty(BucketName))
                    throw new InvalidOperationException("Gcp:BucketName is not configured.");

                // ProjectId is not required for object operations — application default
                // credentials supply it.
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

        public string BucketName { get; }

        public StorageClient Storage => _lazyStorageClient.Value;

        private const string ProxyPrefix = "/api/v1/files/download?path=";

        /// <summary>
        /// A URL the client can load for this object: a signed one where we can sign, otherwise
        /// the API's own download route, which streams the bytes after an ownership check.
        /// </summary>
        public async Task<string> GetUrlAsync(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName)) return string.Empty;
            if (StoragePaths.IsAlreadyResolvable(objectName)) return objectName;

            var signer = _lazyUrlSigner.Value;
            if (signer is not null)
            {
                try
                {
                    return await signer.SignAsync(
                        BucketName, objectName,
                        TimeSpan.FromMinutes(Math.Max(1, _signedUrlTtlMinutes)), HttpMethod.Get);
                }
                catch
                {
                    // Fall through to the proxy route below.
                }
            }

            return $"{ProxyPrefix}{Uri.EscapeDataString(objectName)}";
        }

        /// <summary>
        /// The inverse of <see cref="GetUrlAsync"/>, so a deleted row can take its object with it.
        /// Null when the URL did not come from this bucket.
        /// </summary>
        public string? TryResolveObjectName(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            if (url.StartsWith(ProxyPrefix, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(url[ProxyPrefix.Length..]);

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

            var path = uri.AbsolutePath.TrimStart('/');
            var prefix = BucketName + "/";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

            var objectName = Uri.UnescapeDataString(path[prefix.Length..]);
            return string.IsNullOrWhiteSpace(objectName) ? null : objectName;
        }
    }
}

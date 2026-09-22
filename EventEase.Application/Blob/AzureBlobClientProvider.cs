using System.Collections.Concurrent;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;

namespace EventEase.Application.Blob
{
    /// <summary>
    /// Owns the single <see cref="BlobServiceClient"/> for the process and turns blob names into
    /// URLs a browser can load.
    ///
    /// The client is built on first use rather than at registration: this is a singleton injected
    /// into several controllers, and a misconfigured account should fail the one request that
    /// touches storage, not every endpoint in the API.
    /// </summary>
    public class AzureBlobClientProvider
    {
        private readonly AzureStorageOptions _options;
        private readonly Lazy<BlobServiceClient> _serviceClient;

        // Containers whose existence has already been confirmed this process.
        private readonly ConcurrentDictionary<string, bool> _readyContainers = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _containerInitLock = new(1, 1);

        // A user delegation key costs a round trip and stays valid for hours, so it is reused
        // until shortly before it expires.
        private UserDelegationKey? _delegationKey;
        private DateTimeOffset _delegationKeyExpiry = DateTimeOffset.MinValue;
        private readonly SemaphoreSlim _delegationKeyLock = new(1, 1);

        public AzureBlobClientProvider(IOptions<AzureStorageOptions> options)
        {
            _options = options.Value;
            _serviceClient = new Lazy<BlobServiceClient>(CreateServiceClient, isThreadSafe: true);
        }

        public AzureStorageOptions Options => _options;

        /// <summary>The public container for avatars and package images.</summary>
        public string MediaContainer => _options.ContainerName;

        /// <summary>The private container for verification documents and support attachments.</summary>
        public string DocumentsContainer => _options.DocumentsContainerName;

        private BlobServiceClient CreateServiceClient()
        {
            if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
            {
                return new BlobServiceClient(_options.ConnectionString);
            }

            var endpoint = ResolveBlobEndpoint();
            if (endpoint is null)
            {
                throw new InvalidOperationException(
                    "Azure storage is not configured. Set AzureStorage:ConnectionString, " +
                    "or AzureStorage:AccountName to authenticate with a managed identity.");
            }

            // Picks up, in order: the app's managed identity, an app registration supplied through
            // AZURE_CLIENT_ID/TENANT_ID/CLIENT_SECRET, or the developer's az-cli login.
            return new BlobServiceClient(endpoint, new DefaultAzureCredential());
        }

        private Uri? ResolveBlobEndpoint()
        {
            if (!string.IsNullOrWhiteSpace(_options.BlobEndpoint))
                return new Uri(_options.BlobEndpoint.TrimEnd('/'));

            if (!string.IsNullOrWhiteSpace(_options.AccountName))
                return new Uri($"https://{_options.AccountName}.blob.core.windows.net");

            return null;
        }

        /// <summary>
        /// A container client, with the container created on first use when
        /// CreateContainerIfNotExists is on.
        /// </summary>
        public async Task<BlobContainerClient> GetContainerAsync(
            string containerName, bool publicAccess, CancellationToken ct = default)
        {
            var container = _serviceClient.Value.GetBlobContainerClient(containerName);

            if (!_options.CreateContainerIfNotExists || _readyContainers.ContainsKey(containerName))
                return container;

            await _containerInitLock.WaitAsync(ct);
            try
            {
                if (!_readyContainers.ContainsKey(containerName))
                {
                    var access = publicAccess ? PublicAccessType.Blob : PublicAccessType.None;
                    await container.CreateIfNotExistsAsync(access, cancellationToken: ct);
                    _readyContainers[containerName] = true;
                }
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 403 || ex.Status == 409)
            {
                // 403: a least-privilege identity may hold "Storage Blob Data Contributor" (data
                // plane) without the rights to create containers, or the account may have public
                // access disabled at the account level. 409: someone else created it first.
                // Either way the container is expected to exist already, so this is not fatal.
                _readyContainers[containerName] = true;
            }
            finally
            {
                _containerInitLock.Release();
            }

            return container;
        }

        public Task<BlobContainerClient> GetMediaContainerAsync(CancellationToken ct = default)
            => GetContainerAsync(MediaContainer, _options.MediaPublicAccess, ct);

        public Task<BlobContainerClient> GetDocumentsContainerAsync(CancellationToken ct = default)
            => GetContainerAsync(DocumentsContainer, publicAccess: false, ct);

        /// <summary>
        /// A URL the client can load for an image: the plain blob URL when the media container is
        /// public (permanent, cacheable, safe to persist), otherwise a read-only SAS URL.
        /// </summary>
        public Task<string> GetMediaUrlAsync(string blobName, CancellationToken ct = default)
            => GetUrlAsync(MediaContainer, _options.MediaPublicAccess, blobName, ct);

        /// <summary>A short-lived read-only URL for a private document.</summary>
        public Task<string> GetDocumentUrlAsync(string blobName, CancellationToken ct = default)
            => GetUrlAsync(DocumentsContainer, publicAccess: false, blobName, ct);

        public async Task<string> GetUrlAsync(
            string containerName, bool publicAccess, string blobName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(blobName))
                return string.Empty;

            // Anything already absolute is a legacy value stored as a full URL. Pass it back.
            if (blobName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                blobName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return blobName;
            }

            // Rows written before object storage hold "/files/..." paths served by the API's own
            // static file middleware. They are already usable as-is.
            if (blobName.StartsWith("/", StringComparison.Ordinal))
                return blobName;

            if (publicAccess)
            {
                if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
                    return $"{_options.PublicBaseUrl.TrimEnd('/')}/{containerName}/{EncodePath(blobName)}";

                return _serviceClient.Value
                    .GetBlobContainerClient(containerName)
                    .GetBlobClient(blobName).Uri.ToString();
            }

            var container = await GetContainerAsync(containerName, publicAccess, ct);
            var blob = container.GetBlobClient(blobName);
            var expiresOn = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _options.SasTtlMinutes));

            // Connection-string auth carries the account key, so the SDK can sign locally.
            if (blob.CanGenerateSasUri)
                return blob.GenerateSasUri(BlobSasPermissions.Read, expiresOn).ToString();

            // Managed identity: sign with a user delegation key instead.
            var key = await GetDelegationKeyAsync(ct);

            var builder = new BlobSasBuilder
            {
                BlobContainerName = containerName,
                BlobName = blobName,
                Resource = "b",
                // Backdated a little so a small clock skew between the app and storage does not
                // make a freshly issued SAS look like it starts in the future.
                StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
                ExpiresOn = expiresOn
            };
            builder.SetPermissions(BlobSasPermissions.Read);

            var sas = builder.ToSasQueryParameters(key, _serviceClient.Value.AccountName).ToString();
            return $"{blob.Uri}?{sas}";
        }

        private async Task<UserDelegationKey> GetDelegationKeyAsync(CancellationToken ct)
        {
            if (_delegationKey is not null && _delegationKeyExpiry > DateTimeOffset.UtcNow.AddMinutes(5))
                return _delegationKey;

            await _delegationKeyLock.WaitAsync(ct);
            try
            {
                if (_delegationKey is null || _delegationKeyExpiry <= DateTimeOffset.UtcNow.AddMinutes(5))
                {
                    var expiry = DateTimeOffset.UtcNow.AddHours(6);
                    var response = await _serviceClient.Value.GetUserDelegationKeyAsync(
                        DateTimeOffset.UtcNow.AddMinutes(-5), expiry, ct);

                    _delegationKey = response.Value;
                    _delegationKeyExpiry = expiry;
                }
            }
            finally
            {
                _delegationKeyLock.Release();
            }

            return _delegationKey!;
        }

        /// <summary>
        /// The inverse of <see cref="GetMediaUrlAsync"/>: pulls the blob name back out of a URL we
        /// generated, whether it points at the blob endpoint or at the CDN in front of it. Any SAS
        /// query string is ignored. Null when the URL is not ours.
        /// </summary>
        public string? TryResolveMediaBlobName(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

            var path = uri.AbsolutePath.TrimStart('/');
            var prefix = MediaContainer + "/";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

            var blobName = Uri.UnescapeDataString(path[prefix.Length..]);
            return string.IsNullOrWhiteSpace(blobName) ? null : blobName;
        }

        private static string EncodePath(string blobName)
            => string.Join('/', blobName.Split('/').Select(Uri.EscapeDataString));
    }
}

namespace EventEase.Application.Blob
{
    /// <summary>
    /// Binds the "AzureStorage" configuration section.
    ///
    /// Two ways to authenticate, checked in this order:
    ///   1. ConnectionString  - a storage account connection string (simplest; works anywhere).
    ///   2. AccountName       - the account is reached with DefaultAzureCredential instead, so a
    ///                          managed identity on App Service / Container Apps / AKS needs no
    ///                          secret in configuration at all. This is the preferred production
    ///                          setup; grant the identity "Storage Blob Data Contributor".
    /// </summary>
    public class AzureStorageOptions
    {
        public const string SectionName = "AzureStorage";

        /// <summary>Storage account connection string. Leave empty to use managed identity.</summary>
        public string? ConnectionString { get; set; }

        /// <summary>Storage account name, e.g. "joineventsmedia". Used when no connection string is set.</summary>
        public string? AccountName { get; set; }

        /// <summary>Blob endpoint. Defaults to https://{AccountName}.blob.core.windows.net.</summary>
        public string? BlobEndpoint { get; set; }

        /// <summary>
        /// Container for images that are displayed to anyone who can see the listing they belong
        /// to: avatars and package galleries. Its URLs are written into the database, so they have
        /// to stay valid indefinitely — see <see cref="MediaPublicAccess"/>.
        /// </summary>
        public string ContainerName { get; set; } = "joinevents-media";

        /// <summary>
        /// Container for files that must not be world-readable: vendor verification documents and
        /// support attachments. Always private; reads go through short-lived SAS URLs.
        /// </summary>
        public string DocumentsContainerName { get; set; } = "joinevents-documents";

        /// <summary>
        /// True (the default) grants anonymous blob-level read on the media container, which is
        /// what makes an image URL permanent and therefore safe to store on a row and to cache.
        ///
        /// Setting this to false hands out SAS URLs instead. Those expire, so a URL already saved
        /// in the database stops working — only turn it off together with a read path that
        /// re-resolves every image on each request.
        /// </summary>
        public bool MediaPublicAccess { get; set; } = true;

        /// <summary>
        /// CDN or custom domain in front of the media container, e.g. https://cdn.joinevents.com.
        /// When set, image URLs are built from this instead of the raw blob endpoint. Only
        /// meaningful while <see cref="MediaPublicAccess"/> is on.
        /// </summary>
        public string? PublicBaseUrl { get; set; }

        /// <summary>How long a generated read SAS stays valid, in minutes.</summary>
        public int SasTtlMinutes { get; set; } = 60;

        /// <summary>Rejects anything larger before a byte reaches the account. 10 MB by default.</summary>
        public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;

        /// <summary>Create the containers at first use if they do not exist yet.</summary>
        public bool CreateContainerIfNotExists { get; set; } = true;

        /// <summary>Cache-Control written onto every uploaded blob.</summary>
        public string CacheControl { get; set; } = "public, max-age=31536000, immutable";
    }
}

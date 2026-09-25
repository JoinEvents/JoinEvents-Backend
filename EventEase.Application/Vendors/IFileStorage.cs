namespace EventEase.Application.Vendors
{
    public interface IFileStorage
    {
        /// <summary>
        /// Stores the content and returns the path it was stored at. That path is what belongs on
        /// a row — never the URL, which for private storage is short-lived.
        /// </summary>
        Task<string> SaveAsync(string container, string fileName, Stream content, string contentType);

        /// <summary>
        /// Turns a stored path into a URL the caller can load right now. Values already stored as
        /// absolute URLs, or as the "/files/..." paths written before object storage, come back
        /// unchanged.
        /// </summary>
        Task<string> GetUrlAsync(string? storedPath);
    }
}

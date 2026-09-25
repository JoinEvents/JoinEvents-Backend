namespace EventEase.Application.Blob
{
    /// <summary>
    /// Shared rules for building object names, so the Azure and GCS implementations cannot drift
    /// into naming the same upload differently.
    /// </summary>
    public static class StoragePaths
    {
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif", ".heic", ".pdf", ".doc", ".docx", ".xls", ".xlsx"
        };

        /// <summary>
        /// Keeps an extension we are willing to serve back, and collapses anything else to ".bin"
        /// rather than storing an attacker-chosen one.
        /// </summary>
        public static string SanitizeExtension(string? extension)
        {
            if (string.IsNullOrWhiteSpace(extension)) return string.Empty;
            return AllowedExtensions.Contains(extension) ? extension.ToLowerInvariant() : ".bin";
        }

        /// <summary>
        /// Keeps a path segment to characters that are safe in an object name, and refuses
        /// traversal outright instead of trying to normalise it. Slashes are preserved so callers
        /// can pass nested prefixes like "vendors/{id}/docs".
        /// </summary>
        public static string SanitizeSegment(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (value.Contains("..", StringComparison.Ordinal)) return fallback;

            var cleaned = new string(value
                .Trim('/')
                .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '/' or '.' ? c : '-')
                .ToArray());

            return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
        }

        /// <summary>
        /// The name an <see cref="Vendors.IFileStorage"/> save lands at:
        /// "{prefix}/{guid}-{original name}{ext}". The GUID is what makes it unique and
        /// unguessable; the original name survives only so the object is recognisable in a
        /// storage browser.
        /// </summary>
        public static string BuildDocumentName(string prefix, string fileName)
        {
            var folder = SanitizeSegment(prefix, fallback: "misc");

            var extension = Path.GetExtension(fileName);
            if (extension.Length > 12) extension = string.Empty;

            var stem = SanitizeSegment(Path.GetFileNameWithoutExtension(fileName), fallback: "file");
            if (stem.Length > 40) stem = stem[..40];

            return $"{folder}/{Guid.NewGuid()}-{stem}{extension}";
        }

        /// <summary>
        /// True when the value is already something a browser can load, so it must be handed back
        /// untouched: an absolute URL, or a "/files/..." path written before object storage.
        /// </summary>
        public static bool IsAlreadyResolvable(string value) =>
            value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("/", StringComparison.Ordinal);
    }
}

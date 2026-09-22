using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Application.Vendors
{
    public class LocalFileStorage : IFileStorage
    {
        public Task<string> SaveAsync(string container, string fileName, Stream content, string contentType)
        {
            var dir = Path.Combine("storage", container);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fileName);
            using var fs = File.Create(path);
            content.CopyTo(fs);
            return Task.FromResult($"/files/{container}/{fileName}");
        }

        /// <summary>Local files are already served from /files, so the stored path is the URL.</summary>
        public Task<string> GetUrlAsync(string? storedPath) => Task.FromResult(storedPath ?? string.Empty);
    }
}

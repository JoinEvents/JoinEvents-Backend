using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Application.Blob
{
    public interface IBlobService
    {
        Task<Stream?> DownloadAsync(string blobName);
        Task<string> UploadAsync(IFormFile file, string userId);
        Task<bool> DeleteAsync(string blobName);

    }
}

using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EventEase.Application.Vendors.Dtos;

namespace EventEase.Application.Vendors
{
    public interface IVendorService
    {
        Task<VendorDto> RegisterAsync(Guid userId, VendorRegisterDto dto);
        Task<bool> UploadDocumentsAsync(Guid vendorId, IEnumerable<IFormFile> files);
        Task<VendorDto?> GetAsync(Guid vendorId);
        Task<bool> ValidateAsync(Guid vendorId);
    }
}

using System.Collections.Generic;
using System.Threading.Tasks;
using EventEase.Core.Entities;
using static EventEase.Application.Vendors.Dtos;

namespace EventEase.Application.Vendors
{
    public interface IVendorDocumentService
    {
        Task<VendorDocument> UploadDocumentAsync(Guid vendorId, string documentType, string fileName, string fileUrl);
        Task<VendorDocument?> ReviewDocumentAsync(Guid docId, Guid adminId, ReviewDocumentDto dto);
        Task<bool> ModerateVendorAsync(Guid vendorId, Guid adminId, ModerateVendorDto dto);
        Task<VendorAnalyticsResponse> GetAnalyticsAsync(Guid vendorId);
        Task<object> GetAnalyticsForFrontendAsync(Guid vendorId);
        Task<List<object>> GetAllVendorsForAdminAsync();
    }
}

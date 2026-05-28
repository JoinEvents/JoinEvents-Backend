using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static EventEase.Application.Vendors.Dtos;

namespace EventEase.Application.Vendors
{
    public class VendorService : IVendorService
    {
        private readonly EventEaseDbContext _db;
        private readonly IFileStorage _storage; // abstraction for Blob/local
        public VendorService(EventEaseDbContext db, IFileStorage storage) { _db = db; _storage = storage; }

        public async Task<VendorDto> RegisterAsync(Guid userId, VendorRegisterDto dto)
        {
            var vendor = new Vendor { Id = Guid.NewGuid(), UserId = userId, BusinessName = dto.BusinessName, Description = dto.Description, Location = dto.Location, IsValidated = false };
            _db.Vendors.Add(vendor);
            await _db.SaveChangesAsync();
            return new VendorDto(vendor.Id, vendor.BusinessName, vendor.Location, vendor.IsValidated);
        }

        public async Task<bool> UploadDocumentsAsync(Guid vendorId, IEnumerable<IFormFile> files)
        {
            var vendor = await _db.Vendors.FindAsync(vendorId);
            if (vendor is null) return false;
            var docList = new List<object>();
            foreach (var f in files)
            {
                var uri = await _storage.SaveAsync($"vendors/{vendorId}/docs", f.FileName, f.OpenReadStream(), f.ContentType);
                docList.Add(new { name = f.FileName, url = uri });
            }
            vendor.DocumentsJson = JsonSerializer.Serialize(docList);
            await _db.SaveChangesAsync();
            return true;
        }

        public Task<VendorDto?> GetAsync(Guid vendorId) =>
          _db.Vendors.Where(v => v.Id == vendorId)
            .Select(v => new VendorDto(v.Id, v.BusinessName, v.Location, v.IsValidated))
            .FirstOrDefaultAsync();

        public async Task<bool> ValidateAsync(Guid vendorId)
        {
            var v = await _db.Vendors.FindAsync(vendorId);
            if (v is null) return false;
            v.IsValidated = true;
            await _db.SaveChangesAsync();
            return true;
        }
    }
}

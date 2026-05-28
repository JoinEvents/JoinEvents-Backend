using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using EventEase.Application.Vendors;

namespace EventEase.Api.Controllers
{
    public class UploadDocumentDto
    {
        public IFormFile File { get; set; } = null!;
        public string DocumentType { get; set; } = string.Empty;
    }

    [ApiController]
    [Route("api/v1/vendor/verification")]
    public class VendorVerificationController : ControllerBase
    {
        private readonly EventEaseDbContext _db;
        private readonly IFileStorage _fileStorage;

        public VendorVerificationController(EventEaseDbContext db, IFileStorage fileStorage)
        {
            _db = db;
            _fileStorage = fileStorage;
        }

        private Guid GetUserId()
        {
            var val = User.FindFirstValue(ClaimTypes.NameIdentifier) 
                      ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? User.FindFirstValue("sub")
                      ?? User.FindFirstValue("id");
            return Guid.TryParse(val, out var guid) ? guid : Guid.Empty;
        }

        [Authorize(Policy = "Vendor")]
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var userId = GetUserId();
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
            if (vendor == null)
            {
                vendor = new Core.Entities.Vendor { 
                    Id = Guid.NewGuid(), 
                    UserId = userId, 
                    BusinessName = "My Vendor Business", 
                    IsValidated = false,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Vendors.Add(vendor);
                await _db.SaveChangesAsync();
            }

            var docs = await _db.VendorDocuments.Where(d => d.VendorId == vendor.Id).ToListAsync();

            bool hasDocs = docs.Any();
            bool isVerified = vendor.IsValidated;

            string status = "pending";
            string? remarks = null;

            if (isVerified)
            {
                status = "verified";
            }
            else if (docs.Any(d => d.Status.Equals("pending", StringComparison.OrdinalIgnoreCase)))
            {
                status = "under_review";
            }
            else if (docs.Any(d => d.Status.Equals("action_required", StringComparison.OrdinalIgnoreCase)))
            {
                status = "action_required";
                remarks = docs.FirstOrDefault(d => d.Status.Equals("action_required", StringComparison.OrdinalIgnoreCase))?.RejectionReason;
            }
            else if (docs.Any(d => d.Status.Equals("rejected", StringComparison.OrdinalIgnoreCase)))
            {
                status = "rejected";
                remarks = docs.FirstOrDefault(d => d.Status.Equals("rejected", StringComparison.OrdinalIgnoreCase))?.RejectionReason;
            }
            else if (hasDocs)
            {
                status = "under_review";
            }

            var steps = new List<object>
            {
                new { label = "Registration", desc = "Account created and basic details submitted", status = "done", icon = "bi-person-check" },
                new { 
                    label = "Document Upload", 
                    desc = "Business registration, GST, and other docs uploaded", 
                    status = (status == "action_required" || status == "rejected") ? "active" : (hasDocs ? "done" : "active"), 
                    icon = "bi-file-earmark-check" 
                },
                new { 
                    label = "Admin Review", 
                    desc = "Our team is reviewing your submitted documents", 
                    status = isVerified ? "done" : (status == "under_review" ? "active" : "pending"), 
                    icon = "bi-eye" 
                },
                new { 
                    label = "Go Live", 
                    desc = "Your services are visible to customers and bookings open", 
                    status = isVerified ? "done" : "pending", 
                    icon = "bi-rocket-takeoff" 
                }
            };

            var docResponses = docs.Select(d => new
            {
                type = d.DocumentType,
                name = d.FileName,
                status = d.Status,
                date = d.UploadedAt.ToString("yyyy-MM-dd"),
                fileUrl = d.FileUrl,
                url = d.FileUrl
            }).ToList();

            return Ok(new { steps, docs = docResponses, isVerified, status, remarks });
        }

        [Authorize(Policy = "Vendor")]
        [HttpPost("upload")]
        public async Task<IActionResult> UploadDocument([FromForm] UploadDocumentDto dto)
        {
            if (dto.File == null || dto.File.Length == 0) return BadRequest(new { error = "No file uploaded." });
            if (string.IsNullOrEmpty(dto.DocumentType)) return BadRequest(new { error = "Document type is required." });

            var userId = GetUserId();
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
            if (vendor == null)
            {
                vendor = new Core.Entities.Vendor { 
                    Id = Guid.NewGuid(), 
                    UserId = userId, 
                    BusinessName = "My Vendor Business", 
                    IsValidated = false,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Vendors.Add(vendor);
                await _db.SaveChangesAsync();
            }

            var urlPath = await _fileStorage.SaveAsync("verification", dto.File.FileName, dto.File.OpenReadStream(), dto.File.ContentType);

            var doc = new VendorDocument
            {
                Id = Guid.NewGuid(),
                VendorId = vendor.Id,
                DocumentType = dto.DocumentType,
                FileName = dto.File.FileName,
                FileUrl = urlPath,
                Status = "pending",
                UploadedAt = DateTime.UtcNow
            };

            _db.VendorDocuments.Add(doc);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                type = doc.DocumentType,
                name = doc.FileName,
                status = doc.Status,
                date = doc.UploadedAt.ToString("yyyy-MM-dd"),
                fileUrl = doc.FileUrl,
                url = doc.FileUrl
            });
        }
    }
}

using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1/vendor/dashboard")]
    public class VendorDashboardController : ControllerBase
    {
        private readonly EventEaseDbContext _db;

        public VendorDashboardController(EventEaseDbContext db)
        {
            _db = db;
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
        [HttpGet]
        public async Task<IActionResult> GetDashboard()
        {
            var userId = GetUserId();
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
            
            if (vendor == null)
            {
                // Create a basic vendor profile if it doesn't exist
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

            var dashboard = new
            {
                vendorName = vendor.BusinessName,
                isVerified = vendor.IsValidated,
                recentRequests = new List<object>(), 
            };
            return Ok(dashboard);
        }

        [Authorize(Policy = "Vendor")]
        [HttpGet("tasks")]
        public async Task<IActionResult> GetTasks()
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

            var tasks = new List<object>();

            if (string.IsNullOrWhiteSpace(vendor.Description))
            {
                tasks.Add(new { id = "t1", title = "Business Introduction", link = "/vendor/profile" });
            }

            if (!vendor.IsValidated)
            {
                tasks.Add(new { id = "t2", title = "Profile KYC", link = "/vendor/verification" });
            }
            else
            {
                var packages = await _db.Packages.Where(p => p.VendorId == vendor.Id).ToListAsync();
                if (!packages.Any())
                {
                    tasks.Add(new { id = "t3", title = "Create Service Package", link = "/vendor/add-service" });
                }
                else
                {
                    var packageIds = packages.Select(p => p.Id).ToList();
                    var images = await _db.PackageImages.Where(i => packageIds.Contains(i.PackageId)).ToListAsync();
                    if (!images.Any())
                    {
                        tasks.Add(new { id = "t4", title = "Upload Package Images", link = "/vendor/my-services" });
                    }
                }
            }

            return Ok(tasks);
        }
    }
}

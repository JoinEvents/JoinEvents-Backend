using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using EventEase.Infrastructure.Data;
using EventEase.Core.Entities;
using EventEase.Application.PackageManagement;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1/support/packages")]
    [Authorize(Policy = "Admin")]
    public class SupportPackageVerificationController : ControllerBase
    {
        private readonly EventEaseDbContext _db;

        public SupportPackageVerificationController(EventEaseDbContext db)
        {
            _db = db;
        }

        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingPackages()
        {
            try
            {
                // Retrieve all packages that are NOT verified and are pending review
                var pendingPackagesQuery = _db.Packages
                    .Where(p => !p.IsVerified && p.VerificationStatus != "Approved" && p.VerificationStatus != "Rejected");

                var packagesData = await pendingPackagesQuery
                    .AsNoTracking()
                    .Select(p => new
                    {
                        p.Id,
                        p.VendorId,
                        p.Category,
                        p.Name,
                        p.Description,
                        p.Theme,
                        p.Rating,
                        p.TotalReviews,
                        p.CreatedAt,
                        p.UpdatedAt,
                        p.IsVerified,
                        p.IsActive,
                        p.Experience,
                        p.VerificationStatus,
                        p.VerificationComment,
                        AddressCountry = p.Address.Country,
                        AddressState = p.Address.State,
                        AddressCity = p.Address.City,
                        AddressLocality = p.Address.Locality,
                        AddressStreet = p.Address.Street,
                        AddressLandmark = p.Address.Landmark,
                        AddressPincode = p.Address.Pincode,
                        PricingVegPrice = p.Pricing.VegPrice,
                        PricingNonVegPrice = p.Pricing.NonVegPrice,
                        PricingRoomPrice = p.Pricing.RoomPrice,
                        PricingBasePrice = p.Pricing.BasePrice,
                        PricingRent = p.Pricing.Rent,
                        PricingUnit = p.Pricing.Unit,
                        CapacityMaxGuests = p.Capacity.MaxGuests,
                        CapacityParkingCapacity = p.Capacity.ParkingCapacity,
                        CapacityTotalRooms = p.Capacity.TotalRooms,
                        PoliciesCateringPolicy = p.Policies.CateringPolicy,
                        PoliciesDecorPolicy = p.Policies.DecorPolicy,
                        PoliciesAlcoholPolicy = p.Policies.AlcoholPolicy,
                        PoliciesDjPolicy = p.Policies.DjPolicy,
                        AmenitiesHasAc = p.Amenities.HasAc,
                        AmenitiesHasPowerBackup = p.Amenities.HasPowerBackup,
                        AmenitiesHasChangingRooms = p.Amenities.HasChangingRooms,
                        AmenitiesHasParking = p.Amenities.HasParking
                    })
                    .ToListAsync();

                if (!packagesData.Any())
                {
                    return Ok(new List<PackageResponse>());
                }

                var packageIds = packagesData.Select(p => p.Id).ToList();
                var vendorIds = packagesData.Select(p => p.VendorId).Distinct().ToList();

                var vendorsList = await _db.Vendors
                    .Where(v => vendorIds.Contains(v.Id) || vendorIds.Contains(v.UserId))
                    .Select(v => new { v.Id, v.UserId, v.BusinessName })
                    .ToListAsync();

                var vendors = new Dictionary<Guid, string>();
                foreach (var v in vendorsList)
                {
                    vendors[v.Id] = v.BusinessName;
                    vendors[v.UserId] = v.BusinessName;
                }

                var images = await _db.PackageImages
                    .Where(i => packageIds.Contains(i.PackageId))
                    .ToListAsync();

                var spacesDataRaw = await _db.Packages
                    .AsNoTracking()
                    .Where(p => packageIds.Contains(p.Id))
                    .SelectMany(p => p.Spaces, (p, s) => new { PackageId = p.Id, Space = s })
                    .ToListAsync();

                var spacesDict = spacesDataRaw
                    .GroupBy(s => s.PackageId)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.Space).ToList());

                var response = packagesData.Select(p => new PackageResponse
                {
                    Id = $"pkg_{p.Id:N}",
                    VendorId = $"usr_{p.VendorId:N}",
                    VendorName = vendors.GetValueOrDefault(p.VendorId, ""),
                    Category = p.Category,
                    Name = p.Name,
                    Description = p.Description,
                    Theme = p.Theme,
                    City = p.AddressCity,
                    Address = new PackageAddressDto
                    {
                        Country = p.AddressCountry ?? "",
                        State = p.AddressState ?? "",
                        City = p.AddressCity ?? "",
                        Locality = p.AddressLocality ?? "",
                        Street = p.AddressStreet ?? "",
                        Landmark = p.AddressLandmark ?? "",
                        Pincode = p.AddressPincode ?? ""
                    },
                    Experience = p.Experience,
                    Pricing = new PackagePricingDto
                    {
                        VegPrice = p.PricingVegPrice,
                        NonVegPrice = p.PricingNonVegPrice,
                        RoomPrice = p.PricingRoomPrice,
                        BasePrice = p.PricingBasePrice,
                        Rent = p.PricingRent,
                        Unit = p.PricingUnit ?? ""
                    },
                    Capacity = new PackageCapacityDto
                    {
                        MaxGuests = p.CapacityMaxGuests,
                        ParkingCapacity = p.CapacityParkingCapacity,
                        TotalRooms = p.CapacityTotalRooms
                    },
                    Policies = new PackagePoliciesDto
                    {
                        CateringPolicy = p.PoliciesCateringPolicy ?? "",
                        DecorPolicy = p.PoliciesDecorPolicy ?? "",
                        AlcoholPolicy = p.PoliciesAlcoholPolicy ?? "",
                        DjPolicy = p.PoliciesDjPolicy ?? ""
                    },
                    Amenities = new PackageAmenitiesDto
                    {
                        HasAc = p.AmenitiesHasAc,
                        HasPowerBackup = p.AmenitiesHasPowerBackup,
                        HasChangingRooms = p.AmenitiesHasChangingRooms,
                        HasParking = p.AmenitiesHasParking
                    },
                    Spaces = spacesDict.GetValueOrDefault(p.Id, new List<PackageSpace>()).Select(s => new PackageSpaceDto
                    {
                        Name = s.Name,
                        Type = s.Type,
                        SeatingCapacity = s.SeatingCapacity,
                        FloatingCapacity = s.FloatingCapacity
                    }).ToList(),
                    Includes = new List<string>(), // Handled dynamically or simplified
                    Images = images.Where(i => i.PackageId == p.Id).Select(i => i.Url).ToList(),
                    Rating = p.Rating,
                    TotalReviews = p.TotalReviews,
                    Status = p.VerificationStatus == "Rejected" ? "Rejected" : "PendingReview",
                    VerificationStatus = p.VerificationStatus,
                    VerificationComment = p.VerificationComment,
                    IsVerified = p.IsVerified,
                    IsActive = p.IsActive,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt
                }).ToList();

                return Ok(response);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Error getting pending packages for support");
                return StatusCode(500, new { error = "Failed to load pending reviews", details = ex.Message });
            }
        }

        [HttpPost("{packageId}/verify")]
        public async Task<IActionResult> VerifyPackage(string packageId, [FromBody] Dictionary<string, string> request)
        {
            try
            {
                if (string.IsNullOrEmpty(packageId))
                    return BadRequest(new { error = "Invalid package ID" });

                Guid guid;
                if (packageId.StartsWith("pkg_"))
                {
                    if (!Guid.TryParse(packageId.Substring(4), out guid))
                        return NotFound(new { error = "Package not found" });
                }
                else if (!Guid.TryParse(packageId, out guid))
                {
                    return NotFound(new { error = "Package not found" });
                }

                var package = await _db.Packages.FirstOrDefaultAsync(p => p.Id == guid);
                if (package == null)
                {
                    return NotFound(new { error = "Package not found" });
                }

                var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.Id == package.VendorId || v.UserId == package.VendorId);
                Guid targetUserId = vendor?.UserId ?? package.VendorId;

                // Extract status and comment case-insensitively from the dictionary payload
                string statusStr = string.Empty;
                string? commentStr = null;

                if (request != null)
                {
                    foreach (var key in request.Keys)
                    {
                        if (key.Equals("status", StringComparison.OrdinalIgnoreCase))
                            statusStr = request[key] ?? string.Empty;
                        else if (key.Equals("comment", StringComparison.OrdinalIgnoreCase))
                            commentStr = request[key];
                    }
                }

                statusStr = statusStr.Trim();
                if (string.IsNullOrEmpty(statusStr))
                {
                    return BadRequest(new { error = "Status is required. Must be Approved or Rejected." });
                }

                if (statusStr.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                {
                    package.IsVerified = true;
                    package.VerificationStatus = "Approved";
                    package.VerificationComment = null;
                }
                else if (statusStr.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
                {
                    package.IsVerified = false;
                    package.VerificationStatus = "Rejected";
                    package.VerificationComment = !string.IsNullOrEmpty(commentStr) ? commentStr : "Requires corrections.";
                }
                else
                {
                    return BadRequest(new { error = "Invalid status. Must be Approved or Rejected." });
                }

                package.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                // Add in-app notification for the vendor
                try
                {
                    _db.Notifications.Add(new Notification
                    {
                        Id = Guid.NewGuid(),
                        UserId = targetUserId,
                        Title = package.IsVerified ? "Package Verification Approved 🎉" : "Package Verification Rejected ⚠️",
                        Message = package.IsVerified 
                            ? $"Congratulations! Your package '{package.Name}' has been verified and is now live."
                            : $"Your package '{package.Name}' was rejected. Reason: {package.VerificationComment}",
                        Type = "verification",
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _db.SaveChangesAsync();
                }
                catch (Exception nEx)
                {
                    Serilog.Log.Warning(nEx, "Failed to create vendor notification during verification");
                }

                return Ok(new { success = true, status = package.VerificationStatus });
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Error verifying package {PackageId}", packageId);
                return StatusCode(500, new { error = "Verification failed", details = ex.Message });
            }
        }
    }
}

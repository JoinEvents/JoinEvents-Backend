using System;
using System.Collections.Generic;
using EventEase.Application.PackageManagement;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using EventEase.Core.Entities;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1")]
    public class PackagesSearchController : ControllerBase
    {
        private readonly EventEaseDbContext _db;

        public PackagesSearchController(EventEaseDbContext db)
        {
            _db = db;
        }

        [HttpGet("packages/search")]
        public async Task<IActionResult> SearchPackages(
            [FromQuery] string? city,
            [FromQuery] string? eventTypeId,
            [FromQuery] string? category,
            [FromQuery] decimal? priceMin,
            [FromQuery] decimal? priceMax,
            [FromQuery] int? maxGuests,
            [FromQuery] string? query,
            [FromQuery] string? sortBy,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var packagesQuery = _db.Packages
                .Where(p => p.IsActive && p.IsVerified);

            if (!string.IsNullOrEmpty(city))
                packagesQuery = packagesQuery.Where(p => p.Address.City == city);

            if (!string.IsNullOrEmpty(category))
                packagesQuery = packagesQuery.Where(p => p.Category == category);

            if (!string.IsNullOrEmpty(eventTypeId))
                packagesQuery = packagesQuery.Where(p => p.Theme.Contains(eventTypeId));

            if (priceMin.HasValue)
                packagesQuery = packagesQuery.Where(p => p.Pricing.BasePrice >= priceMin.Value || p.Pricing.VegPrice >= priceMin.Value);

            if (priceMax.HasValue)
                packagesQuery = packagesQuery.Where(p => p.Pricing.BasePrice <= priceMax.Value || p.Pricing.VegPrice <= priceMax.Value);

            if (maxGuests.HasValue)
                packagesQuery = packagesQuery.Where(p => p.Capacity.MaxGuests >= maxGuests.Value);

            if (!string.IsNullOrEmpty(query))
                packagesQuery = packagesQuery.Where(p => p.Name.Contains(query) || p.Description.Contains(query) || p.Theme.Contains(query));

            switch (sortBy)
            {
                case "price_asc":
                    packagesQuery = packagesQuery.OrderBy(p => p.Pricing.BasePrice);
                    break;
                case "price_desc":
                    packagesQuery = packagesQuery.OrderByDescending(p => p.Pricing.BasePrice);
                    break;
                case "rating":
                    packagesQuery = packagesQuery.OrderByDescending(p => p.Rating);
                    break;
                case "newest":
                    packagesQuery = packagesQuery.OrderByDescending(p => p.CreatedAt);
                    break;
                default:
                    packagesQuery = packagesQuery.OrderByDescending(p => p.Rating).ThenByDescending(p => p.CreatedAt);
                    break;
            }

            var allPackages = await packagesQuery.AsNoTracking().ToListAsync();
            
            // Add null and count validation
            if (allPackages == null || allPackages.Count == 0)
            {
                return Ok(new PackageSearchResponse
                {
                    Packages = new List<PackageResponse>(),
                    TotalCount = 0,
                    Page = page,
                    PageSize = pageSize
                });
            }

            var totalCount = allPackages.Count;
            var packages = allPackages
                .OrderByDescending(p => p.Rating)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Load images and spaces separately to avoid complex SQL syntax issues
            var packageIds = packages.Select(p => p.Id).ToList();
            var allImages = await _db.PackageImages
                .Where(i => packageIds.Contains(i.PackageId))
                .ToListAsync();

            var allSpaces = await _db.Packages.AsNoTracking()
                .Where(p => packageIds.Contains(p.Id))
                .SelectMany(p => p.Spaces)
                .ToListAsync();

            // Manually link data back to packages in memory
            foreach (var p in packages)
            {
                p.Images = allImages.Where(i => i.PackageId == p.Id).ToList();
                p.Spaces = allSpaces.Where(s => s.PackageId == p.Id).ToList();
            }

            var vendorIds = packages.Select(p => p.VendorId).Distinct().ToList();
            var vendors = await _db.Vendors.Where(v => vendorIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.BusinessName);

            var responsePackages = packages.Select(p => new PackageResponse
            {
                Id = $"pkg_{p.Id:N}",
                VendorId = $"usr_{p.VendorId:N}",
                VendorName = vendors.ContainsKey(p.VendorId) ? vendors[p.VendorId] : "",
                Category = p.Category,
                Name = p.Name,
                Description = p.Description,
                Theme = p.Theme,
                City = p.Address?.City,
                Address = new PackageAddressDto
                {
                    Country = p.Address?.Country ?? "",
                    State = p.Address?.State ?? "",
                    City = p.Address?.City ?? "",
                    Locality = p.Address?.Locality ?? "",
                    Street = p.Address?.Street ?? "",
                    Landmark = p.Address?.Landmark ?? "",
                    Pincode = p.Address?.Pincode ?? ""
                },
                Experience = p.Experience,
                Pricing = new PackagePricingDto
                {
                    VegPrice = p.Pricing?.VegPrice,
                    NonVegPrice = p.Pricing?.NonVegPrice,
                    RoomPrice = p.Pricing?.RoomPrice,
                    BasePrice = p.Pricing?.BasePrice,
                    Rent = p.Pricing?.Rent,
                    Unit = p.Pricing?.Unit ?? ""
                },
                Capacity = new PackageCapacityDto
                {
                    MaxGuests = p.Capacity?.MaxGuests,
                    ParkingCapacity = p.Capacity?.ParkingCapacity,
                    TotalRooms = p.Capacity?.TotalRooms
                },
                Policies = new PackagePoliciesDto
                {
                    CateringPolicy = p.Policies?.CateringPolicy ?? "",
                    DecorPolicy = p.Policies?.DecorPolicy ?? "",
                    AlcoholPolicy = p.Policies?.AlcoholPolicy ?? "",
                    DjPolicy = p.Policies?.DjPolicy ?? ""
                },
                Amenities = new PackageAmenitiesDto
                {
                    HasAc = p.Amenities?.HasAc ?? false,
                    HasPowerBackup = p.Amenities?.HasPowerBackup ?? false,
                    HasChangingRooms = p.Amenities?.HasChangingRooms ?? false,
                    HasParking = p.Amenities?.HasParking ?? false
                },
                Spaces = p.Spaces?.Select(s => new PackageSpaceDto
                {
                    Id = null,
                    Name = s.Name,
                    Type = s.Type,
                    SeatingCapacity = s.SeatingCapacity,
                    FloatingCapacity = s.FloatingCapacity
                }).ToList() ?? new List<PackageSpaceDto>(),
                Includes = p.Includes ?? new List<string>(),
                Images = p.Images?.Select(i => i.Url).ToList() ?? new List<string>(),
                Rating = p.Rating,
                TotalReviews = p.TotalReviews,
                Status = p.Status.ToString(),
                IsVerified = p.IsVerified,
                IsActive = p.IsActive,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            }).ToList();

            return Ok(new PackageSearchResponse
            {
                Packages = responsePackages,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        [HttpGet("packages/{packageId}")]
        public async Task<IActionResult> GetPackageById(string packageId)
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

                // Retrieve package securely using AsNoTracking to avoid projection exceptions
                var p = await _db.Packages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == guid);
                if (p == null)
                    return NotFound(new { error = "Package not found" });

                // Load visual assets and spaces manually for complex EF mapping safety
                p.Images = await _db.PackageImages.AsNoTracking().Where(i => i.PackageId == guid).ToListAsync();
                p.Spaces = await _db.Packages.AsNoTracking().Where(x => x.Id == guid).SelectMany(x => x.Spaces).ToListAsync();

                // Look up parent business profiles
                var vendors = await _db.Vendors.Where(v => v.Id == p.VendorId).ToDictionaryAsync(v => v.Id, v => v.BusinessName);

                var response = new PackageResponse
                {
                    Id = $"pkg_{p.Id:N}",
                    VendorId = $"usr_{p.VendorId:N}",
                    VendorName = vendors.ContainsKey(p.VendorId) ? vendors[p.VendorId] : "",
                    Category = p.Category,
                    Name = p.Name,
                    Description = p.Description,
                    Theme = p.Theme,
                    City = p.Address?.City,
                    Address = new PackageAddressDto
                    {
                        Country = p.Address?.Country ?? "",
                        State = p.Address?.State ?? "",
                        City = p.Address?.City ?? "",
                        Locality = p.Address?.Locality ?? "",
                        Street = p.Address?.Street ?? "",
                        Landmark = p.Address?.Landmark ?? "",
                        Pincode = p.Address?.Pincode ?? ""
                    },
                    Experience = p.Experience,
                    Pricing = new PackagePricingDto
                    {
                        VegPrice = p.Pricing?.VegPrice,
                        NonVegPrice = p.Pricing?.NonVegPrice,
                        RoomPrice = p.Pricing?.RoomPrice,
                        BasePrice = p.Pricing?.BasePrice,
                        Rent = p.Pricing?.Rent,
                        Unit = p.Pricing?.Unit ?? ""
                    },
                    Capacity = new PackageCapacityDto
                    {
                        MaxGuests = p.Capacity?.MaxGuests,
                        ParkingCapacity = p.Capacity?.ParkingCapacity,
                        TotalRooms = p.Capacity?.TotalRooms
                    },
                    Policies = new PackagePoliciesDto
                    {
                        CateringPolicy = p.Policies?.CateringPolicy ?? "",
                        DecorPolicy = p.Policies?.DecorPolicy ?? "",
                        AlcoholPolicy = p.Policies?.AlcoholPolicy ?? "",
                        DjPolicy = p.Policies?.DjPolicy ?? ""
                    },
                    Amenities = new PackageAmenitiesDto
                    {
                        HasAc = p.Amenities?.HasAc ?? false,
                        HasPowerBackup = p.Amenities?.HasPowerBackup ?? false,
                        HasChangingRooms = p.Amenities?.HasChangingRooms ?? false,
                        HasParking = p.Amenities?.HasParking ?? false
                    },
                    Spaces = p.Spaces?.Select(s => new PackageSpaceDto
                    {
                        Id = null,
                        Name = s.Name,
                        Type = s.Type,
                        SeatingCapacity = s.SeatingCapacity,
                        FloatingCapacity = s.FloatingCapacity
                    }).ToList() ?? new List<PackageSpaceDto>(),
                    Includes = p.Includes ?? new List<string>(),
                    Images = p.Images?.Select(i => i.Url).ToList() ?? new List<string>(),
                    Rating = p.Rating,
                    TotalReviews = p.TotalReviews,
                    Status = p.Status.ToString(),
                    IsVerified = p.IsVerified,
                    IsActive = p.IsActive,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt
                };

                return Ok(response);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { error = "Backend Error", details = ex.Message });
            }
        }

        [HttpGet("service-categories")]
        public IActionResult GetServiceCategories()
        {
            var categories = new[]
            {
                new { id = "venue", name = "Venue", icon = "bi-building", description = "Banquet halls, lawns, resorts & farmhouses" },
                new { id = "catering", name = "Catering", icon = "bi-egg-fried", description = "Veg, non-veg & live food counters" },
                new { id = "decoration", name = "Decoration", icon = "bi-flower1", description = "Floral, theme & stage decoration" },
                new { id = "transport", name = "Transport", icon = "bi-car-front", description = "Buses, cars & luxury fleets" },
                new { id = "priest", name = "Priest", icon = "bi-fire", description = "Vedic priests for all rituals" },
                new { id = "manpower", name = "Manpower", icon = "bi-people", description = "Event staff, waiters & security" },
                new { id = "photography", name = "Photography", icon = "bi-camera", description = "Professional photos & videos" },
                new { id = "music", name = "Music & DJ", icon = "bi-music-note-beamed", description = "DJs, live bands & sound systems" }
            };

            return Ok(new { categories });
        }
    }
}

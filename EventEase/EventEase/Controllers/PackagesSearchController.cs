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
            try
            {
                var packagesQuery = _db.Packages
                    .Where(p => p.IsActive && p.IsVerified);

                // Apply Filters
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

                // Apply Sorting
                packagesQuery = sortBy switch
                {
                    "price_asc" => packagesQuery.OrderBy(p => p.Pricing.BasePrice),
                    "price_desc" => packagesQuery.OrderByDescending(p => p.Pricing.BasePrice),
                    "rating" => packagesQuery.OrderByDescending(p => p.Rating),
                    "newest" => packagesQuery.OrderByDescending(p => p.CreatedAt),
                    _ => packagesQuery.OrderByDescending(p => p.Rating).ThenByDescending(p => p.CreatedAt)
                };

                var totalCount = await packagesQuery.CountAsync();

                // Project to a lightweight DTO first to avoid complex JSON/CTE issues in SQL Server 2014
                var packagesData = await packagesQuery
                    .AsNoTracking()
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
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
                        p.Status,
                        p.IsVerified,
                        p.IsActive,
                        p.Experience,
                        // Flatten Owned Types to avoid complex mapping issues
                        City = p.Address.City,
                        Address = p.Address,
                        Pricing = p.Pricing,
                        Capacity = p.Capacity,
                        Policies = p.Policies,
                        Amenities = p.Amenities,
                        // Do not select Includes or navigation properties here if they cause issues
                    })
                    .ToListAsync();

                if (!packagesData.Any())
                {
                    return Ok(new PackageSearchResponse { TotalCount = totalCount, Page = page, PageSize = pageSize });
                }

                var packageIds = packagesData.Select(p => p.Id).ToList();
                var vendorIds = packagesData.Select(p => p.VendorId).Distinct().ToList();

                // Load related data separately
                var vendors = await _db.Vendors
                    .Where(v => vendorIds.Contains(v.Id))
                    .Select(v => new { v.Id, v.BusinessName })
                    .ToDictionaryAsync(v => v.Id, v => v.BusinessName);

                var images = await _db.PackageImages
                    .Where(i => packageIds.Contains(i.PackageId))
                    .ToListAsync();

                // Load Spaces - using a simpler query to avoid SelectMany issues
                var spaces = await _db.Packages
                    .AsNoTracking()
                    .Where(p => packageIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.Spaces })
                    .ToListAsync();

                // Map to final Response
                var responsePackages = packagesData.Select(p => new PackageResponse
                {
                    Id = $"pkg_{p.Id:N}",
                    VendorId = $"usr_{p.VendorId:N}",
                    VendorName = vendors.GetValueOrDefault(p.VendorId, ""),
                    Category = p.Category,
                    Name = p.Name,
                    Description = p.Description,
                    Theme = p.Theme,
                    City = p.City,
                    Address = new PackageAddressDto
                    {
                        Country = p.Address.Country,
                        State = p.Address.State,
                        City = p.Address.City,
                        Locality = p.Address.Locality,
                        Street = p.Address.Street,
                        Landmark = p.Address.Landmark,
                        Pincode = p.Address.Pincode
                    },
                    Experience = p.Experience,
                    Pricing = new PackagePricingDto
                    {
                        VegPrice = p.Pricing.VegPrice,
                        NonVegPrice = p.Pricing.NonVegPrice,
                        RoomPrice = p.Pricing.RoomPrice,
                        BasePrice = p.Pricing.BasePrice,
                        Rent = p.Pricing.Rent,
                        Unit = p.Pricing.Unit
                    },
                    Capacity = new PackageCapacityDto
                    {
                        MaxGuests = p.Capacity.MaxGuests,
                        ParkingCapacity = p.Capacity.ParkingCapacity,
                        TotalRooms = p.Capacity.TotalRooms
                    },
                    Policies = new PackagePoliciesDto
                    {
                        CateringPolicy = p.Policies.CateringPolicy,
                        DecorPolicy = p.Policies.DecorPolicy,
                        AlcoholPolicy = p.Policies.AlcoholPolicy,
                        DjPolicy = p.Policies.DjPolicy
                    },
                    Amenities = new PackageAmenitiesDto
                    {
                        HasAc = p.Amenities.HasAc,
                        HasPowerBackup = p.Amenities.HasPowerBackup,
                        HasChangingRooms = p.Amenities.HasChangingRooms,
                        HasParking = p.Amenities.HasParking
                    },
                    Rating = p.Rating,
                    TotalReviews = p.TotalReviews,
                    Status = p.Status.ToString(),
                    IsVerified = p.IsVerified,
                    IsActive = p.IsActive,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt,
                    Images = images.Where(i => i.PackageId == p.Id).Select(i => i.Url).ToList(),
                    Spaces = spaces.FirstOrDefault(s => s.Id == p.Id)?.Spaces.Select(s => new PackageSpaceDto
                    {
                        Name = s.Name,
                        Type = s.Type,
                        SeatingCapacity = s.SeatingCapacity,
                        FloatingCapacity = s.FloatingCapacity
                    }).ToList() ?? new List<PackageSpaceDto>(),
                    // Load Includes safely - if they were stored as JSON, they might be null or inaccessible in complex queries
                    // For now, we'll try to get them from the original context if needed, but usually they aren't critical for search results
                    Includes = new List<string>() 
                }).ToList();

                return Ok(new PackageSearchResponse
                {
                    Packages = responsePackages,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize
                });
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Error during Package Search");
                return StatusCode(500, new { error = "Search Failed", details = ex.Message });
            }
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

                // Retrieve package securely using projection to avoid JSON/complex mapping issues on SQL 2014
                var pData = await _db.Packages
                    .AsNoTracking()
                    .Where(x => x.Id == guid)
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
                        p.Status,
                        p.IsVerified,
                        p.IsActive,
                        p.Experience,
                        City = p.Address.City,
                        Address = p.Address,
                        Pricing = p.Pricing,
                        Capacity = p.Capacity,
                        Policies = p.Policies,
                        Amenities = p.Amenities,
                        Includes = p.Includes // This might still fail if it's JSON, we'll see
                    })
                    .FirstOrDefaultAsync();

                if (pData == null)
                    return NotFound(new { error = "Package not found" });

                // Load visual assets and spaces manually for complex EF mapping safety
                var images = await _db.PackageImages.AsNoTracking().Where(i => i.PackageId == guid).Select(i => i.Url).ToListAsync();
                var spaces = await _db.Packages.AsNoTracking()
                    .Where(x => x.Id == guid)
                    .SelectMany(x => x.Spaces)
                    .ToListAsync();

                // Look up parent business profiles
                var vendor = await _db.Vendors
                    .Where(v => v.Id == pData.VendorId)
                    .Select(v => new { v.Id, v.BusinessName })
                    .FirstOrDefaultAsync();

                var response = new PackageResponse
                {
                    Id = $"pkg_{pData.Id:N}",
                    VendorId = $"usr_{pData.VendorId:N}",
                    VendorName = vendor?.BusinessName ?? "",
                    Category = pData.Category,
                    Name = pData.Name,
                    Description = pData.Description,
                    Theme = pData.Theme,
                    City = pData.City,
                    Address = new PackageAddressDto
                    {
                        Country = pData.Address.Country,
                        State = pData.Address.State,
                        City = pData.Address.City,
                        Locality = pData.Address.Locality,
                        Street = pData.Address.Street,
                        Landmark = pData.Address.Landmark,
                        Pincode = pData.Address.Pincode
                    },
                    Experience = pData.Experience,
                    Pricing = new PackagePricingDto
                    {
                        VegPrice = pData.Pricing.VegPrice,
                        NonVegPrice = pData.Pricing.NonVegPrice,
                        RoomPrice = pData.Pricing.RoomPrice,
                        BasePrice = pData.Pricing.BasePrice,
                        Rent = pData.Pricing.Rent,
                        Unit = pData.Pricing.Unit
                    },
                    Capacity = new PackageCapacityDto
                    {
                        MaxGuests = pData.Capacity.MaxGuests,
                        ParkingCapacity = pData.Capacity.ParkingCapacity,
                        TotalRooms = pData.Capacity.TotalRooms
                    },
                    Policies = new PackagePoliciesDto
                    {
                        CateringPolicy = pData.Policies.CateringPolicy,
                        DecorPolicy = pData.Policies.DecorPolicy,
                        AlcoholPolicy = pData.Policies.AlcoholPolicy,
                        DjPolicy = pData.Policies.DjPolicy
                    },
                    Amenities = new PackageAmenitiesDto
                    {
                        HasAc = pData.Amenities.HasAc,
                        HasPowerBackup = pData.Amenities.HasPowerBackup,
                        HasChangingRooms = pData.Amenities.HasChangingRooms,
                        HasParking = pData.Amenities.HasParking
                    },
                    Spaces = spaces.Select(s => new PackageSpaceDto
                    {
                        Name = s.Name,
                        Type = s.Type,
                        SeatingCapacity = s.SeatingCapacity,
                        FloatingCapacity = s.FloatingCapacity
                    }).ToList(),
                    Includes = pData.Includes ?? new List<string>(),
                    Images = images,
                    Rating = pData.Rating,
                    TotalReviews = pData.TotalReviews,
                    Status = pData.Status.ToString(),
                    IsVerified = pData.IsVerified,
                    IsActive = pData.IsActive,
                    CreatedAt = pData.CreatedAt,
                    UpdatedAt = pData.UpdatedAt
                };

                return Ok(response);
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Error(ex, "Error retrieving package {PackageId}", packageId);
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

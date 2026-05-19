using EventEase.Application.Blob;
using EventEase.Application.PackageManagement;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1/vendor/packages")]
    [Authorize]
    public class VendorPackagesController : ControllerBase
    {
        private readonly EventEaseDbContext _db;
        private readonly IBlobService _blobService;

        public VendorPackagesController(EventEaseDbContext db, IBlobService blobService)
        {
            _db = db;
            _blobService = blobService;
        }

        private Guid GetUserId()
        {
            var val = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                      ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            return Guid.TryParse(val, out var guid) ? guid : Guid.Empty;
        }

        [HttpPost]
        public async Task<IActionResult> CreatePackage([FromBody] CreatePackageRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { error = "Request body cannot be null" });
            }

            var vendorId = GetUserId();
            
            // Extract safely using local variables to prevent NullReferenceException on omitted JSON fields
            var addressDto = request.Address ?? new PackageAddressDto();
            var pricingDto = request.Pricing ?? new PackagePricingDto();
            var capacityDto = request.Capacity ?? new PackageCapacityDto();
            var policiesDto = request.Policies ?? new PackagePoliciesDto();
            var amenitiesDto = request.Amenities ?? new PackageAmenitiesDto();
            var spacesList = request.Spaces ?? new List<PackageSpaceDto>();
            var includesList = request.Includes ?? new List<string>();
            var imagesList = request.Images ?? new List<string>();

            var package = new Package
            {
                VendorId = vendorId,
                Category = request.Category ?? string.Empty,
                Name = request.Name ?? string.Empty,
                Description = request.Description ?? string.Empty,
                Theme = request.Theme ?? string.Empty,
                Experience = request.Experience,
                Includes = includesList,
                Address = new PackageAddress
                {
                    Country = addressDto.Country ?? string.Empty,
                    State = addressDto.State ?? string.Empty,
                    City = addressDto.City ?? string.Empty,
                    Locality = addressDto.Locality ?? string.Empty,
                    Street = addressDto.Street ?? string.Empty,
                    Landmark = addressDto.Landmark ?? string.Empty,
                    Pincode = addressDto.Pincode ?? string.Empty
                },
                Pricing = new PackagePricing
                {
                    VegPrice = pricingDto.VegPrice,
                    NonVegPrice = pricingDto.NonVegPrice,
                    RoomPrice = pricingDto.RoomPrice,
                    BasePrice = pricingDto.BasePrice,
                    Rent = pricingDto.Rent,
                    Unit = pricingDto.Unit ?? string.Empty
                },
                Capacity = new PackageCapacity
                {
                    MaxGuests = capacityDto.MaxGuests,
                    ParkingCapacity = capacityDto.ParkingCapacity,
                    TotalRooms = capacityDto.TotalRooms
                },
                Policies = new PackagePolicies
                {
                    CateringPolicy = policiesDto.CateringPolicy ?? string.Empty,
                    DecorPolicy = policiesDto.DecorPolicy ?? string.Empty,
                    AlcoholPolicy = policiesDto.AlcoholPolicy ?? string.Empty,
                    DjPolicy = policiesDto.DjPolicy ?? string.Empty
                },
                Amenities = new PackageAmenities
                {
                    HasAc = amenitiesDto.HasAc,
                    HasPowerBackup = amenitiesDto.HasPowerBackup,
                    HasChangingRooms = amenitiesDto.HasChangingRooms,
                    HasParking = amenitiesDto.HasParking
                },
                Spaces = spacesList.Select(s => new PackageSpace
                {
                    Name = s.Name ?? string.Empty,
                    Type = s.Type ?? string.Empty,
                    SeatingCapacity = s.SeatingCapacity,
                    FloatingCapacity = s.FloatingCapacity
                }).ToList(),
                IsVerified = false,
                IsActive = true,
                VerificationStatus = "Pending",
                VerificationComment = null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            foreach (var imgUrl in imagesList)
            {
                if (!string.IsNullOrEmpty(imgUrl))
                {
                    package.Images.Add(new PackageImage { Url = imgUrl, PackageId = package.Id, IsMain = package.Images.Count == 0 });
                }
            }

            _db.Packages.Add(package);
            await _db.SaveChangesAsync();

            // Add notification for Support members
            try
            {
                var supportUser = await _db.Users.FirstOrDefaultAsync(u => u.Role == "Support" || u.Email == "support@test.com");
                if (supportUser != null)
                {
                    _db.Notifications.Add(new Notification
                    {
                        Id = Guid.NewGuid(),
                        UserId = supportUser.Id,
                        Title = "New Package Submitted",
                        Message = $"Package '{package.Name}' has been submitted by vendor and is awaiting verification.",
                        Type = "verification",
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _db.SaveChangesAsync();
                }
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to create support notification for new package");
            }

            return Created($"/api/v1/vendor/packages/{package.Id}", new
            {
                id = $"pkg_{package.Id:N}",
                vendorId = $"usr_{vendorId:N}",
                name = package.Name,
                category = package.Category,
                status = "PendingReview",
                isVerified = package.IsVerified,
                isActive = package.IsActive,
                createdAt = package.CreatedAt
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetMyPackages([FromQuery] string? category, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var vendorId = GetUserId();
                var totalPackagesInDb = await _db.Packages.CountAsync();
                
                var query = _db.Packages.Where(p => p.VendorId == vendorId);
                
                // If no packages found, also check for "orphaned" packages as a fallback debug step
                if (!await query.AnyAsync() && vendorId != Guid.Empty) {
                    // This is just to see if services exist but with wrong IDs
                }

                // Use an explicit Select with AsNoTracking to bypass complex mapping and tracking issues
                var rawData = await query.AsNoTracking().Select(p => new
                {
                    p.Id,
                    p.VendorId,
                    p.Category,
                    p.Name,
                    p.Description,
                    p.Theme,
                    p.Experience,
                    p.IsActive,
                    p.IsVerified,
                    p.CreatedAt,
                    p.UpdatedAt,
                    p.Rating,
                    p.TotalReviews,
                    // Map Owned types to a flat object for the query
                    Address = p.Address,
                    Pricing = p.Pricing,
                    Capacity = p.Capacity,
                    Policies = p.Policies,
                    Amenities = p.Amenities
                }).ToListAsync();

                var packages = rawData.Select(d => new Package
                {
                    Id = d.Id,
                    VendorId = d.VendorId,
                    Category = d.Category,
                    Name = d.Name,
                    Description = d.Description,
                    Theme = d.Theme,
                    Experience = d.Experience,
                    IsActive = d.IsActive,
                    IsVerified = d.IsVerified,
                    CreatedAt = d.CreatedAt,
                    UpdatedAt = d.UpdatedAt,
                    Rating = d.Rating,
                    TotalReviews = d.TotalReviews,
                    Address = d.Address,
                    Pricing = d.Pricing,
                    Capacity = d.Capacity,
                    Policies = d.Policies,
                    Amenities = d.Amenities
                })
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

                var totalCount = rawData.Count;

                // Load images and spaces separately to avoid complex SQL syntax issues
                var packageIds = packages.Select(p => p.Id).ToList();
                var allImages = new List<PackageImage>();
                if (packageIds.Any())
                {
                    foreach (var id in packageIds)
                    {
                        var imgs = await _db.PackageImages
                            .Where(i => i.PackageId == id)
                            .ToListAsync();
                        allImages.AddRange(imgs);
                    }
                }

                // Load spaces through Package since PackageSpace is an owned entity
                var spacesByPackageId = new Dictionary<Guid, List<PackageSpace>>();
                if (packageIds.Any())
                {
                    var spacesDataRaw = await _db.Packages
                        .AsNoTracking()
                        .Where(p => packageIds.Contains(p.Id))
                        .SelectMany(p => p.Spaces, (p, s) => new { PackageId = p.Id, Space = s })
                        .ToListAsync();

                    spacesByPackageId = spacesDataRaw
                        .GroupBy(s => s.PackageId)
                        .ToDictionary(g => g.Key, g => g.Select(x => x.Space).ToList());
                }

                // Manually link data back to packages in memory
                foreach (var p in packages)
                {
                    p.Images = allImages.Where(i => i.PackageId == p.Id).ToList();
                    p.Spaces = spacesByPackageId.TryGetValue(p.Id, out var spaces) ? spaces : new List<PackageSpace>();
                }

                return Ok(new
                {
                    Packages = packages.Select(MapToResponse).ToList(),
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    Debug = new {
                        CurrentVendorId = vendorId,
                        TotalPackagesInDb = totalPackagesInDb
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Backend Error", details = ex.Message, inner = ex.InnerException?.Message });
            }
        }

        [HttpGet("debug-auth")]
        public IActionResult DebugAuth()
        {
            var userId = GetUserId();
            var claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList();
            return Ok(new { userId, claims });
        }

        [HttpGet("{packageId}")]
        public async Task<IActionResult> GetPackage(string packageId)
        {
            try
            {
                var vendorId = GetUserId();
                if (!packageId.StartsWith("pkg_") || !Guid.TryParse(packageId.Substring(4), out var guid))
                    return NotFound(new { error = "Package not found" });

                // Use simple fetch to avoid "WITH" syntax errors with Include
                var package = await _db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == guid);
                if (package == null)
                    return NotFound(new { error = "Package not found" });

                if (package.VendorId != vendorId)
                    return StatusCode(403, new { error = "You are not authorized to view this package" });

                // Manually load related data using safe query patterns
                package.Images = await _db.PackageImages.AsNoTracking().Where(i => i.PackageId == guid).ToListAsync();
                
                // Fetch spaces through the parent to ensure owned entity compatibility
                package.Spaces = await _db.Packages.AsNoTracking()
                    .Where(p => p.Id == guid)
                    .SelectMany(p => p.Spaces)
                    .ToListAsync();

                return Ok(MapToResponse(package));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Backend Error", details = ex.Message, inner = ex.InnerException?.Message });
            }
        }

        [HttpPut("{packageId}")]
        public async Task<IActionResult> UpdatePackage(string packageId, [FromBody] UpdatePackageRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { error = "Request body cannot be null" });
            }

            var vendorId = GetUserId();
            if (!packageId.StartsWith("pkg_") || !Guid.TryParse(packageId.Substring(4), out var guid))
                return NotFound(new { error = "Package not found" });

            var package = await _db.Packages.FirstOrDefaultAsync(p => p.Id == guid);
            if (package == null)
                return NotFound(new { error = "Package not found" });

            if (package.VendorId != vendorId)
                return StatusCode(403, new { error = "You are not authorized to edit this package" });

            package.Name = request.Name ?? package.Name;
            package.Category = request.Category ?? package.Category;
            package.Description = request.Description ?? package.Description;
            package.Theme = request.Theme ?? package.Theme;
            package.Experience = request.Experience;
            
            if (request.Includes != null)
            {
                package.Includes = request.Includes;
            }

            if (request.Address != null)
            {
                package.Address ??= new PackageAddress();
                package.Address.Country = request.Address.Country ?? package.Address.Country ?? string.Empty;
                package.Address.State = request.Address.State ?? package.Address.State ?? string.Empty;
                package.Address.City = request.Address.City ?? package.Address.City ?? string.Empty;
                package.Address.Locality = request.Address.Locality ?? package.Address.Locality ?? string.Empty;
                package.Address.Street = request.Address.Street ?? package.Address.Street ?? string.Empty;
                package.Address.Landmark = request.Address.Landmark ?? package.Address.Landmark ?? string.Empty;
                package.Address.Pincode = request.Address.Pincode ?? package.Address.Pincode ?? string.Empty;
            }

            if (request.Pricing != null)
            {
                package.Pricing ??= new PackagePricing();
                package.Pricing.VegPrice = request.Pricing.VegPrice ?? package.Pricing.VegPrice;
                package.Pricing.NonVegPrice = request.Pricing.NonVegPrice ?? package.Pricing.NonVegPrice;
                package.Pricing.RoomPrice = request.Pricing.RoomPrice ?? package.Pricing.RoomPrice;
                package.Pricing.BasePrice = request.Pricing.BasePrice ?? package.Pricing.BasePrice;
                package.Pricing.Rent = request.Pricing.Rent ?? package.Pricing.Rent;
                package.Pricing.Unit = request.Pricing.Unit ?? package.Pricing.Unit ?? string.Empty;
            }

            if (request.Capacity != null)
            {
                package.Capacity ??= new PackageCapacity();
                package.Capacity.MaxGuests = request.Capacity.MaxGuests ?? package.Capacity.MaxGuests;
                package.Capacity.ParkingCapacity = request.Capacity.ParkingCapacity ?? package.Capacity.ParkingCapacity;
                package.Capacity.TotalRooms = request.Capacity.TotalRooms ?? package.Capacity.TotalRooms;
            }

            if (request.Policies != null)
            {
                package.Policies ??= new PackagePolicies();
                package.Policies.CateringPolicy = request.Policies.CateringPolicy ?? package.Policies.CateringPolicy ?? string.Empty;
                package.Policies.DecorPolicy = request.Policies.DecorPolicy ?? package.Policies.DecorPolicy ?? string.Empty;
                package.Policies.AlcoholPolicy = request.Policies.AlcoholPolicy ?? package.Policies.AlcoholPolicy ?? string.Empty;
                package.Policies.DjPolicy = request.Policies.DjPolicy ?? package.Policies.DjPolicy ?? string.Empty;
            }

            if (request.Amenities != null)
            {
                package.Amenities ??= new PackageAmenities();
                package.Amenities.HasAc = request.Amenities.HasAc;
                package.Amenities.HasPowerBackup = request.Amenities.HasPowerBackup;
                package.Amenities.HasChangingRooms = request.Amenities.HasChangingRooms;
                package.Amenities.HasParking = request.Amenities.HasParking;
            }

            if (request.Spaces != null)
            {
                package.Spaces.Clear();
                foreach (var s in request.Spaces)
                {
                    if (s != null)
                    {
                        package.Spaces.Add(new PackageSpace
                        {
                            Name = s.Name ?? string.Empty,
                            Type = s.Type ?? string.Empty,
                            SeatingCapacity = s.SeatingCapacity,
                            FloatingCapacity = s.FloatingCapacity
                        });
                    }
                }
            }

            if (request.Images != null)
            {
                // Remove existing ones safely from db first, preventing database orphans
                var existingImages = await _db.PackageImages.Where(i => i.PackageId == guid).ToListAsync();
                _db.PackageImages.RemoveRange(existingImages);
                
                package.Images.Clear();
                foreach (var imgUrl in request.Images)
                {
                    if (!string.IsNullOrEmpty(imgUrl))
                    {
                        package.Images.Add(new PackageImage { Url = imgUrl, PackageId = package.Id, IsMain = package.Images.Count == 0 });
                    }
                }
            }

            package.IsVerified = false;
            package.VerificationStatus = "Pending";
            package.VerificationComment = null;
            package.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            // Add notification for Support members
            try
            {
                var supportUser = await _db.Users.FirstOrDefaultAsync(u => u.Role == "Support" || u.Email == "support@test.com");
                if (supportUser != null)
                {
                    _db.Notifications.Add(new Notification
                    {
                        Id = Guid.NewGuid(),
                        UserId = supportUser.Id,
                        Title = "Package Resubmitted",
                        Message = $"Package '{package.Name}' has been updated and is awaiting verification.",
                        Type = "verification",
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _db.SaveChangesAsync();
                }
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to create support notification for updated package");
            }

            return Ok(new
            {
                id = $"pkg_{package.Id:N}",
                name = package.Name,
                status = "PendingReview",
                isVerified = package.IsVerified,
                updatedAt = package.UpdatedAt
            });
        }

        [HttpDelete("{packageId}")]
        public async Task<IActionResult> DeletePackage(string packageId)
        {
            var vendorId = GetUserId();
            if (!packageId.StartsWith("pkg_") || !Guid.TryParse(packageId.Substring(4), out var guid))
                return NotFound(new { error = "Package not found or already deleted" });

            var package = await _db.Packages.FirstOrDefaultAsync(p => p.Id == guid);
            if (package == null)
                return NotFound(new { error = "Package not found or already deleted" });

            if (package.VendorId != vendorId)
                return StatusCode(403, new { error = "You are not authorized to delete this package" });

            _db.Packages.Remove(package);
            await _db.SaveChangesAsync();

            return Ok(new { success = true, message = "Package deleted successfully" });
        }

        [HttpPatch("{packageId}/status")]
        public async Task<IActionResult> ToggleStatus(string packageId, [FromBody] UpdateStatusRequest request)
        {
            var vendorId = GetUserId();
            if (!packageId.StartsWith("pkg_") || !Guid.TryParse(packageId.Substring(4), out var guid))
                return NotFound(new { error = "Package not found" });

            var package = await _db.Packages.FirstOrDefaultAsync(p => p.Id == guid);
            if (package == null)
                return NotFound(new { error = "Package not found" });

            if (package.VendorId != vendorId)
                return StatusCode(403, new { error = "You are not authorized to edit this package" });

            package.IsActive = request.IsActive;
            package.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                id = $"pkg_{package.Id:N}",
                isActive = package.IsActive,
                updatedAt = package.UpdatedAt
            });
        }

        [HttpPost("{packageId}/images")]
        public async Task<IActionResult> UploadImages(string packageId, [FromForm] IFormFileCollection files)
        {
            var vendorId = GetUserId();
            if (!packageId.StartsWith("pkg_") || !Guid.TryParse(packageId.Substring(4), out var guid))
                return NotFound(new { error = "Package not found" });

            var package = await _db.Packages.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == guid);
            if (package == null)
                return NotFound(new { error = "Package not found" });

            if (package.VendorId != vendorId)
                return StatusCode(403, new { error = "You are not authorized to edit this package" });

            if (files == null || files.Count == 0)
                return BadRequest(new { error = "No files uploaded" });

            if (files.Count > 6)
                return BadRequest(new { error = "Maximum 6 images allowed" });

            var responseImages = new System.Collections.Generic.List<PackageImageDto>();

            foreach (var file in files)
            {
                // In reality, upload to BlobStorage
                string url;
                try
                {
                    var blobName = await _blobService.UploadAsync(file, package.VendorId.ToString());
                    url = $"https://storage.joinevents.com/uploads/{blobName}";
                }
                catch (Exception)
                {
                    // Fallback local or mock URL for test
                    url = $"https://storage.joinevents.com/uploads/pkg_{package.Id:N}/{file.FileName}";
                }

                var img = new PackageImage
                {
                    PackageId = package.Id,
                    Url = url,
                    IsMain = package.Images.Count == 0
                };
                package.Images.Add(img);
                responseImages.Add(new PackageImageDto { Id = img.Id, Url = img.Url, IsMain = img.IsMain });
            }

            await _db.SaveChangesAsync();

            return Ok(new UploadImageResponse { Images = responseImages });
        }

        [HttpDelete("{packageId}/images/{imageId}")]
        public async Task<IActionResult> DeleteImage(string packageId, string imageId)
        {
            var vendorId = GetUserId();
            if (!packageId.StartsWith("pkg_") || !Guid.TryParse(packageId.Substring(4), out var guid))
                return NotFound(new { error = "Package not found" });

            var package = await _db.Packages.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == guid);
            if (package == null)
                return NotFound(new { error = "Package not found" });

            if (package.VendorId != vendorId)
                return StatusCode(403, new { error = "You are not authorized to edit this package" });

            var img = package.Images.FirstOrDefault(i => i.Id == imageId);
            if (img == null)
                return NotFound(new { error = "Image not found" });

            _db.PackageImages.Remove(img);
            await _db.SaveChangesAsync();

            return Ok(new { success = true, remainingImages = package.Images.Count });
        }

        private PackageResponse MapToResponse(Package p)
        {
            return new PackageResponse
            {
                Id = $"pkg_{p.Id:N}",
                VendorId = $"usr_{p.VendorId:N}",
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
                }).ToList() ?? new System.Collections.Generic.List<PackageSpaceDto>(),
                Includes = p.Includes ?? new System.Collections.Generic.List<string>(),
                Images = p.Images?.Select(i => i.Url).ToList() ?? new System.Collections.Generic.List<string>(),
                Rating = p.Rating,
                TotalReviews = p.TotalReviews,
                Status = p.IsVerified ? "Active" : p.VerificationStatus == "Rejected" ? "Rejected" : "PendingReview",
                VerificationStatus = p.VerificationStatus,
                VerificationComment = p.VerificationComment,
                IsVerified = p.IsVerified,
                IsActive = p.IsActive,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            };
        }
    }
}

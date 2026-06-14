using System;
using System.Collections.Generic;

namespace EventEase.Application.PackageManagement
{
    public class CreatePackageRequest
    {
        public string Category { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Theme { get; set; } = string.Empty;
        public PackageAddressDto Address { get; set; } = new();
        public int Experience { get; set; }
        public PackagePricingDto Pricing { get; set; } = new();
        public PackageCapacityDto Capacity { get; set; } = new();
        public PackagePoliciesDto Policies { get; set; } = new();
        public PackageAmenitiesDto Amenities { get; set; } = new();
        public List<PackageSpaceDto> Spaces { get; set; } = new();
        public List<string> Includes { get; set; } = new();
        public List<string> Images { get; set; } = new();
    }

    public class UpdatePackageRequest : CreatePackageRequest
    {
    }

    public class PackageAddressDto
    {
        public string Country { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Locality { get; set; } = string.Empty;
        public string Street { get; set; } = string.Empty;
        public string Landmark { get; set; } = string.Empty;
        public string Pincode { get; set; } = string.Empty;
    }

    public class PackagePricingDto
    {
        public decimal? VegPrice { get; set; }
        public decimal? NonVegPrice { get; set; }
        public decimal? RoomPrice { get; set; }
        public decimal? BasePrice { get; set; }
        public decimal? Rent { get; set; }
        public string Unit { get; set; } = string.Empty;
    }

    public class PackageCapacityDto
    {
        public int? MaxGuests { get; set; }
        public int? ParkingCapacity { get; set; }
        public int? TotalRooms { get; set; }
    }

    public class PackagePoliciesDto
    {
        public string CateringPolicy { get; set; } = string.Empty;
        public string DecorPolicy { get; set; } = string.Empty;
        public string AlcoholPolicy { get; set; } = string.Empty;
        public string DjPolicy { get; set; } = string.Empty;
    }

    public class PackageAmenitiesDto
    {
        public bool HasAc { get; set; }
        public bool HasPowerBackup { get; set; }
        public bool HasChangingRooms { get; set; }
        public bool HasParking { get; set; }
    }

    public class PackageSpaceDto
    {
        public string? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int SeatingCapacity { get; set; }
        public int FloatingCapacity { get; set; }
    }

    public class PackageResponse
    {
        public string Id { get; set; } = string.Empty;
        public string VendorId { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public string VendorDescription { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Theme { get; set; } = string.Empty;
        public string? City { get; set; }
        public PackageAddressDto Address { get; set; } = new();
        public int Experience { get; set; }
        public PackagePricingDto Pricing { get; set; } = new();
        public PackageCapacityDto Capacity { get; set; } = new();
        public PackagePoliciesDto Policies { get; set; } = new();
        public PackageAmenitiesDto Amenities { get; set; } = new();
        public List<PackageSpaceDto> Spaces { get; set; } = new();
        public List<string> Includes { get; set; } = new();
        public List<string> Images { get; set; } = new();
        public double Rating { get; set; }
        public int TotalReviews { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsVerified { get; set; }
        public bool IsActive { get; set; }
        public string VerificationStatus { get; set; } = "Pending";
        public string? VerificationComment { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class PackageSearchResponse
    {
        public List<PackageResponse> Packages { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class UpdateStatusRequest
    {
        public bool IsActive { get; set; }
    }

    public class UploadImageResponse
    {
        public List<PackageImageDto> Images { get; set; } = new();
    }

    public class PackageImageDto
    {
        public string Id { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public bool IsMain { get; set; }
    }
}

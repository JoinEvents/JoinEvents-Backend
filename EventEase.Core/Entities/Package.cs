using System;
using System.Collections.Generic;
using EventEase.Core.Enums;

namespace EventEase.Core.Entities
{
    public class Package
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid VendorId { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Theme { get; set; } = string.Empty;
        
        public PackageAddress Address { get; set; } = new();
        public int Experience { get; set; }
        
        public PackagePricing Pricing { get; set; } = new();
        public PackageCapacity Capacity { get; set; } = new();
        public PackagePolicies Policies { get; set; } = new();
        public PackageAmenities Amenities { get; set; } = new();
        
        // Navigation properties for nested arrays and relationships
        public ICollection<PackageSpace> Spaces { get; set; } = new List<PackageSpace>();
        
        // EF Core 8 natively supports storing list of primitives as JSON
        public List<string> Includes { get; set; } = new();
        
        // Relationship to images
        public ICollection<PackageImage> Images { get; set; } = new List<PackageImage>();

        // System managed status fields
        public bool IsVerified { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public string VerificationStatus { get; set; } = "Pending";
        public string? VerificationComment { get; set; }
        
        // Aggregates
        public double Rating { get; set; } = 0.0;
        public int TotalReviews { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class PackageAddress
    {
        public string Country { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Locality { get; set; } = string.Empty;
        public string Street { get; set; } = string.Empty;
        public string Landmark { get; set; } = string.Empty;
        public string Pincode { get; set; } = string.Empty;
    }

    public class PackagePricing
    {
        public decimal? VegPrice { get; set; }
        public decimal? NonVegPrice { get; set; }
        public decimal? RoomPrice { get; set; }
        public decimal? BasePrice { get; set; }
        public decimal? Rent { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string? Cuisine { get; set; }
        public string? CuisineType { get; set; }
    }

    public class PackageCapacity
    {
        public int? MaxGuests { get; set; }
        public int? ParkingCapacity { get; set; }
        public int? TotalRooms { get; set; }
    }

    public class PackagePolicies
    {
        public string CateringPolicy { get; set; } = string.Empty;
        public string DecorPolicy { get; set; } = string.Empty;
        public string AlcoholPolicy { get; set; } = string.Empty;
        public string DjPolicy { get; set; } = string.Empty;
    }

    public class PackageAmenities
    {
        public bool HasAc { get; set; }
        public bool HasPowerBackup { get; set; }
        public bool HasChangingRooms { get; set; }
        public bool HasParking { get; set; }
    }
}

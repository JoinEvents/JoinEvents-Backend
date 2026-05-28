using EventEase.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EventEase.Infrastructure.Data
{
    public static class DbInitializer
    {
        public static void Seed(EventEaseDbContext db)
        {
            // ── Always ensure admin & support accounts exist ──────────────────
            EnsureAdminUsers(db);
            EnsureTiers(db);

            // ── Full seed only on empty DB ────────────────────────────────────
            if (db.Users.Count() > 2) return; // vendor + customer already seeded

            var user = new User { Id = Guid.NewGuid(), Name = "Test User", Phone = "9999999999", Email = "user@test.com", Role = "Customer", PasswordHash = Hash("test") };
            var vendorUser = new User { Id = Guid.NewGuid(), Name = "Vendor User", Phone = "8888888888", Email = "vendor@test.com", Role = "Vendor", PasswordHash = Hash("test") };
            var AdminUser = new User { Id = Guid.NewGuid(), Name = "Admin User", Phone = "8888888888", Email = "admin@test.com", Role = "Admin", PasswordHash = Hash("test") };
            var SupportUser = new User { Id = Guid.NewGuid(), Name = "Support User", Phone = "8888888888", Email = "support@test.com", Role = "Support", PasswordHash = Hash("test") };
            db.Users.AddRange(user, vendorUser, AdminUser, SupportUser);

            var vendor = new Vendor { Id = Guid.NewGuid(), UserId = vendorUser.Id, BusinessName = "Dream Weddings", Description = "Full service wedding planner", Location = "Hyderabad", IsValidated = true };
            db.Vendors.Add(vendor);

            var services = new List<Service>
            {
                new Service { Id = Guid.NewGuid(), VendorId = vendor.Id, Name = "Catering",     Category = "Food",  Price = 50000 },
                new Service { Id = Guid.NewGuid(), VendorId = vendor.Id, Name = "Photography",  Category = "Media", Price = 30000 },
                new Service { Id = Guid.NewGuid(), VendorId = vendor.Id, Name = "Decoration",   Category = "Decor", Price = 20000 }
            };
            db.Services.AddRange(services);

            db.Packages.Add(new Package
            {
                Id = Guid.NewGuid(),
                VendorId = vendor.Id,
                Name = "Wedding Basic",
                Category = "Wedding",
                Pricing = new PackagePricing { BasePrice = 100000 },
                Includes = services.Select(s => s.Name).ToList(),
                IsActive = true,
                IsVerified = true
            });

            db.SaveChanges();
        }

        /// <summary>
        /// Adds admin/support accounts if they don't already exist.
        /// Safe to call on any existing database.
        /// </summary>
        private static void EnsureAdminUsers(EventEaseDbContext db)
        {
            bool changed = false;

            if (!db.Users.Any(u => u.Email == "admin@test.com"))
            {
                db.Users.Add(new User
                {
                    Id = Guid.NewGuid(),
                    Name = "Priya Nair",
                    Phone = "9988776655",
                    Email = "admin@test.com",
                    Role = "Admin",
                    PasswordHash = Hash("test")
                });
                changed = true;
            }

            if (!db.Users.Any(u => u.Email == "support@test.com"))
            {
                db.Users.Add(new User
                {
                    Id = Guid.NewGuid(),
                    Name = "Rahul Support",
                    Phone = "9900011223",
                    Email = "support@test.com",
                    Role = "Support",
                    PasswordHash = Hash("test")
                });
                changed = true;
            }

            if (changed) db.SaveChanges();
        }

        private static void EnsureTiers(EventEaseDbContext db)
        {
            if (db.Tiers.Any()) return;

            var categories = db.EventCategories.ToList();
            if (categories.Count == 0) return;

            // Find wedding category or fallback to first
            var weddingCategory = categories.FirstOrDefault(c => c.CategoryKey.ToLower() == "wedding" || c.Name.ToLower().Contains("wedding")) ?? categories.First();
            
            // Seed a "Platinum" tier for Wedding
            var premiumTier = new Tier
            {
                Id = Guid.NewGuid(),
                Name = "Platinum",
                CategoryId = weddingCategory.Id,
                Description = "High-end luxury wedding package with complete VIP accommodations.",
                IsActive = true,
                Icon = "bi-gem",
                Gradient = "linear-gradient(135deg,#0EA5E9,#6B21A8)",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            premiumTier.PriceRanges.AddRange(new List<TierPriceRange>
            {
                new TierPriceRange { Id = Guid.NewGuid(), TierId = premiumTier.Id, ServiceName = "Venue", MinPrice = 100000, MaxPrice = 300000 },
                new TierPriceRange { Id = Guid.NewGuid(), TierId = premiumTier.Id, ServiceName = "Catering", MinPrice = 80000, MaxPrice = 200000 },
                new TierPriceRange { Id = Guid.NewGuid(), TierId = premiumTier.Id, ServiceName = "Decoration", MinPrice = 50000, MaxPrice = 150000 }
            });

            // Seed a "Gold" tier for Wedding
            var goldTier = new Tier
            {
                Id = Guid.NewGuid(),
                Name = "Gold",
                CategoryId = weddingCategory.Id,
                Description = "Mid-range premium wedding package offering great value.",
                IsActive = true,
                Icon = "bi-award",
                Gradient = "linear-gradient(#fdbb2d,#F2C94C,#f09819)",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            goldTier.PriceRanges.AddRange(new List<TierPriceRange>
            {
                new TierPriceRange { Id = Guid.NewGuid(), TierId = goldTier.Id, ServiceName = "Venue", MinPrice = 50000, MaxPrice = 100000 },
                new TierPriceRange { Id = Guid.NewGuid(), TierId = goldTier.Id, ServiceName = "Catering", MinPrice = 40000, MaxPrice = 80000 },
                new TierPriceRange { Id = Guid.NewGuid(), TierId = goldTier.Id, ServiceName = "Decoration", MinPrice = 25000, MaxPrice = 50000 }
            });

            db.Tiers.AddRange(premiumTier, goldTier);

            // Seed a tier for another category if exists
            if (categories.Count > 1)
            {
                var otherCategory = categories.FirstOrDefault(c => c.Id != weddingCategory.Id) ?? categories.Last();
                var standardTier = new Tier
                {
                    Id = Guid.NewGuid(),
                    Name = "Silver",
                    CategoryId = otherCategory.Id,
                    Description = "Silver tier configuration for general events.",
                    IsActive = true,
                    Icon = "bi-patch-check",
                    Gradient = "linear-gradient(#3E5151,#bdc3c7,#DECBA4)",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                standardTier.PriceRanges.AddRange(new List<TierPriceRange>
                {
                    new TierPriceRange { Id = Guid.NewGuid(), TierId = standardTier.Id, ServiceName = "Venue", MinPrice = 20000, MaxPrice = 50000 },
                    new TierPriceRange { Id = Guid.NewGuid(), TierId = standardTier.Id, ServiceName = "Catering", MinPrice = 15000, MaxPrice = 35000 }
                });
                db.Tiers.Add(standardTier);
            }

            db.SaveChanges();
        }

        private static string Hash(string password)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(password)));
        }
    }
}

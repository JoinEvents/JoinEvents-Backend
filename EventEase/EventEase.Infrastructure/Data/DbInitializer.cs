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

        private static string Hash(string password)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(password)));
        }
    }
}

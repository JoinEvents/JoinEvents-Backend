using EventEase.Core.Constants;
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
        /// <summary>
        /// Applies data that every environment needs (reference data), optionally bootstraps a
        /// single administrator from configuration, and only seeds demo fixtures when explicitly
        /// enabled.
        /// </summary>
        /// <param name="db">Database context.</param>
        /// <param name="options">
        /// Controls admin bootstrap and demo seeding. Demo seeding must never be enabled in
        /// production: it creates well-known accounts.
        /// </param>
        /// <returns>
        /// One line per step, for the caller to log. Seeding is otherwise entirely silent:
        /// when a staff account does not appear, there is nothing to distinguish "not
        /// configured" from "someone already holds the role" from "the step threw", and the
        /// only symptom is a login that says invalid credentials.
        /// </returns>
        public static IReadOnlyList<string> Seed(EventEaseDbContext db, SeedOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            // Each step stands on its own. They used to run in sequence under the caller's
            // single try/catch, so a failure in the first cost every step after it — the
            // staff bootstrap would never run, and the log would show one line about
            // seeding failing without saying what had not happened.
            var notes = new List<string>
            {
                Step("tiers", () => { EnsureTiers(db); return "verified"; }),
                Step("admin", () => EnsureStaffUser(
                    db, options.AdminEmail, options.AdminPassword, AuthRoles.Admin, "Administrator")),
                Step("support", () => EnsureStaffUser(
                    db, options.SupportEmail, options.SupportPassword, AuthRoles.Support, "Support")),
                Step("uat-admin", () => EnsureUatStaffUser(
                    db, UatAdminEmail, UatStaffPassword, AuthRoles.Admin, "UAT Administrator")),
                Step("uat-support", () => EnsureUatStaffUser(
                    db, UatSupportEmail, UatStaffPassword, AuthRoles.Support, "UAT Support"))
            };

            if (options.SeedDemoData)
            {
                notes.Add(Step("demo", () => { SeedDemoData(db); return "fixtures seeded"; }));
            }

            return notes;
        }

        // ── UAT staff accounts ─────────────────────────────────────────────────
        //
        // Fixed credentials for testing, created on every start. Admin and support cannot be
        // registered for in the app, and the configuration-driven bootstrap above depends on
        // four settings being right in the hosting environment, which is a thing to get wrong
        // before anyone can sign in at all. These two need nothing set up.
        //
        // [SECURITY] The credentials are in the source, so they are public. Anyone who can
        // read this repository can sign in as an administrator on any deployment running this
        // code. That is a deliberate trade for a UAT environment holding throwaway data, and
        // it is not safe anywhere else.
        //
        // Before this backend holds anything real: delete this block and these two rows, and
        // use Bootstrap:AdminEmail / Bootstrap:SupportEmail instead — that path takes its
        // credentials from the environment and never writes them down here.
        private const string UatAdminEmail = "admin@gmail.com";
        private const string UatSupportEmail = "support@gmail.com";
        private const string UatStaffPassword = "Test@123";

        /// <summary>
        /// Creates a fixed UAT account, and repairs one that has drifted from what this file says.
        /// </summary>
        /// <remarks>
        /// Keyed on the email, not on whether the role is already taken — unlike the
        /// configured bootstrap, which skips when anybody holds the role. That guard is a
        /// good one for a real deployment and is very likely why a configured admin never
        /// appeared here: something already held Admin. An account whose entire purpose is
        /// that a documented password works must not be defeated the same way, so an
        /// existing row has its role and password brought back into line.
        /// </remarks>
        private static string EnsureUatStaffUser(
            EventEaseDbContext db, string email, string password, string role, string name)
        {
            var existing = db.Users.FirstOrDefault(u => u.Email == email);

            if (existing is null)
            {
                db.Users.Add(new User
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Email = email,
                    Phone = string.Empty,
                    Role = role,
                    PasswordHash = Hash(password),
                    CreatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
                return $"created {email}";
            }

            var repaired = new List<string>();
            if (existing.Role != role)
            {
                existing.Role = role;
                repaired.Add("role");
            }
            if (string.IsNullOrEmpty(existing.PasswordHash)
                || !BCrypt.Net.BCrypt.Verify(password, existing.PasswordHash))
            {
                existing.PasswordHash = Hash(password);
                repaired.Add("password");
            }

            if (repaired.Count == 0)
            {
                return $"{email} already correct";
            }

            db.SaveChanges();
            return $"{email} reset ({string.Join(" and ", repaired)})";
        }

        /// <summary>Runs one step, turning a failure into a reportable line rather than an abort.</summary>
        private static string Step(string name, Func<string> action)
        {
            try
            {
                return $"{name}: {action()}";
            }
            catch (Exception ex)
            {
                return $"{name}: FAILED — {ex.GetType().Name}: {ex.Message}";
            }
        }

        /// <summary>
        /// Creates a staff account for a role if — and only if — nobody holds that role yet.
        /// </summary>
        /// <remarks>
        /// Admin and support cannot be signed up for: registration only offers customer and
        /// vendor, and nothing else creates them, so without this there is no way to get a
        /// first one. That is deliberate, and so is taking the credentials from configuration
        /// rather than from the source: an email and password written here would be an
        /// administrator login for every deployment of this repository, readable by anyone who
        /// can read the code.
        ///
        /// An existing holder's password is never reset. Doing that on every start would
        /// silently revert whatever the operator has since chosen, and would turn a
        /// configuration value left lying around into a standing way back in.
        /// </remarks>
        private static string EnsureStaffUser(
            EventEaseDbContext db, string? email, string? password, string role, string name)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                // Nothing configured — leave the database alone rather than inventing credentials.
                return "not configured";
            }

            var existing = db.Users.FirstOrDefault(u => u.Role == role);
            if (existing is not null)
            {
                // Names the holder, because the usual surprise is that someone else already
                // has the role and the configured address was therefore never created.
                return $"already held by {existing.Email}; configured address not created";
            }

            var normalised = email.Trim().ToLowerInvariant();
            if (db.Users.Any(u => u.Email == normalised))
            {
                return $"{normalised} already exists under a different role";
            }

            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                Name = name,
                Email = normalised,
                Phone = string.Empty,
                Role = role,
                PasswordHash = Hash(password),
                CreatedAt = DateTime.UtcNow
            });

            db.SaveChanges();
            return $"created {normalised}";
        }

        /// <summary>
        /// Demo fixtures for local development and test runs only. These accounts use a shared,
        /// well-known password and must never be created in a deployed environment.
        /// </summary>
        private static void SeedDemoData(EventEaseDbContext db)
        {
            const string demoPassword = "DemoPassw0rd!";

            if (db.Users.Any(u => u.Email == "customer@example.test")) return;

            var customer = new User
            {
                Id = Guid.NewGuid(),
                Name = "Demo Customer",
                Phone = "9999999999",
                Email = "customer@example.test",
                Role = AuthRoles.Customer,
                PasswordHash = Hash(demoPassword)
            };
            var vendorUser = new User
            {
                Id = Guid.NewGuid(),
                Name = "Demo Vendor",
                Phone = "8888888888",
                Email = "vendor@example.test",
                Role = AuthRoles.Vendor,
                PasswordHash = Hash(demoPassword)
            };
            var supportUser = new User
            {
                Id = Guid.NewGuid(),
                Name = "Demo Support",
                Phone = "7777777777",
                Email = "support@example.test",
                Role = AuthRoles.Support,
                PasswordHash = Hash(demoPassword)
            };
            db.Users.AddRange(customer, vendorUser, supportUser);

            var vendor = new Vendor
            {
                Id = Guid.NewGuid(),
                UserId = vendorUser.Id,
                BusinessName = "Dream Weddings",
                Description = "Full service wedding planner",
                Location = "Hyderabad",
                IsValidated = true
            };
            db.Vendors.Add(vendor);

            db.Services.AddRange(new List<Service>
            {
                new Service { Id = Guid.NewGuid(), VendorId = vendor.Id, Name = "Catering",    Category = "Food",  Price = 50000 },
                new Service { Id = Guid.NewGuid(), VendorId = vendor.Id, Name = "Photography", Category = "Media", Price = 30000 },
                new Service { Id = Guid.NewGuid(), VendorId = vendor.Id, Name = "Decoration",  Category = "Decor", Price = 20000 }
            });

            db.Packages.Add(new Package
            {
                Id = Guid.NewGuid(),
                VendorId = vendor.Id,
                Name = "Wedding Basic",
                Category = "Wedding",
                Pricing = new PackagePricing { BasePrice = 100000 },
                IsActive = true,
                IsVerified = true
            });

            db.SaveChanges();
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


        /// <summary>
        /// Hashes a seed password with BCrypt, matching the work factor used by the auth service.
        /// </summary>
        private static string Hash(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
        }
    }

    /// <summary>
    /// Seeding inputs, resolved from configuration by the host.
    /// </summary>
    public sealed class SeedOptions
    {
        /// <summary>Email for the bootstrap administrator. Null disables admin bootstrap.</summary>
        public string? AdminEmail { get; init; }

        /// <summary>Password for the bootstrap administrator. Null disables admin bootstrap.</summary>
        public string? AdminPassword { get; init; }

        /// <summary>Email for the bootstrap support agent. Null disables support bootstrap.</summary>
        public string? SupportEmail { get; init; }

        /// <summary>Password for the bootstrap support agent. Null disables support bootstrap.</summary>
        public string? SupportPassword { get; init; }

        /// <summary>When true, seeds demo accounts and catalogue fixtures. Never enable in production.</summary>
        public bool SeedDemoData { get; init; }
    }
}

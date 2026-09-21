using EventEase.Core.Constants;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EventEase.Tests
{
    /// <summary>
    /// Admin and support cannot be registered for through the app, so the seeder is the only
    /// way a first one exists. These pin that it creates them from configuration, that it
    /// creates nothing without configuration, and above all that it never rewrites the
    /// password of an account that is already there.
    /// </summary>
    public class StaffBootstrapTests
    {
        private static EventEaseDbContext NewDb() =>
            new(new DbContextOptionsBuilder<EventEaseDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        private static SeedOptions Staff(string adminPassword = "Test@123", string supportPassword = "Test@123") => new()
        {
            AdminEmail = "admin@gmail.com",
            AdminPassword = adminPassword,
            SupportEmail = "support@gmail.com",
            SupportPassword = supportPassword,
            SeedDemoData = false
        };

        [Fact]
        public void Creates_an_admin_and_a_support_account_from_configuration()
        {
            using var db = NewDb();

            DbInitializer.Seed(db, Staff());

            var admin = db.Users.Single(u => u.Role == AuthRoles.Admin);
            var support = db.Users.Single(u => u.Role == AuthRoles.Support);

            Assert.Equal("admin@gmail.com", admin.Email);
            Assert.Equal("support@gmail.com", support.Email);
            Assert.True(BCrypt.Net.BCrypt.Verify("Test@123", admin.PasswordHash));
            Assert.True(BCrypt.Net.BCrypt.Verify("Test@123", support.PasswordHash));
        }

        [Fact]
        public void Lowercases_the_configured_email_so_sign_in_matches()
        {
            using var db = NewDb();

            DbInitializer.Seed(db, new SeedOptions
            {
                AdminEmail = "  Admin@GMAIL.com  ",
                AdminPassword = "Test@123"
            });

            Assert.Equal("admin@gmail.com", db.Users.Single(u => u.Role == AuthRoles.Admin).Email);
        }

        [Fact]
        public void Creates_only_the_fixed_UAT_pair_when_nothing_is_configured()
        {
            using var db = NewDb();

            DbInitializer.Seed(db, new SeedOptions { SeedDemoData = false });

            // The configured bootstrap adds nothing without configuration; the fixed UAT
            // accounts are created regardless, which is the point of them.
            Assert.Equal(2, db.Users.Count());
            Assert.Contains(db.Users, u => u.Role == AuthRoles.Admin);
            Assert.Contains(db.Users, u => u.Role == AuthRoles.Support);
        }

        [Fact]
        public void Creates_only_the_role_that_is_configured()
        {
            using var db = NewDb();

            DbInitializer.Seed(db, new SeedOptions
            {
                SupportEmail = "support@gmail.com",
                SupportPassword = "Test@123"
            });

            // No admin from configuration; the one present is the fixed UAT account.
            Assert.Single(db.Users.Where(u => u.Role == AuthRoles.Admin));
            Assert.Single(db.Users.Where(u => u.Role == AuthRoles.Support));
        }

        [Fact]
        public void Never_resets_the_password_of_a_role_that_already_exists()
        {
            using var db = NewDb();
            DbInitializer.Seed(db, Staff());
            var originalHash = db.Users.Single(u => u.Role == AuthRoles.Admin).PasswordHash;

            // A later start with a different configured password must not take effect.
            DbInitializer.Seed(db, Staff(adminPassword: "SomethingElse!1", supportPassword: "SomethingElse!1"));

            var admin = db.Users.Single(u => u.Role == AuthRoles.Admin);
            Assert.Equal(originalHash, admin.PasswordHash);
            Assert.False(BCrypt.Net.BCrypt.Verify("SomethingElse!1", admin.PasswordHash));
        }

        [Fact]
        public void Running_twice_does_not_create_a_second_account()
        {
            using var db = NewDb();

            DbInitializer.Seed(db, Staff());
            DbInitializer.Seed(db, Staff());

            Assert.Single(db.Users.Where(u => u.Role == AuthRoles.Admin));
            Assert.Single(db.Users.Where(u => u.Role == AuthRoles.Support));
        }

        [Fact]
        public void Reports_what_it_did_for_each_role()
        {
            using var db = NewDb();

            var notes = DbInitializer.Seed(db, Staff());

            Assert.Contains(notes, n => n.StartsWith("admin:") && n.Contains("created admin@gmail.com"));
            Assert.Contains(notes, n => n.StartsWith("support:") && n.Contains("created support@gmail.com"));
        }

        [Fact]
        public void Reports_that_a_role_is_already_held_and_names_the_holder()
        {
            using var db = NewDb();
            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                Name = "Existing",
                Email = "someone.else@example.test",
                Role = AuthRoles.Admin,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Whatever!1", workFactor: 4)
            });
            db.SaveChanges();

            var notes = DbInitializer.Seed(db, Staff());

            // The usual surprise: the configured address was never created because the role
            // was already taken, and nothing previously said so.
            Assert.Contains(notes, n => n.StartsWith("admin:")
                                        && n.Contains("already held by someone.else@example.test"));
        }

        [Fact]
        public void Reports_when_a_role_is_not_configured()
        {
            using var db = NewDb();

            var notes = DbInitializer.Seed(db, new SeedOptions { SeedDemoData = false });

            Assert.Contains(notes, n => n == "admin: not configured");
            Assert.Contains(notes, n => n == "support: not configured");
        }

        [Fact]
        public void A_failing_step_does_not_stop_the_ones_after_it()
        {
            using var db = NewDb();
            db.Dispose(); // Forces every step to throw, standing in for a database problem.

            var notes = DbInitializer.Seed(db, Staff());

            // Each step is reported rather than the first failure aborting the rest:
            // tiers, the two configured roles, and the two fixed UAT accounts.
            Assert.Equal(5, notes.Count);
            Assert.All(notes, n => Assert.Contains("FAILED", n));
        }

        [Fact]
        public void Leaves_an_admin_created_by_hand_alone()
        {
            using var db = NewDb();
            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                Name = "Existing",
                Email = "someone.else@example.test",
                Role = AuthRoles.Admin,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Whatever!1", workFactor: 4)
            });
            db.SaveChanges();

            DbInitializer.Seed(db, Staff());

            // The configured address is still skipped because the role is taken, and the
            // hand-made account keeps its password.
            var byHand = db.Users.Single(u => u.Email == "someone.else@example.test");
            Assert.Equal(AuthRoles.Admin, byHand.Role);
            Assert.True(BCrypt.Net.BCrypt.Verify("Whatever!1", byHand.PasswordHash));
        }

        [Fact]
        public void Creates_the_fixed_UAT_admin_even_when_the_role_is_already_taken()
        {
            using var db = NewDb();
            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                Name = "Existing",
                Email = "someone.else@example.test",
                Role = AuthRoles.Admin,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Whatever!1", workFactor: 4)
            });
            db.SaveChanges();

            DbInitializer.Seed(db, new SeedOptions());

            // This is exactly the case that makes the configured bootstrap do nothing
            // silently, and the reason the UAT pair is keyed on email instead.
            Assert.Contains(db.Users, u => u.Email == "admin@gmail.com" && u.Role == AuthRoles.Admin);
        }

        [Fact]
        public void Puts_a_changed_UAT_password_back_and_says_so()
        {
            using var db = NewDb();
            DbInitializer.Seed(db, new SeedOptions());

            var admin = db.Users.Single(u => u.Email == "admin@gmail.com");
            var seededHash = admin.PasswordHash;
            admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword("SomethingElse!1", workFactor: 4);
            db.SaveChanges();

            var notes = DbInitializer.Seed(db, new SeedOptions());

            var restored = db.Users.Single(u => u.Email == "admin@gmail.com").PasswordHash;
            Assert.False(BCrypt.Net.BCrypt.Verify("SomethingElse!1", restored));
            Assert.NotEqual(seededHash, restored); // rehashed, not merely left alone
            Assert.Contains(notes, n => n.StartsWith("uat-admin:") && n.Contains("reset"));
        }

        [Fact]
        public void Reports_the_UAT_pair_as_already_correct_on_a_later_start()
        {
            using var db = NewDb();
            DbInitializer.Seed(db, new SeedOptions());

            var notes = DbInitializer.Seed(db, new SeedOptions());

            Assert.Contains(notes, n => n == "uat-admin: admin@gmail.com already correct");
            Assert.Contains(notes, n => n == "uat-support: support@gmail.com already correct");
        }
    }
}

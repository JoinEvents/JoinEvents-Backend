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
        public void Creates_nothing_when_no_credentials_are_configured()
        {
            using var db = NewDb();

            DbInitializer.Seed(db, new SeedOptions { SeedDemoData = false });

            Assert.Empty(db.Users);
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

            Assert.Empty(db.Users.Where(u => u.Role == AuthRoles.Admin));
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

            Assert.Single(db.Users.Where(u => u.Role == AuthRoles.Admin));
            Assert.Equal("someone.else@example.test", db.Users.Single(u => u.Role == AuthRoles.Admin).Email);
        }
    }
}

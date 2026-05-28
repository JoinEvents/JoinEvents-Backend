using EventEase.Core.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;


namespace EventEase.Infrastructure.Data
{
    public class EventEaseDbContext : DbContext
    {
        public EventEaseDbContext(DbContextOptions<EventEaseDbContext> options) : base(options) { }
        public DbSet<User> Users { get; set; }
        public DbSet<Vendor> Vendors { get; set; }
        public DbSet<Service> Services { get; set; }
        public DbSet<Package> Packages { get; set; }
        public DbSet<PackageImage> PackageImages { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<Review> Reviews { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Chat> Chats { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<Rfp> Rfps { get; set; }
        public DbSet<Bid> Bids { get; set; }
        public DbSet<VendorDocument> VendorDocuments { get; set; }
        public DbSet<BookingLog> BookingLogs { get; set; }
        public DbSet<ChatThread> ChatThreads { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<EventEase.Core.Entities.EventCategory> EventCategories { get; set; }
        public DbSet<LoyaltyTransaction> LoyaltyTransactions { get; set; }
        public DbSet<SupportTicket> SupportTickets { get; set; }
        public DbSet<Tier> Tiers { get; set; }
        public DbSet<TierPriceRange> TierPriceRanges { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            //one vendor has many services (one to many relationship)
            modelBuilder.Entity<Vendor>()
                .HasMany(v => v.services)
                .WithOne(s => s.vendors)
                .HasForeignKey(s => s.VendorId);

            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<EventEase.Core.Entities.Booking>()
                .Property(b => b.Amount)
                .HasPrecision(18, 2); // 18 digits, 2 decimal places
            
            modelBuilder.Entity<Package>(b =>
            {
                b.OwnsOne(p => p.Address);
                b.OwnsOne(p => p.Pricing, pricing => {
                    pricing.Property(p => p.VegPrice).HasPrecision(18, 2);
                    pricing.Property(p => p.NonVegPrice).HasPrecision(18, 2);
                    pricing.Property(p => p.RoomPrice).HasPrecision(18, 2);
                    pricing.Property(p => p.BasePrice).HasPrecision(18, 2);
                    pricing.Property(p => p.Rent).HasPrecision(18, 2);
                });
                b.OwnsOne(p => p.Capacity);
                b.OwnsOne(p => p.Policies);
                b.OwnsOne(p => p.Amenities);
                b.OwnsMany(p => p.Spaces);

                b.HasMany(p => p.Images)
                 .WithOne(i => i.Package)
                 .HasForeignKey(i => i.PackageId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<EventEase.Core.Entities.Payment>()
                .Property(b => b.Amount)
                .HasPrecision(18, 2); // 18 digits, 2 decimal places
            modelBuilder.Entity<EventEase.Core.Entities.Service>()
                .Property(b => b.Price)
                .HasPrecision(18, 2); // 18 digits, 2 decimal places
            modelBuilder.Entity<Bid>()
                .Property(b => b.ProposedAmount)
                .HasPrecision(18, 2);
            modelBuilder.Entity<Rfp>()
                .Property(r => r.BudgetMin)
                .HasPrecision(18, 2);
            modelBuilder.Entity<Rfp>()
                .Property(r => r.BudgetMax)
                .HasPrecision(18, 2);
            modelBuilder.Entity<RefreshToken>().ToTable("RefreshTokens");

            modelBuilder.Entity<EventEase.Core.Entities.EventCategory>(b =>
            {
                b.HasIndex(c => c.CategoryKey).IsUnique();
                b.Property(c => c.StartingPrice).HasPrecision(18, 2);
            });

            modelBuilder.Entity<Tier>(b =>
            {
                b.HasOne(t => t.Category)
                 .WithMany()
                 .HasForeignKey(t => t.CategoryId)
                 .OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(t => new { t.Name, t.CategoryId }).IsUnique();
                b.Property(t => t.Icon).HasDefaultValue("bi-layers").HasMaxLength(100);
                b.Property(t => t.Gradient).HasDefaultValue("linear-gradient(135deg,#6B7280,#374151)").HasMaxLength(250);
            });

            modelBuilder.Entity<TierPriceRange>(b =>
            {
                b.HasOne(p => p.Tier)
                 .WithMany(t => t.PriceRanges)
                 .HasForeignKey(p => p.TierId)
                 .OnDelete(DeleteBehavior.Cascade);
                b.Property(p => p.MinPrice).HasPrecision(18, 2);
                b.Property(p => p.MaxPrice).HasPrecision(18, 2);
            });
        }
    }


}

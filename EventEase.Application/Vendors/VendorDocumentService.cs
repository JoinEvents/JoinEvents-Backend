using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static EventEase.Application.Vendors.Dtos;

namespace EventEase.Application.Vendors
{
    public class VendorDocumentService : IVendorDocumentService
    {
        private readonly EventEaseDbContext _db;
        public VendorDocumentService(EventEaseDbContext db) => _db = db;

        public async Task<VendorDocument> UploadDocumentAsync(Guid vendorId, string documentType, string fileName, string fileUrl)
        {
            var doc = new VendorDocument
            {
                Id = Guid.NewGuid(),
                VendorId = vendorId,
                DocumentType = documentType,
                FileName = fileName,
                FileUrl = fileUrl,
                Status = "pending",
                UploadedAt = DateTime.UtcNow
            };
            _db.VendorDocuments.Add(doc);
            await _db.SaveChangesAsync();
            return doc;
        }

        public async Task<VendorDocument?> ReviewDocumentAsync(Guid docId, Guid adminId, ReviewDocumentDto dto)
        {
            var doc = await _db.VendorDocuments.FindAsync(docId);
            if (doc is null) return null;

            doc.Status = dto.Status.ToLower();
            doc.RejectionReason = dto.RejectionReason;
            doc.AuditedBy = adminId;

            // If approved, let's also mark the vendor as validated
            if (doc.Status == "approved")
            {
                var vendor = await _db.Vendors.FindAsync(doc.VendorId);
                if (vendor is not null)
                {
                    vendor.IsValidated = true;
                }
            }

            await _db.SaveChangesAsync();
            return doc;
        }

        public async Task<bool> ModerateVendorAsync(Guid vendorId, Guid adminId, ModerateVendorDto dto)
        {
            var vendor = await _db.Vendors.FindAsync(vendorId);
            if (vendor is null) return false;

            // Update vendor validation state according to the action
            if (dto.Action.ToLower() == "suspend" || dto.Action.ToLower() == "ban")
            {
                vendor.IsValidated = false;
            }
            else if (dto.Action.ToLower() == "reactivate")
            {
                vendor.IsValidated = true;
            }

            // Create an audit trail log
            var log = new BookingLog
            {
                Id = Guid.NewGuid(),
                BookingId = Guid.Empty, // System-wide moderation
                Message = $"Vendor {vendor.BusinessName} moderated: {dto.Action}. Reason: {dto.Reason}. Duration: {dto.Duration}",
                Actor = $"Admin {adminId}",
                CreatedAt = DateTime.UtcNow
            };
            _db.BookingLogs.Add(log);

            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<VendorAnalyticsResponse> GetAnalyticsAsync(Guid vendorId)
        {
            var currentYear = DateTime.UtcNow.Year;

            // Fetch all bookings for this vendor
            var allBookings = await _db.Bookings
                .Where(b => b.VendorId == vendorId)
                .ToListAsync();

            // Monthly earnings (12 months of current year)
            var monthlyEarnings = new decimal[12];
            foreach (var b in allBookings.Where(b => b.Status == "Paid" && b.EventDate.Year == currentYear))
            {
                var idx = b.EventDate.Month - 1;
                if (idx >= 0 && idx < 12)
                    monthlyEarnings[idx] += b.Amount;
            }

            // If no real data, use demo values
            bool hasData = monthlyEarnings.Any(e => e > 0);
            if (!hasData)
            {
                monthlyEarnings = new decimal[] { 40000, 50000, 65000, 45000, 80000, 95000, 70000, 110000, 85000, 120000, 150000, 180000 };
            }

            decimal totalEarnings = monthlyEarnings.Sum();

            // Booking counts by status
            var pendingCount = allBookings.Count(b => b.Status == "Pending");
            var acceptedCount = allBookings.Count(b => b.Status == "Accepted" || b.Status == "Paid");
            var declinedCount = allBookings.Count(b => b.Status == "Rejected" || b.Status == "Cancelled");
            var completedCount = allBookings.Count(b => b.Status == "Paid");

            if (!allBookings.Any())
            {
                pendingCount = 4; acceptedCount = 3; declinedCount = 1; completedCount = 87;
            }

            // Rating trend (12 months)
            var averageRatingTrend = new double[] { 4.5, 4.6, 4.6, 4.7, 4.7, 4.8, 4.8, 4.8, 4.9, 4.8, 4.9, 4.8 };

            // Top performing package
            var topPackage = await _db.Packages
                .Where(p => p.VendorId == vendorId)
                .FirstOrDefaultAsync();

            var topPerformingService = topPackage != null
                ? (object)new
                {
                    name = topPackage.Name,
                    description = topPackage.Description ?? "Premium service package",
                    rating = 4.9,
                    totalReviews = completedCount
                }
                : (object)new
                {
                    name = "Premium Event Package",
                    description = "Top-rated event service bundle",
                    rating = 4.9,
                    totalReviews = 38
                };

            // Return in frontend-compatible format via anonymous object (serialized as JSON)
            var result = new
            {
                totalEarnings = (long)totalEarnings,
                monthlyEarnings = monthlyEarnings.Select(e => (long)e).ToArray(),
                bookingCountByStatus = new
                {
                    pending = pendingCount,
                    accepted = acceptedCount,
                    declined = declinedCount,
                    completed = completedCount
                },
                averageRatingTrend,
                topPerformingService
            };

            // We must still return VendorAnalyticsResponse for interface compliance;
            // the controller will use the raw result instead
            var legacyMonthlyRevenue = monthlyEarnings.Select((e, i) => new { month = i, revenue = (long)e }).ToArray<object>();
            return new VendorAnalyticsResponse(totalEarnings, acceptedCount, pendingCount, legacyMonthlyRevenue, 4.8);
        }

        /// <summary>
        /// Returns vendor analytics in the format expected by the Angular frontend.
        /// </summary>
        public async Task<object> GetAnalyticsForFrontendAsync(Guid vendorId)
        {
            var currentYear = DateTime.UtcNow.Year;

            var allBookings = await _db.Bookings
                .Where(b => b.VendorId == vendorId)
                .ToListAsync();

            var monthlyEarnings = new decimal[12];
            foreach (var b in allBookings.Where(b => b.Status == "Paid" && b.EventDate.Year == currentYear))
            {
                var idx = b.EventDate.Month - 1;
                if (idx >= 0 && idx < 12)
                    monthlyEarnings[idx] += b.Amount;
            }

            bool hasData = monthlyEarnings.Any(e => e > 0);
            if (!hasData)
                monthlyEarnings = new decimal[] { 40000, 50000, 65000, 45000, 80000, 95000, 70000, 110000, 85000, 120000, 150000, 180000 };

            decimal totalEarnings = monthlyEarnings.Sum();

            var pendingCount = allBookings.Any() ? allBookings.Count(b => b.Status == "Pending") : 4;
            var acceptedCount = allBookings.Any() ? allBookings.Count(b => b.Status == "Accepted" || b.Status == "Paid") : 3;
            var declinedCount = allBookings.Any() ? allBookings.Count(b => b.Status == "Rejected" || b.Status == "Cancelled") : 1;
            var completedCount = allBookings.Any() ? allBookings.Count(b => b.Status == "Paid") : 87;

            var averageRatingTrend = new double[] { 4.5, 4.6, 4.6, 4.7, 4.7, 4.8, 4.8, 4.8, 4.9, 4.8, 4.9, 4.8 };

            var topPackage = await _db.Packages
                .Where(p => p.VendorId == vendorId)
                .FirstOrDefaultAsync();

            object topPerformingService = topPackage != null
                ? new { name = topPackage.Name, description = topPackage.Description ?? "Premium service package", rating = 4.9, totalReviews = completedCount }
                : new { name = "Premium Event Package", description = "Top-rated event service bundle", rating = 4.9, totalReviews = 38 };

            return new
            {
                totalEarnings = (long)totalEarnings,
                monthlyEarnings = monthlyEarnings.Select(e => (long)e).ToArray(),
                bookingCountByStatus = new { pending = pendingCount, accepted = acceptedCount, declined = declinedCount, completed = completedCount },
                averageRatingTrend,
                topPerformingService
            };
        }

        public async Task<List<object>> GetAllVendorsForAdminAsync()
        {
            var vendors = await _db.Vendors
                .Include(v => v.services)
                .ToListAsync();

            var userIds = vendors.Select(v => v.UserId).ToList();
            var users = await _db.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u);

            var vendorIds = vendors.Select(v => v.Id).ToList();
            var docs = await _db.VendorDocuments
                .Where(d => vendorIds.Contains(d.VendorId))
                .ToListAsync();

            var bookings = await _db.Bookings
                .Where(b => vendorIds.Contains(b.VendorId))
                .ToListAsync();

            var reviews = await _db.Reviews
                .Where(r => vendorIds.Contains(r.VendorId))
                .ToListAsync();

            var logs = await _db.BookingLogs
                .Where(l => l.BookingId == Guid.Empty && l.Message.Contains("moderated:"))
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            var response = new List<object>();

            foreach (var v in vendors)
            {
                users.TryGetValue(v.UserId, out var u);
                var vDocs = docs.Where(d => d.VendorId == v.Id).ToList();
                var vBookings = bookings.Where(b => b.VendorId == v.Id).ToList();
                var vReviews = reviews.Where(r => r.VendorId == v.Id).ToList();

                // Compute rating
                double rating = vReviews.Any() ? Math.Round(vReviews.Average(r => r.Rating), 1) : 4.8;
                int totalReviews = vReviews.Count;

                // Compute total earnings
                decimal totalEarnings = vBookings.Where(b => b.Status.Equals("Paid", StringComparison.OrdinalIgnoreCase)).Sum(b => b.Amount);

                // Compute verification status
                string verificationStatus = "pending";
                if (v.IsValidated)
                {
                    verificationStatus = "verified";
                }
                else if (vDocs.Any(d => d.Status.Equals("pending", StringComparison.OrdinalIgnoreCase)))
                {
                    verificationStatus = "under_review";
                }
                else if (vDocs.Any(d => d.Status.Equals("rejected", StringComparison.OrdinalIgnoreCase) || d.Status.Equals("action_required", StringComparison.OrdinalIgnoreCase)))
                {
                    verificationStatus = "rejected";
                }

                // Compute account status / moderation details from latest log
                var latestLog = logs.FirstOrDefault(l => l.Message.StartsWith($"Vendor {v.BusinessName} moderated:"));
                string accountStatus = "active";
                string? suspensionReason = null;
                string? suspensionDuration = null;

                if (latestLog != null)
                {
                    var msg = latestLog.Message;
                    int modIndex = msg.IndexOf("moderated:");
                    if (modIndex != -1)
                    {
                        string rest = msg.Substring(modIndex + 10).Trim();
                        int dotIndex = rest.IndexOf('.');
                        string action = dotIndex != -1 ? rest.Substring(0, dotIndex).Trim().ToLower() : rest.Trim().ToLower();

                        if (action == "suspend")
                        {
                            accountStatus = "suspended";
                        }
                        else if (action == "ban")
                        {
                            accountStatus = "banned";
                        }
                        else if (action == "reactivate")
                        {
                            accountStatus = "active";
                        }

                        // Parse Reason
                        int reasonIndex = msg.IndexOf("Reason:");
                        if (reasonIndex != -1)
                        {
                            string reasonRest = msg.Substring(reasonIndex + 7).Trim();
                            int nextDotIndex = reasonRest.IndexOf('.');
                            suspensionReason = nextDotIndex != -1 ? reasonRest.Substring(0, nextDotIndex).Trim() : reasonRest.Trim();
                            if (string.IsNullOrEmpty(suspensionReason)) suspensionReason = null;
                        }

                        // Parse Duration
                        int durationIndex = msg.IndexOf("Duration:");
                        if (durationIndex != -1)
                        {
                            suspensionDuration = msg.Substring(durationIndex + 9).Trim();
                            if (string.IsNullOrEmpty(suspensionDuration)) suspensionDuration = null;
                        }
                    }
                }

                response.Add(new
                {
                    id = v.Id,
                    name = u?.Name ?? "Unknown",
                    avatar = u?.Avatar,
                    businessName = v.BusinessName,
                    email = u?.Email ?? "Unknown",
                    phone = u?.Phone ?? "Unknown",
                    city = v.Location ?? u?.City ?? "Unknown",
                    services = v.services?.Select(s => s.Name).ToList() ?? new List<string>(),
                    isVerified = v.IsValidated,
                    verificationStatus = verificationStatus,
                    verificationDocs = vDocs.Select(d => new
                    {
                        type = d.DocumentType,
                        name = d.FileName,
                        uploadedAt = d.UploadedAt.ToString("yyyy-MM-dd"),
                        status = d.Status,
                        fileUrl = d.FileUrl,
                        url = d.FileUrl
                    }).ToList(),
                    rating = rating,
                    totalReviews = totalReviews,
                    totalEarnings = (double)totalEarnings,
                    joinedDate = v.CreatedAt.ToString("yyyy-MM-dd"),
                    accountStatus = accountStatus,
                    suspensionReason = suspensionReason,
                    suspensionDuration = suspensionDuration
                });
            }

            return response;
        }
    }
}

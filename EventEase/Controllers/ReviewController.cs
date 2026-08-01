using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace EventEase.Api.Controllers
{
    public class ReviewSubmitDto
    {
        public string BookingId { get; set; } = string.Empty;
        public string VendorId { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public int Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
    }

    [ApiController]
    [Route("api/v1/reviews")]
    public class ReviewController : ControllerBase
    {
        private readonly EventEaseDbContext _db;

        public ReviewController(EventEaseDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetReviews([FromQuery] string? vendorId)
        {
            if (string.IsNullOrEmpty(vendorId))
            {
                var allReviews = await (from r in _db.Reviews.AsNoTracking()
                                       join u in _db.Users.AsNoTracking() on r.UserId equals u.Id into userGroup
                                       from u in userGroup.DefaultIfEmpty()
                                       where r.Status == "published"
                                       select new
                                       {
                                           id = r.Id.ToString(),
                                           bookingId = r.BookingId.ToString(),
                                           vendorId = $"usr_{r.VendorId:N}",
                                           customerName = r.CustomerName,
                                           customerAvatar = u != null ? u.Avatar : null,
                                           eventName = r.EventName,
                                           rating = r.Rating,
                                           comment = r.Comment,
                                           date = r.CreatedAt.ToString("yyyy-MM-dd"),
                                           status = r.Status,
                                           disputeReason = r.DisputeReason
                                       })
                                       .ToListAsync();
                return Ok(allReviews);
            }

            var cleanVendorId = vendorId.Replace("usr_", "").Replace("v_", "");
            if (!Guid.TryParse(cleanVendorId, out var vendorGuid))
            {
                return BadRequest(new { error = "Invalid vendor ID format." });
            }

            var reviews = await (from r in _db.Reviews.AsNoTracking()
                                 join u in _db.Users.AsNoTracking() on r.UserId equals u.Id into userGroup
                                 from u in userGroup.DefaultIfEmpty()
                                 where r.VendorId == vendorGuid && r.Status == "published"
                                 select new
                                 {
                                     id = r.Id.ToString(),
                                     bookingId = r.BookingId.ToString(),
                                     vendorId = $"usr_{r.VendorId:N}",
                                     customerName = r.CustomerName,
                                     customerAvatar = u != null ? u.Avatar : null,
                                     eventName = r.EventName,
                                     rating = r.Rating,
                                     comment = r.Comment,
                                     date = r.CreatedAt.ToString("yyyy-MM-dd"),
                                     status = r.Status,
                                     disputeReason = r.DisputeReason
                                 })
                                 .ToListAsync();

            return Ok(reviews);
        }

        [HttpGet("vendor/{vendorId}")]
        public async Task<IActionResult> GetReviewsByVendor(string vendorId)
        {
            return await GetReviews(vendorId);
        }

        [Authorize(Policy = "User")]
        [HttpPost]
        public async Task<IActionResult> SubmitReview([FromBody] ReviewSubmitDto dto)
        {
            if (dto == null) return BadRequest("Invalid review details.");
            
            var cleanBookingId = dto.BookingId.Replace("bk_", "").Replace("booking_", "");
            var cleanVendorId = dto.VendorId.Replace("usr_", "").Replace("v_", "");

            if (!Guid.TryParse(cleanBookingId, out var bookingGuid))
                return BadRequest("Invalid Booking ID.");

            if (!Guid.TryParse(cleanVendorId, out var vendorGuid))
                return BadRequest("Invalid Vendor ID.");

            var userIdVal = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            Guid.TryParse(userIdVal, out var userGuid);

            var review = new Review
            {
                Id = Guid.NewGuid(),
                BookingId = bookingGuid,
                VendorId = vendorGuid,
                UserId = userGuid,
                CustomerName = dto.CustomerName,
                EventName = dto.EventName,
                Rating = dto.Rating,
                Comment = dto.Comment,
                CreatedAt = DateTime.UtcNow,
                Status = "published"
            };

            // Check if there is already a review for this booking
            var existing = await _db.Reviews.FirstOrDefaultAsync(r => r.BookingId == review.BookingId);
            if (existing != null)
            {
                existing.Rating = review.Rating;
                existing.Comment = review.Comment;
                existing.CreatedAt = DateTime.UtcNow;
                existing.Status = "published";
                _db.Reviews.Update(existing);
                await _db.SaveChangesAsync();
                await PackageRatingHelper.RecalculatePackageRating(_db, existing.BookingId);
                return Ok(existing);
            }

            _db.Reviews.Add(review);
            await _db.SaveChangesAsync();
            await PackageRatingHelper.RecalculatePackageRating(_db, review.BookingId);
            return Ok(review);
        }

        public record FlagReviewRequest(string Reason);

        [Authorize(Policy = "Vendor")]
        [HttpPost("{id:guid}/flag")]
        public async Task<IActionResult> FlagReview(Guid id, [FromBody] FlagReviewRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Reason))
            {
                return BadRequest(new { error = "Please provide a reason for the dispute." });
            }

            var review = await _db.Reviews.FindAsync(id);
            if (review == null) return NotFound(new { error = "Review not found" });

            review.Status = "flagged";
            review.DisputeReason = req.Reason;

            _db.Reviews.Update(review);
            await _db.SaveChangesAsync();
            await PackageRatingHelper.RecalculatePackageRating(_db, review.BookingId);
            return Ok(new { success = true });
        }
    }
}

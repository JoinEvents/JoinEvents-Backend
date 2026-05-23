using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1/reviews")]
    public class ReviewController : ControllerBase
    {
        private readonly EventEaseDbContext _db;

        public ReviewController(EventEaseDbContext db)
        {
            _db = db;
        }

        [Authorize(Policy = "User")]
        [HttpPost]
        public async Task<IActionResult> SubmitReview([FromBody] Review review)
        {
            if (review == null) return BadRequest("Invalid review details.");
            
            review.Id = Guid.NewGuid();
            review.CreatedAt = DateTime.UtcNow;
            review.Status = "published";

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
                return Ok(existing);
            }

            _db.Reviews.Add(review);
            await _db.SaveChangesAsync();
            return Ok(review);
        }
    }
}

using EventEase.Application.Payments;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static EventEase.Application.Checkout.Dtos;
using EventEase.Application.Loyalty;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1/booking")]
    public class BookingController : ControllerBase
    {
        private readonly EventEaseDbContext _db;
        private readonly IPaymentGateway _gateway;
        private readonly ILoyaltyService _loyaltyService;

        public BookingController(EventEaseDbContext db, IPaymentGateway gateway, ILoyaltyService loyaltyService)
        {
            _db = db;
            _gateway = gateway;
            _loyaltyService = loyaltyService;
        }

        [Authorize(Policy = "User")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Booking dto)
        {
            dto.Id = Guid.NewGuid();
            dto.Status = "Pending";
            _db.Bookings.Add(dto);
            await _db.SaveChangesAsync();
            return Ok(dto);
        }

        private async Task<object> MapBookingToDto(Booking b)
        {
            var user = await _db.Users.FindAsync(b.UserId);
            var customerName = user?.Name ?? "Customer";
            var customerPhone = user?.Phone ?? "";
            
            var vendor = await _db.Vendors.FindAsync(b.VendorId);
            var vendorName = vendor?.BusinessName ?? "Vendor Partner";

            string mappedStatus = b.Status.ToLower();
            if (mappedStatus == "paid") mappedStatus = "confirmed";

            var services = new List<object>
            {
                new
                {
                    serviceId = Guid.NewGuid().ToString(),
                    serviceName = b.PackageName ?? "Full Event Package Service",
                    category = "Event",
                    vendorId = b.VendorId.ToString(),
                    vendorName = vendorName,
                    price = b.Amount,
                    status = "confirmed"
                }
            };

            var package = b.PackageId.HasValue 
                ? await _db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == b.PackageId.Value) 
                : null;

            if (package != null && package.Includes != null)
            {
                foreach (var include in package.Includes)
                {
                    services.Add(new
                    {
                        serviceId = Guid.NewGuid().ToString(),
                        serviceName = include,
                        category = "Included Service",
                        vendorId = b.VendorId.ToString(),
                        vendorName = vendorName,
                        price = 0m,
                        status = "included"
                    });
                }
            }

            // Fetch review
            var review = await _db.Reviews.FirstOrDefaultAsync(r => r.BookingId == b.Id && r.Status != "removed");
            object? reviewInfo = null;
            if (review != null)
            {
                reviewInfo = new
                {
                    rating = review.Rating,
                    comment = review.Comment
                };
            }

            // Fetch dispute info
            object? disputeInfo = null;
            if (mappedStatus == "disputed")
            {
                var disputeLog = await _db.BookingLogs
                    .Where(l => l.BookingId == b.Id && l.Message.StartsWith("Dispute raised. Reason:"))
                    .OrderByDescending(l => l.CreatedAt)
                    .FirstOrDefaultAsync();

                var reason = disputeLog != null 
                    ? disputeLog.Message.Substring("Dispute raised. Reason:".Length).Trim()
                    : "Dispute raised.";

                disputeInfo = new
                {
                    reason = reason,
                    status = "open"
                };
            }

            // Map eventTypeId
            string eventTypeId = "wedding";
            var nameLower = (b.EventName ?? "").ToLower();
            if (nameLower.Contains("birthday")) eventTypeId = "birthday";
            else if (nameLower.Contains("corporate")) eventTypeId = "corporate";
            else if (nameLower.Contains("beauty")) eventTypeId = "beauty";
            else if (nameLower.Contains("travel")) eventTypeId = "travel";
            else if (nameLower.Contains("shopping")) eventTypeId = "shopping";

            return new
            {
                id = b.Id.ToString(),
                bookingNumber = $"BK-{b.Id.ToString().Substring(0, 8).ToUpper()}",
                customerId = b.UserId.ToString(),
                customerName = customerName,
                customerPhone = customerPhone,
                eventTypeId = eventTypeId,
                eventName = string.IsNullOrWhiteSpace(b.EventName) ? "Event Celebration" : b.EventName,
                packageId = b.PackageId?.ToString() ?? Guid.NewGuid().ToString(),
                packageName = string.IsNullOrWhiteSpace(b.PackageName) ? "Premium Celebration Package" : b.PackageName,
                eventDate = b.EventDate.ToString("yyyy-MM-dd"),
                venue = string.IsNullOrWhiteSpace(b.Venue) ? "Grand Palace Resort" : b.Venue,
                city = string.IsNullOrWhiteSpace(b.City) ? (user?.City ?? "Mumbai") : b.City,
                guestCount = b.GuestCount > 0 ? b.GuestCount : 150,
                status = mappedStatus,
                advanceAmount = b.AdvanceAmount,
                baseAmount = Math.Round((b.TotalAmount - b.DamageCharges) / 1.18m, 2),
                extraServicesAmount = b.ExtraServicesAmount,
                damageCharges = b.DamageCharges,
                damageChargeNotes = b.DamageChargeNotes,
                isDamageChargeApproved = b.IsDamageChargeApproved,
                gstPercent = 18,
                totalAmount = b.TotalAmount,
                finalPaidAmount = b.FinalPaidAmount,
                cancelledBy = b.CancelledBy,
                cancellationReason = b.CancellationReason,
                disputeInfo = disputeInfo,
                review = reviewInfo,
                services = services,
                createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")
            };
        }

        private async Task<List<object>> MapBookingsToDtosAsync(List<Booking> bookings)
        {
            if (bookings == null || !bookings.Any())
            {
                return new List<object>();
            }

            var userIds = bookings.Select(b => b.UserId).Distinct().ToList();
            var vendorIds = bookings.Select(b => b.VendorId).Distinct().ToList();
            var packageIds = bookings.Where(b => b.PackageId.HasValue).Select(b => b.PackageId!.Value).Distinct().ToList();
            var bookingIds = bookings.Select(b => b.Id).ToList();

            var users = await _db.Users
                .Where(u => userIds.Contains(u.Id))
                .AsNoTracking()
                .ToDictionaryAsync(u => u.Id);

            var vendors = await _db.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .AsNoTracking()
                .ToDictionaryAsync(v => v.Id);

            var packages = await _db.Packages
                .Where(p => packageIds.Contains(p.Id))
                .AsNoTracking()
                .ToDictionaryAsync(p => p.Id);

            var reviewsList = await _db.Reviews
                .Where(r => bookingIds.Contains(r.BookingId) && r.Status != "removed")
                .AsNoTracking()
                .ToListAsync();

            var reviews = reviewsList
                .GroupBy(r => r.BookingId)
                .ToDictionary(g => g.Key, g => g.First());

            var disputeLogs = await _db.BookingLogs
                .Where(l => bookingIds.Contains(l.BookingId) && l.Message.StartsWith("Dispute raised. Reason:"))
                .AsNoTracking()
                .ToListAsync();

            var disputeLogsDict = disputeLogs
                .GroupBy(l => l.BookingId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.CreatedAt).First());

            var result = new List<object>();
            foreach (var b in bookings)
            {
                users.TryGetValue(b.UserId, out var user);
                var customerName = user?.Name ?? "Customer";
                var customerPhone = user?.Phone ?? "";

                vendors.TryGetValue(b.VendorId, out var vendor);
                var vendorName = vendor?.BusinessName ?? "Vendor Partner";

                string mappedStatus = b.Status.ToLower();
                if (mappedStatus == "paid") mappedStatus = "confirmed";

                var services = new List<object>
                {
                    new
                    {
                        serviceId = Guid.NewGuid().ToString(),
                        serviceName = b.PackageName ?? "Full Event Package Service",
                        category = "Event",
                        vendorId = b.VendorId.ToString(),
                        vendorName = vendorName,
                        price = b.Amount,
                        status = "confirmed"
                    }
                };

                packages.TryGetValue(b.PackageId ?? Guid.Empty, out var package);
                if (package != null && package.Includes != null)
                {
                    foreach (var include in package.Includes)
                    {
                        services.Add(new
                        {
                            serviceId = Guid.NewGuid().ToString(),
                            serviceName = include,
                            category = "Included Service",
                            vendorId = b.VendorId.ToString(),
                            vendorName = vendorName,
                            price = 0m,
                            status = "included"
                        });
                    }
                }

                reviews.TryGetValue(b.Id, out var review);
                object? reviewInfo = null;
                if (review != null)
                {
                    reviewInfo = new
                    {
                        rating = review.Rating,
                        comment = review.Comment
                    };
                }

                object? disputeInfo = null;
                if (mappedStatus == "disputed")
                {
                    disputeLogsDict.TryGetValue(b.Id, out var disputeLog);
                    var reason = disputeLog != null 
                        ? disputeLog.Message.Substring("Dispute raised. Reason:".Length).Trim()
                        : "Dispute raised.";

                    disputeInfo = new
                    {
                        reason = reason,
                        status = "open"
                    };
                }

                string eventTypeId = "wedding";
                var nameLower = (b.EventName ?? "").ToLower();
                if (nameLower.Contains("birthday")) eventTypeId = "birthday";
                else if (nameLower.Contains("corporate")) eventTypeId = "corporate";
                else if (nameLower.Contains("beauty")) eventTypeId = "beauty";
                else if (nameLower.Contains("travel")) eventTypeId = "travel";
                else if (nameLower.Contains("shopping")) eventTypeId = "shopping";

                result.Add(new
                {
                    id = b.Id.ToString(),
                    bookingNumber = $"BK-{b.Id.ToString().Substring(0, 8).ToUpper()}",
                    customerId = b.UserId.ToString(),
                    customerName = customerName,
                    customerPhone = customerPhone,
                    eventTypeId = eventTypeId,
                    eventName = string.IsNullOrWhiteSpace(b.EventName) ? "Event Celebration" : b.EventName,
                    packageId = b.PackageId?.ToString() ?? Guid.NewGuid().ToString(),
                    packageName = string.IsNullOrWhiteSpace(b.PackageName) ? "Premium Celebration Package" : b.PackageName,
                    eventDate = b.EventDate.ToString("yyyy-MM-dd"),
                    venue = string.IsNullOrWhiteSpace(b.Venue) ? "Grand Palace Resort" : b.Venue,
                    city = string.IsNullOrWhiteSpace(b.City) ? (user?.City ?? "Mumbai") : b.City,
                    guestCount = b.GuestCount > 0 ? b.GuestCount : 150,
                    status = mappedStatus,
                    advanceAmount = b.AdvanceAmount,
                    baseAmount = Math.Round((b.TotalAmount - b.DamageCharges) / 1.18m, 2),
                    extraServicesAmount = b.ExtraServicesAmount,
                    damageCharges = b.DamageCharges,
                    damageChargeNotes = b.DamageChargeNotes,
                    isDamageChargeApproved = b.IsDamageChargeApproved,
                    gstPercent = 18,
                    totalAmount = b.TotalAmount,
                    finalPaidAmount = b.FinalPaidAmount,
                    cancelledBy = b.CancelledBy,
                    cancellationReason = b.CancellationReason,
                    disputeInfo = disputeInfo,
                    review = reviewInfo,
                    services = services,
                    createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")
                });
            }

            return result;
        }

        [HttpGet("/api/v1/bookings")]
        public async Task<IActionResult> GetBookings([FromQuery] string? userId)
        {
            Guid searchUserId = Guid.Empty;
            if (!string.IsNullOrEmpty(userId) && !Guid.TryParse(userId, out searchUserId))
            {
                searchUserId = GetUserId();
            }
            else if (string.IsNullOrEmpty(userId))
            {
                searchUserId = GetUserId();
            }

            if (searchUserId == Guid.Empty)
            {
                var firstUser = await _db.Users.FirstOrDefaultAsync();
                if (firstUser != null) searchUserId = firstUser.Id;
            }

            var bookings = await _db.Bookings
                .Where(b => b.UserId == searchUserId)
                .ToListAsync();

            var result = await MapBookingsToDtosAsync(bookings);
            return Ok(result);
        }

        private Guid GetUserId()
        {
            var val = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                      ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            return Guid.TryParse(val, out var guid) ? guid : Guid.Empty;
        }

        private string? GetUserRole()
        {
            return User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value 
                   ?? User.FindFirst("role")?.Value;
        }

        public record ConfirmPaymentRequest(string ProviderRef, string Status);
        public record UpdateStatusRequest(string Status);
        public record CancelBookingRequest(string Reason, string CancelledBy);
        public record AddDamageRequest(decimal Amount, string Notes);
        public record RescheduleBookingRequest(DateTime NewDate);
        public record RaiseDisputeRequest(string Reason);

        [Authorize(Policy = "Vendor")]
        [HttpGet("/api/v1/bookings/vendor")]
        public async Task<IActionResult> GetVendorBookings()
        {
            var userId = GetUserId();
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
            if (vendor == null)
            {
                return NotFound(new { error = "Vendor profile not found" });
            }

            var bookings = await _db.Bookings
                .Where(b => b.VendorId == vendor.Id)
                .ToListAsync();

            var result = await MapBookingsToDtosAsync(bookings);
            return Ok(result);
        }

        [Authorize]
        [HttpPost("/api/v1/bookings/{bookingId:guid}/reschedule")]
        public async Task<IActionResult> RescheduleBooking(Guid bookingId, [FromBody] RescheduleBookingRequest req)
        {
            var booking = await _db.Bookings.FindAsync(bookingId);
            if (booking is null) return NotFound();

            var oldDate = booking.EventDate;
            booking.EventDate = req.NewDate;

            _db.BookingLogs.Add(new BookingLog
            {
                Id = Guid.NewGuid(),
                BookingId = bookingId,
                Message = $"Booking rescheduled from {oldDate:yyyy-MM-dd} to {req.NewDate:yyyy-MM-dd}.",
                Actor = GetUserRole() ?? "Customer",
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }

        [Authorize]
        [HttpGet("/api/v1/bookings/{bookingId:guid}/logs")]
        public async Task<IActionResult> GetBookingLogs(Guid bookingId)
        {
            var logs = await _db.BookingLogs
                .Where(l => l.BookingId == bookingId)
                .OrderBy(l => l.CreatedAt)
                .ToListAsync();

            if (!logs.Any())
            {
                var booking = await _db.Bookings.FindAsync(bookingId);
                if (booking != null)
                {
                    logs = new List<BookingLog>
                    {
                        new BookingLog { Id = Guid.NewGuid(), BookingId = bookingId, Message = "Booking request submitted and pending vendor approval.", Actor = "Customer", CreatedAt = booking.EventDate.AddDays(-30) },
                        new BookingLog { Id = Guid.NewGuid(), BookingId = bookingId, Message = "Advance payment received. Booking confirmed.", Actor = "System", CreatedAt = booking.EventDate.AddDays(-28) }
                    };

                    if (booking.Status.Equals("inprogress", StringComparison.OrdinalIgnoreCase))
                    {
                        logs.Add(new BookingLog { Id = Guid.NewGuid(), BookingId = bookingId, Message = "Catering menu finalized. Floral arrangements are being sourced.", Actor = "Vendor", CreatedAt = DateTime.UtcNow.AddDays(-1) });
                    }
                    else if (booking.Status.Equals("completed", StringComparison.OrdinalIgnoreCase))
                    {
                        logs.Add(new BookingLog { Id = Guid.NewGuid(), BookingId = bookingId, Message = "Catering menu finalized. Floral arrangements are being sourced.", Actor = "Vendor", CreatedAt = booking.EventDate.AddDays(-1) });
                        logs.Add(new BookingLog { Id = Guid.NewGuid(), BookingId = bookingId, Message = "Event successfully concluded.", Actor = "System", CreatedAt = booking.EventDate });
                    }
                }
            }

            return Ok(logs);
        }

        [Authorize(Policy = "User")]
        [HttpPost("/api/v1/payment/initiate")]
        public async Task<IActionResult> Initiate([FromBody] InitiatePaymentRequest req)
        {
            var booking = await _db.Bookings.FindAsync(req.BookingId);
            if (booking is null) return NotFound();

            // Security: Enforce user ownership of the booking
            var currentUserId = GetUserId();
            if (booking.UserId != currentUserId)
            {
                return StatusCode(403, new { error = "You do not have permission to pay for this booking." });
            }

            decimal amountToPay = booking.Amount;
            if (!booking.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            {
                amountToPay = booking.TotalAmount - booking.AdvanceAmount;
            }

            if (amountToPay <= 0)
            {
                return BadRequest(new { error = "This booking is already fully paid." });
            }

            var (refId, _) = await _gateway.InitiateAsync(booking.Id, amountToPay, req.PaymentMethod);
            var payment = new Payment { Id = Guid.NewGuid(), BookingId = booking.Id, Amount = amountToPay, ProviderReference = refId };
            _db.Payments.Add(payment);
            await _db.SaveChangesAsync();
            return Ok(new { paymentId = payment.Id, providerRef = refId });
        }

        [Authorize]
        [HttpPost("/api/v1/payment/confirm")]
        public async Task<IActionResult> Confirm([FromBody] ConfirmPaymentRequest req)
        {
            var payment = await _db.Payments.FirstOrDefaultAsync(p => p.ProviderReference == req.ProviderRef);
            if (payment is null) return NotFound();
            
            var ok = await _gateway.ConfirmAsync(req.ProviderRef, req.Status);
            payment.Status = ok ? "Succeeded" : "Failed";
            
            var booking = await _db.Bookings.FindAsync(payment.BookingId);
            if (booking is not null && ok) {
                // If the booking was already confirmed/completed/etc, it means we are paying the final balance.
                // In that case, set status to Settled and record the FinalPaidAmount.
                if (!booking.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                {
                    booking.Status = "Settled";
                    booking.FinalPaidAmount = booking.TotalAmount;
                    _db.BookingLogs.Add(new BookingLog
                    {
                        Id = Guid.NewGuid(),
                        BookingId = booking.Id,
                        Message = "Booking fully settled via successful balance payment confirmation.",
                        Actor = "System",
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    booking.Status = "Paid";
                    _db.BookingLogs.Add(new BookingLog
                    {
                        Id = Guid.NewGuid(),
                        BookingId = booking.Id,
                        Message = "Booking confirmed via successful payment confirmation.",
                        Actor = "System",
                        CreatedAt = DateTime.UtcNow
                    });
                }

                // Award points: 10 points for every ₹100 spent in this specific payment transaction
                int pointsEarned = (int)(payment.Amount / 100) * 10;
                if (pointsEarned > 0)
                {
                    string description = $"Earned points for Booking BK-{booking.Id.ToString().Substring(0, 8).ToUpper()}";
                    await _loyaltyService.AddPointsAsync(booking.UserId, pointsEarned, description, booking.Id);
                }
            }
            await _db.SaveChangesAsync();
            return Ok(new { status = payment.Status });
        }

        [Authorize]
        [HttpPatch("/api/v1/bookings/{bookingId:guid}/status")]
        public async Task<IActionResult> UpdateStatus(Guid bookingId, [FromBody] UpdateStatusRequest req)
        {
            var booking = await _db.Bookings.FindAsync(bookingId);
            if (booking is null) return NotFound();
            
            booking.Status = req.Status;
            
            if (req.Status.Equals("settled", StringComparison.OrdinalIgnoreCase))
            {
                booking.FinalPaidAmount = booking.TotalAmount;
            }
            if (req.Status.Equals("confirmed", StringComparison.OrdinalIgnoreCase) && booking.DamageCharges > 0)
            {
                booking.IsDamageChargeApproved = true;
            }

            _db.BookingLogs.Add(new BookingLog
            {
                Id = Guid.NewGuid(),
                BookingId = bookingId,
                Message = $"Booking status updated to {req.Status}.",
                Actor = GetUserRole() ?? "System",
                CreatedAt = DateTime.UtcNow
            });
            
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }

        [Authorize]
        [HttpPost("/api/v1/bookings/{bookingId:guid}/cancel")]
        public async Task<IActionResult> CancelBooking(Guid bookingId, [FromBody] CancelBookingRequest req)
        {
            var booking = await _db.Bookings.FindAsync(bookingId);
            if (booking is null) return NotFound();
            
            booking.Status = "Cancelled";
            booking.CancelledBy = req.CancelledBy;
            booking.CancellationReason = req.Reason;

            _db.BookingLogs.Add(new BookingLog
            {
                Id = Guid.NewGuid(),
                BookingId = bookingId,
                Message = $"Booking cancelled by {req.CancelledBy}. Reason: {req.Reason}",
                Actor = req.CancelledBy,
                CreatedAt = DateTime.UtcNow
            });
            
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }

        [Authorize]
        [HttpPost("/api/v1/bookings/{bookingId:guid}/damage")]
        public async Task<IActionResult> AddDamage(Guid bookingId, [FromBody] AddDamageRequest req)
        {
            var booking = await _db.Bookings.FindAsync(bookingId);
            if (booking is null) return NotFound();
            
            booking.DamageCharges = req.Amount;
            booking.DamageChargeNotes = req.Notes;
            booking.TotalAmount += req.Amount;

            _db.BookingLogs.Add(new BookingLog
            {
                Id = Guid.NewGuid(),
                BookingId = bookingId,
                Message = $"Damage charges of {req.Amount} added. Notes: {req.Notes}",
                Actor = "Vendor",
                CreatedAt = DateTime.UtcNow
            });
            
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }

        [Authorize]
        [HttpPost("/api/v1/bookings/{bookingId:guid}/dispute")]
        public async Task<IActionResult> RaiseDispute(Guid bookingId, [FromBody] RaiseDisputeRequest req)
        {
            var booking = await _db.Bookings.FindAsync(bookingId);
            if (booking is null) return NotFound();

            booking.Status = "Disputed";
            
            _db.BookingLogs.Add(new BookingLog
            {
                Id = Guid.NewGuid(),
                BookingId = bookingId,
                Message = $"Dispute raised. Reason: {req.Reason}",
                Actor = GetUserRole() ?? "Customer",
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
    }
}

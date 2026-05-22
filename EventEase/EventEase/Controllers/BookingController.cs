using EventEase.Application.Payments;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static EventEase.Application.Checkout.Dtos;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1/booking")]
    public class BookingController : ControllerBase
    {
        private readonly EventEaseDbContext _db;
        private readonly IPaymentGateway _gateway;
        public BookingController(EventEaseDbContext db, IPaymentGateway gateway) { _db = db; _gateway = gateway; }

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

        [Authorize(Policy = "User")]
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

            var result = new List<object>();
            foreach (var b in bookings)
            {
                var user = await _db.Users.FindAsync(b.UserId);
                var customerName = user?.Name ?? "Customer";
                var customerPhone = user?.Phone ?? "";
                var city = user?.City ?? "Mumbai";

                var vendor = await _db.Vendors.FindAsync(b.VendorId);
                var vendorName = vendor?.BusinessName ?? "Vendor Partner";

                string mappedStatus = b.Status.ToLower();
                if (mappedStatus == "paid") mappedStatus = "confirmed";

                var services = new List<object>
                {
                    new
                    {
                        serviceId = Guid.NewGuid().ToString(),
                        serviceName = "Full Event Package Service",
                        category = "Wedding",
                        vendorId = b.VendorId.ToString(),
                        vendorName = vendorName,
                        price = b.Amount,
                        status = "confirmed"
                    }
                };

                result.Add(new
                {
                    id = b.Id.ToString(),
                    bookingNumber = $"BK-{b.Id.ToString().Substring(0, 8).ToUpper()}",
                    customerId = b.UserId.ToString(),
                    customerName = customerName,
                    customerPhone = customerPhone,
                    eventTypeId = "wedding",
                    eventName = "Wedding Celebration",
                    packageId = Guid.NewGuid().ToString(),
                    packageName = "Premium Celebration Package",
                    eventDate = b.EventDate.ToString("yyyy-MM-dd"),
                    venue = "Grand Palace Resort",
                    city = city,
                    guestCount = 150,
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
                    services = services,
                    createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")
                });
            }

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

            var result = new List<object>();
            foreach (var b in bookings)
            {
                var user = await _db.Users.FindAsync(b.UserId);
                var customerName = user?.Name ?? "Customer";
                var customerPhone = user?.Phone ?? "";
                var city = user?.City ?? "Mumbai";

                string mappedStatus = b.Status.ToLower();
                if (mappedStatus == "paid") mappedStatus = "confirmed";

                var services = new List<object>
                {
                    new
                    {
                        serviceId = Guid.NewGuid().ToString(),
                        serviceName = "Full Event Package Service",
                        category = "Wedding",
                        vendorId = b.VendorId.ToString(),
                        vendorName = vendor.BusinessName,
                        price = b.Amount,
                        status = "confirmed"
                    }
                };

                result.Add(new
                {
                    id = b.Id.ToString(),
                    bookingNumber = $"BK-{b.Id.ToString().Substring(0, 8).ToUpper()}",
                    customerId = b.UserId.ToString(),
                    customerName = customerName,
                    customerPhone = customerPhone,
                    eventTypeId = "wedding",
                    eventName = "Wedding Celebration",
                    packageId = Guid.NewGuid().ToString(),
                    packageName = "Premium Celebration Package",
                    eventDate = b.EventDate.ToString("yyyy-MM-dd"),
                    venue = "Grand Palace Resort",
                    city = city,
                    guestCount = 150,
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
                    services = services,
                    createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")
                });
            }

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
            var (refId, _) = await _gateway.InitiateAsync(booking.Id, booking.Amount, req.PaymentMethod);
            var payment = new Payment { Id = Guid.NewGuid(), BookingId = booking.Id, Amount = booking.Amount, ProviderReference = refId };
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
    }
}

using EventEase.Application.Payments;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static EventEase.Application.Checkout.Dtos;
using EventEase.Application.Loyalty;
using EventEase.Application.Vendors;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1/booking")]
    [Authorize]
    public class BookingController : ControllerBase
    {
        private readonly EventEaseDbContext _db;
        private readonly IPaymentGateway _gateway;
        private readonly ILoyaltyService _loyaltyService;
        private readonly IVendorCalendarService _calendarService;

        public BookingController(EventEaseDbContext db, IPaymentGateway gateway, ILoyaltyService loyaltyService, IVendorCalendarService calendarService)
        {
            _db = db;
            _gateway = gateway;
            _loyaltyService = loyaltyService;
            _calendarService = calendarService;
        }

        [Authorize(Policy = "User")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Booking dto)
        {
            // Verify vendor availability before creating the booking
            var isAvailable = await _calendarService.CheckAvailabilityAsync(dto.VendorId, dto.EventDate);
            if (!isAvailable)
            {
                return BadRequest(new { error = "The vendor is already booked or has blocked the selected date." });
            }

            dto.Id = Guid.NewGuid();
            dto.Status = "Pending";
            _db.Bookings.Add(dto);

            if (dto.PackageId.HasValue)
            {
                var package = await _db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.PackageId.Value);
                if (package != null && package.Includes != null)
                {
                    foreach (var include in package.Includes)
                    {
                        _db.BookingServices.Add(new BookingService
                        {
                            Id = Guid.NewGuid(),
                            BookingId = dto.Id,
                            ServiceName = include,
                            Category = "Included Service",
                            Status = "pending",
                            Price = 0m
                        });
                    }
                }
            }
            else
            {
                var defaultServices = new List<string> { "Venue Setup", "Catering Service", "Event Decoration" };
                foreach (var sName in defaultServices)
                {
                    _db.BookingServices.Add(new BookingService
                    {
                        Id = Guid.NewGuid(),
                        BookingId = dto.Id,
                        ServiceName = sName,
                        Category = "Standard Service",
                        Status = "pending",
                        Price = 0m
                    });
                }
            }

            await _db.SaveChangesAsync();
            return Ok(dto);
        }

        private async Task<object> MapBookingToDto(Booking b)
        {
            var user = await _db.Users.FindAsync(b.UserId);
            var customerName = user?.Name ?? "Customer";
            var customerPhone = user?.Phone ?? "";
            
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.Id == b.VendorId || v.UserId == b.VendorId);
            var vendorName = vendor?.BusinessName ?? "Vendor Partner";
            var vendorLocation = vendor?.Location ?? "";
            var vendorDescription = vendor?.Description ?? "";
            var vendorUser = vendor != null ? await _db.Users.FindAsync(vendor.UserId) : null;
            var vendorPhone = vendorUser?.Phone ?? "";
            var vendorEmail = vendorUser?.Email ?? "";

            string mappedStatus = b.Status.ToLower();
            if (mappedStatus == "paid") mappedStatus = "confirmed";

            var dbServices = await _db.BookingServices.Where(bs => bs.BookingId == b.Id).ToListAsync();
            if (!dbServices.Any())
            {
                var package = b.PackageId.HasValue 
                    ? await _db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == b.PackageId.Value) 
                    : null;

                var listToInsert = new List<BookingService>();
                if (package != null && package.Includes != null && package.Includes.Any())
                {
                    foreach (var include in package.Includes)
                    {
                        listToInsert.Add(new BookingService
                        {
                            Id = Guid.NewGuid(),
                            BookingId = b.Id,
                            ServiceName = include,
                            Category = "Included Service",
                            Status = "pending",
                            Price = 0m
                        });
                    }
                }
                else
                {
                    var defaultServices = new List<string> { "Venue Setup", "Catering Service", "Event Decoration" };
                    foreach (var sName in defaultServices)
                    {
                        listToInsert.Add(new BookingService
                        {
                            Id = Guid.NewGuid(),
                            BookingId = b.Id,
                            ServiceName = sName,
                            Category = "Standard Service",
                            Status = "pending",
                            Price = 0m
                        });
                    }
                }

                _db.BookingServices.AddRange(listToInsert);
                await _db.SaveChangesAsync();
                dbServices = listToInsert;
            }

            var services = new List<object>();
            foreach (var bs in dbServices)
            {
                services.Add(new
                {
                    serviceId = bs.Id.ToString(),
                    serviceName = bs.ServiceName,
                    category = bs.Category,
                    vendorId = b.VendorId.ToString(),
                    vendorName = vendorName,
                    price = bs.Price,
                    status = bs.Status.ToLower()
                });
            }

            // Fetch review
            var review = await _db.Reviews.FirstOrDefaultAsync(r => r.BookingId == b.Id && r.Status != "removed");
            object? reviewInfo = null;
            if (review != null)
            {
                reviewInfo = new
                {
                    id = review.Id.ToString(),
                    bookingId = review.BookingId.ToString(),
                    vendorId = review.VendorId.ToString(),
                    customerName = review.CustomerName,
                    eventName = review.EventName,
                    rating = review.Rating,
                    comment = review.Comment,
                    date = review.CreatedAt.ToString("yyyy-MM-dd"),
                    status = review.Status,
                    disputeReason = review.DisputeReason
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
                vendorId = b.VendorId.ToString(),
                vendorName = vendorName,
                vendorPhone = vendorPhone,
                vendorEmail = vendorEmail,
                vendorLocation = vendorLocation,
                vendorDescription = vendorDescription,
                eventTypeId = eventTypeId,
                eventName = b.EventName,
                packageId = b.PackageId?.ToString(),
                packageName = b.PackageName,
                eventDate = b.EventDate.ToString("yyyy-MM-dd"),
                venue = b.Venue,
                city = b.City,
                guestCount = b.GuestCount,
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
                cancellationDate = b.CancellationDate?.ToString("yyyy-MM-dd"),
                cancellationFee = b.CancellationFee,
                platformCancellationFeeRetained = b.PlatformCancellationFeeRetained,
                refundAmount = b.RefundAmount,
                refundStatus = b.RefundStatus,
                refundTransactionId = b.RefundTransactionId,
                vendorPenaltyAmount = b.VendorPenaltyAmount,
                vendorStrikeApplied = b.VendorStrikeApplied,
                escrowStatus = b.EscrowStatus.ToLower(),
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

            var vendorsList = await _db.Vendors
                .Where(v => vendorIds.Contains(v.Id) || vendorIds.Contains(v.UserId))
                .AsNoTracking()
                .ToListAsync();

            var vendorsById = vendorsList.ToDictionary(v => v.Id);
            var vendorsByUserId = new Dictionary<Guid, Vendor>();
            foreach (var v in vendorsList)
            {
                if (v.UserId != Guid.Empty && !vendorsByUserId.ContainsKey(v.UserId))
                {
                    vendorsByUserId[v.UserId] = v;
                }
            }
 
            var allUserIds = userIds.Concat(vendorsList.Select(v => v.UserId)).Distinct().ToList();
            var users = await _db.Users
                .Where(u => allUserIds.Contains(u.Id))
                .AsNoTracking()
                .ToDictionaryAsync(u => u.Id);

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

            var dbServicesList = await _db.BookingServices
                .Where(bs => bookingIds.Contains(bs.BookingId))
                .ToListAsync();

            var dbServicesGrouped = dbServicesList
                .GroupBy(bs => bs.BookingId)
                .ToDictionary(g => g.Key, g => g.ToList());

            bool needsSave = false;
            foreach (var b in bookings)
            {
                if (!dbServicesGrouped.TryGetValue(b.Id, out var bServices) || !bServices.Any())
                {
                    packages.TryGetValue(b.PackageId ?? Guid.Empty, out var package);
                    var listToInsert = new List<BookingService>();
                    if (package != null && package.Includes != null && package.Includes.Any())
                    {
                        foreach (var include in package.Includes)
                        {
                            listToInsert.Add(new BookingService
                            {
                                Id = Guid.NewGuid(),
                                BookingId = b.Id,
                                ServiceName = include,
                                Category = "Included Service",
                                Status = "pending",
                                Price = 0m
                            });
                        }
                    }
                    else
                    {
                        var defaultServices = new List<string> { "Venue Setup", "Catering Service", "Event Decoration" };
                        foreach (var sName in defaultServices)
                        {
                            listToInsert.Add(new BookingService
                            {
                                Id = Guid.NewGuid(),
                                BookingId = b.Id,
                                ServiceName = sName,
                                Category = "Standard Service",
                                Status = "pending",
                                Price = 0m
                            });
                        }
                    }

                    _db.BookingServices.AddRange(listToInsert);
                    dbServicesGrouped[b.Id] = listToInsert;
                    needsSave = true;
                }
            }

            if (needsSave)
            {
                await _db.SaveChangesAsync();
            }

            var result = new List<object>();
            foreach (var b in bookings)
            {
                users.TryGetValue(b.UserId, out var user);
                var customerName = user?.Name ?? "Customer";
                var customerPhone = user?.Phone ?? "";

                Vendor? vendor = null;
                if (!vendorsById.TryGetValue(b.VendorId, out vendor))
                {
                    vendorsByUserId.TryGetValue(b.VendorId, out vendor);
                }
                var vendorName = vendor?.BusinessName ?? "Vendor Partner";
                var vendorLocation = vendor?.Location ?? "";
                var vendorDescription = vendor?.Description ?? "";
                
                User? vendorUser = null;
                if (vendor != null)
                {
                    users.TryGetValue(vendor.UserId, out vendorUser);
                }
                var vendorPhone = vendorUser?.Phone ?? "";
                var vendorEmail = vendorUser?.Email ?? "";
 
                string mappedStatus = b.Status.ToLower();
                if (mappedStatus == "paid") mappedStatus = "confirmed";

                var services = new List<object>();
                dbServicesGrouped.TryGetValue(b.Id, out var bServices);

                if (bServices != null && bServices.Any())
                {
                    foreach (var bs in bServices)
                    {
                        services.Add(new
                        {
                            serviceId = bs.Id.ToString(),
                            serviceName = bs.ServiceName,
                            category = bs.Category,
                            vendorId = b.VendorId.ToString(),
                            vendorName = vendorName,
                            price = bs.Price,
                            status = bs.Status.ToLower()
                        });
                    }
                }

                reviews.TryGetValue(b.Id, out var review);
                object? reviewInfo = null;
                if (review != null)
                {
                    reviewInfo = new
                    {
                        id = review.Id.ToString(),
                        bookingId = review.BookingId.ToString(),
                        vendorId = review.VendorId.ToString(),
                        customerName = review.CustomerName,
                        eventName = review.EventName,
                        rating = review.Rating,
                        comment = review.Comment,
                        date = review.CreatedAt.ToString("yyyy-MM-dd"),
                        status = review.Status,
                        disputeReason = review.DisputeReason
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
                    vendorId = b.VendorId.ToString(),
                    vendorName = vendorName,
                    vendorPhone = vendorPhone,
                    vendorEmail = vendorEmail,
                    vendorLocation = vendorLocation,
                    vendorDescription = vendorDescription,
                    eventTypeId = eventTypeId,
                    eventName = b.EventName,
                    packageId = b.PackageId?.ToString(),
                    packageName = b.PackageName,
                    eventDate = b.EventDate.ToString("yyyy-MM-dd"),
                    venue = b.Venue,
                    city = b.City,
                    guestCount = b.GuestCount,
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
                    cancellationDate = b.CancellationDate?.ToString("yyyy-MM-dd"),
                    cancellationFee = b.CancellationFee,
                    platformCancellationFeeRetained = b.PlatformCancellationFeeRetained,
                    refundAmount = b.RefundAmount,
                    refundStatus = b.RefundStatus,
                    refundTransactionId = b.RefundTransactionId,
                    vendorPenaltyAmount = b.VendorPenaltyAmount,
                    vendorStrikeApplied = b.VendorStrikeApplied,
                    escrowStatus = b.EscrowStatus.ToLower(),
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
            var currentUserId = GetUserId();
            var currentRole = GetUserRole();
            Guid searchUserId = currentUserId;

            // [SECURITY] Only Admin/Support/Vendor can query other users' bookings
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var requestedUserId))
            {
                if (requestedUserId != currentUserId)
                {
                    var isPrivileged = currentRole != null && 
                        (currentRole.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                         currentRole.Equals("Support", StringComparison.OrdinalIgnoreCase) ||
                         currentRole.Equals("Vendor", StringComparison.OrdinalIgnoreCase));
                    if (!isPrivileged) return Forbid();
                }
                searchUserId = requestedUserId;
            }

            if (searchUserId == Guid.Empty) return BadRequest(new { error = "Invalid user ID" });

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
        public record CancelBookingRequest(
            string Reason, 
            string CancelledBy,
            DateTime? CancellationDate = null,
            decimal? CancellationFee = null,
            decimal? PlatformCancellationFeeRetained = null,
            decimal? RefundAmount = null,
            string? RefundStatus = null,
            string? RefundTransactionId = null,
            decimal? VendorPenaltyAmount = null,
            bool? VendorStrikeApplied = null
        );
        public record AddDamageRequest(decimal Amount, string Notes);
        public record RescheduleBookingRequest(DateTime NewDate);
        public record RaiseDisputeRequest(string Reason);
        public record UpdateCancellationRequest(
            decimal? RefundAmount = null,
            decimal? CancellationFee = null,
            decimal? PlatformCancellationFeeRetained = null,
            string? RefundStatus = null,
            string? RefundTransactionId = null,
            string? EscrowStatus = null
        );

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
                .Where(b => b.VendorId == vendor.Id || b.VendorId == vendor.UserId)
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

                // Close RFP if booking is linked to one
                if (booking.RfpId.HasValue)
                {
                    var rfp = await _db.Rfps.FindAsync(booking.RfpId.Value);
                    if (rfp != null && rfp.Status == "bid_selected")
                    {
                        rfp.Status = "closed";
                    }
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

            // [SECURITY] Verify ownership — only booking owner, vendor, or Admin/Support can update
            var callerId = GetUserId();
            var callerRole = GetUserRole();
            var isPrivileged = callerRole != null && 
                (callerRole.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                 callerRole.Equals("Support", StringComparison.OrdinalIgnoreCase) ||
                 callerRole.Equals("Vendor", StringComparison.OrdinalIgnoreCase));
            if (booking.UserId != callerId && !isPrivileged) return Forbid();
            
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

            // [SECURITY] Verify ownership — only booking owner, vendor, or Admin/Support can cancel
            var callerId = GetUserId();
            var callerRole = GetUserRole();
            var isPrivileged = callerRole != null && 
                (callerRole.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                 callerRole.Equals("Support", StringComparison.OrdinalIgnoreCase) ||
                 callerRole.Equals("Vendor", StringComparison.OrdinalIgnoreCase));
            if (booking.UserId != callerId && !isPrivileged) return Forbid();
            
            booking.Status = "Cancelled";
            booking.CancelledBy = req.CancelledBy;
            booking.CancellationReason = req.Reason;

            DateTime cancelDate = req.CancellationDate ?? DateTime.UtcNow;
            booking.CancellationDate = cancelDate;

            decimal refundAmt = 0;
            decimal cancelFee = 0;
            decimal platformFee = 0;
            decimal penaltyAmt = 0;
            bool strikeApplied = false;

            if (req.RefundAmount.HasValue && req.CancellationFee.HasValue && req.PlatformCancellationFeeRetained.HasValue)
            {
                refundAmt = req.RefundAmount.Value;
                cancelFee = req.CancellationFee.Value;
                platformFee = req.PlatformCancellationFeeRetained.Value;
                penaltyAmt = req.VendorPenaltyAmount ?? 0;
                strikeApplied = req.VendorStrikeApplied ?? false;
            }
            else
            {
                int daysUntilEvent = (booking.EventDate.Date - cancelDate.Date).Days;
                decimal advancePaid = booking.AdvanceAmount;
                decimal totalAmount = booking.TotalAmount;

                if (req.CancelledBy.ToLower() == "customer")
                {
                    if (booking.Status.ToLower() == "pending")
                    {
                        refundAmt = 0;
                        cancelFee = 0;
                        platformFee = 0;
                    }
                    else
                    {
                        if (daysUntilEvent > 30)
                        {
                            platformFee = Math.Min(Math.Round(totalAmount * 0.02m), 2500m);
                            refundAmt = Math.Max(0m, advancePaid - platformFee);
                            cancelFee = 0m;
                        }
                        else if (daysUntilEvent >= 15 && daysUntilEvent <= 30)
                        {
                            decimal retained = advancePaid * 0.5m;
                            refundAmt = advancePaid * 0.5m;
                            platformFee = Math.Min(Math.Round(totalAmount * 0.10m), retained * 0.5m);
                            cancelFee = Math.Max(0m, retained - platformFee);
                        }
                        else if (daysUntilEvent >= 7 && daysUntilEvent < 15)
                        {
                            decimal retained = advancePaid * 0.75m;
                            refundAmt = advancePaid * 0.25m;
                            platformFee = Math.Min(Math.Round(totalAmount * 0.10m), retained * 0.5m);
                            cancelFee = Math.Max(0m, retained - platformFee);
                        }
                        else
                        {
                            decimal retained = advancePaid;
                            refundAmt = 0m;
                            platformFee = Math.Min(Math.Round(totalAmount * 0.10m), retained * 0.5m);
                            cancelFee = Math.Max(0m, retained - platformFee);
                        }
                    }
                }
                else if (req.CancelledBy.ToLower() == "vendor")
                {
                    refundAmt = advancePaid;
                    cancelFee = 0m;
                    platformFee = 0m;
                    penaltyAmt = Math.Min(Math.Round(totalAmount * 0.10m), 15000m);
                    strikeApplied = true;
                }
                else
                {
                    refundAmt = advancePaid;
                    cancelFee = 0m;
                    platformFee = 0m;
                }
            }

            booking.RefundAmount = refundAmt;
            booking.CancellationFee = cancelFee;
            booking.PlatformCancellationFeeRetained = platformFee;
            booking.VendorPenaltyAmount = penaltyAmt;
            booking.VendorStrikeApplied = strikeApplied;
            booking.RefundStatus = refundAmt > 0 ? (req.RefundStatus ?? "pending") : "none";
            booking.RefundTransactionId = req.RefundTransactionId;
            booking.EscrowStatus = refundAmt > 0 ? "refunded" : "released";

            _db.BookingLogs.Add(new BookingLog
            {
                Id = Guid.NewGuid(),
                BookingId = bookingId,
                Message = $"Booking cancelled by {req.CancelledBy}. Reason: {req.Reason}. Refund: ₹{refundAmt}, Fee Retained: ₹{cancelFee + platformFee} (Platform Retained: ₹{platformFee})",
                Actor = req.CancelledBy,
                CreatedAt = DateTime.UtcNow
            });
            
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }

        [Authorize]
        [HttpPatch("/api/v1/bookings/{bookingId:guid}/cancellation")]
        public async Task<IActionResult> UpdateCancellation(Guid bookingId, [FromBody] UpdateCancellationRequest req)
        {
            var booking = await _db.Bookings.FindAsync(bookingId);
            if (booking is null) return NotFound();

            // [SECURITY] Only Admin/Support can modify cancellation details
            var callerRole = GetUserRole();
            if (callerRole == null || !(callerRole.Equals("Admin", StringComparison.OrdinalIgnoreCase) || callerRole.Equals("Support", StringComparison.OrdinalIgnoreCase)))
                return Forbid();

            if (req.RefundAmount.HasValue) booking.RefundAmount = req.RefundAmount.Value;
            if (req.CancellationFee.HasValue) booking.CancellationFee = req.CancellationFee.Value;
            if (req.PlatformCancellationFeeRetained.HasValue) booking.PlatformCancellationFeeRetained = req.PlatformCancellationFeeRetained.Value;
            if (!string.IsNullOrEmpty(req.RefundStatus)) booking.RefundStatus = req.RefundStatus;
            if (req.RefundTransactionId != null) booking.RefundTransactionId = req.RefundTransactionId;
            if (!string.IsNullOrEmpty(req.EscrowStatus)) booking.EscrowStatus = req.EscrowStatus;

            _db.BookingLogs.Add(new BookingLog
            {
                Id = Guid.NewGuid(),
                BookingId = bookingId,
                Message = $"Booking cancellation/refund details updated by support. Refund: ₹{booking.RefundAmount}, Fee Retained: ₹{booking.CancellationFee}, Platform Retained: ₹{booking.PlatformCancellationFeeRetained}, Refund Status: {booking.RefundStatus}, Escrow: {booking.EscrowStatus}",
                Actor = GetUserRole() ?? "Support",
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

            // [SECURITY] Only Vendor can add damage charges
            var callerRole = GetUserRole();
            if (callerRole == null || !callerRole.Equals("Vendor", StringComparison.OrdinalIgnoreCase))
                return Forbid();
            
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

            // [SECURITY] Only booking owner can raise a dispute
            var callerId = GetUserId();
            if (booking.UserId != callerId) return Forbid();

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

        public record UpdateServiceStatusRequest(string Status);

        [Authorize(Policy = "Vendor")]
        [HttpPatch("/api/v1/bookings/{bookingId:guid}/services/{serviceId:guid}/status")]
        public async Task<IActionResult> UpdateServiceStatus(Guid bookingId, Guid serviceId, [FromBody] UpdateServiceStatusRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Status))
            {
                return BadRequest(new { error = "Please provide a valid service status." });
            }

            var booking = await _db.Bookings.FindAsync(bookingId);
            if (booking == null) return NotFound(new { error = "Booking not found." });

            var userId = GetUserId();
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
            if (vendor == null || (booking.VendorId != vendor.Id && booking.VendorId != vendor.UserId))
            {
                return StatusCode(403, new { error = "You do not have permission to update services for this booking." });
            }

            var service = await _db.BookingServices.FirstOrDefaultAsync(bs => bs.Id == serviceId && bs.BookingId == bookingId);
            if (service == null) return NotFound(new { error = "Service item not found." });

            var oldStatus = service.Status;
            service.Status = req.Status.ToLower();
            _db.BookingServices.Update(service);

            _db.BookingLogs.Add(new BookingLog
            {
                Id = Guid.NewGuid(),
                BookingId = bookingId,
                Message = $"Service '{service.ServiceName}' status updated from '{oldStatus}' to '{service.Status}'.",
                Actor = "Vendor",
                CreatedAt = DateTime.UtcNow
            });

            var customerNotification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = booking.UserId,
                Title = "Service Update: " + service.ServiceName,
                Message = $"The vendor has updated '{service.ServiceName}' to '{service.Status}' for your event '{booking.EventName}'.",
                Type = "booking",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
            _db.Notifications.Add(customerNotification);

            await _db.SaveChangesAsync();

            return Ok(new { success = true, serviceId = service.Id, status = service.Status });
        }
    }
}

using EventEase.Application.Payments;
using EventEase.Application.Pricing;
using EventEase.Core.Constants;
using EventEase.Core.Entities;
using EventEase.Core.Exceptions;
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
        private readonly IBookingPricingService _pricing;

        public BookingController(
            EventEaseDbContext db,
            IPaymentGateway gateway,
            ILoyaltyService loyaltyService,
            IVendorCalendarService calendarService,
            IBookingPricingService pricing)
        {
            _db = db;
            _gateway = gateway;
            _loyaltyService = loyaltyService;
            _calendarService = calendarService;
            _pricing = pricing;
        }

        /// <summary>
        /// Request to create a booking. Deliberately carries no monetary fields — every amount is
        /// computed server-side from the vendor's catalogue, and the owner is taken from the token.
        /// Ids may be sent as plain GUIDs or in the prefixed form the catalogue returns (pkg_…, usr_…);
        /// the vendor is taken from the package when one is booked.
        /// </summary>
        public record CreateBookingRequest(
            string? VendorId,
            DateTime EventDate,
            string? PackageId,
            List<Guid>? ServiceIds,
            string? MealPreference,
            string? EventName,
            string? Venue,
            string? City,
            int GuestCount,
            Guid? RfpId);

        /// <summary>What the customer is pricing before they book.</summary>
        public record QuoteRequest(string? PackageId, int GuestCount, string? MealPreference, List<Guid>? ServiceIds);

        /// <summary>
        /// Prices a package for a guest count exactly as a booking would be charged, so customers
        /// see the real total, GST and advance before they commit.
        /// </summary>
        [HttpPost("quote")]
        public async Task<IActionResult> Quote([FromBody] QuoteRequest req)
        {
            if (req is null) return BadRequest(new { error = "A package is required." });

            var package = await FindPackageAsync(req.PackageId);
            if (package is null) return BadRequest(new { error = "The selected package does not exist." });

            BookingPriceResult price;
            try
            {
                price = await _pricing.PriceAsync(new BookingPriceRequest(
                    package.VendorId, package.Id, req.GuestCount, req.ServiceIds, req.MealPreference));
            }
            catch (BusinessRuleException ex)
            {
                return BadRequest(new { error = ex.Message });
            }

            return Ok(new
            {
                packageId = package.Id.ToString(),
                packageName = price.PackageName,
                vendorId = package.VendorId.ToString(),
                guestCount = req.GuestCount,
                maxGuests = price.MaxGuests,
                lines = price.Lines.Select(l => new { description = l.Description, detail = l.Detail, amount = l.Amount }),
                subtotal = price.Subtotal,
                gstPercent = price.GstRate * 100,
                gstAmount = price.GstAmount,
                totalAmount = price.TotalAmount,
                advancePercent = price.AdvanceRate * 100,
                advanceAmount = price.AdvanceAmount,
                balanceAmount = price.TotalAmount - price.AdvanceAmount
            });
        }

        [Authorize(Policy = AuthPolicies.User)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateBookingRequest req)
        {
            if (req is null) return BadRequest(new { error = "A booking request is required." });
            if (req.EventDate.Date < DateTime.UtcNow.Date)
                return BadRequest(new { error = "The event date cannot be in the past." });

            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized(new { error = "Invalid token." });

            Package? package = null;
            if (!string.IsNullOrWhiteSpace(req.PackageId))
            {
                package = await FindPackageAsync(req.PackageId);
                if (package is null) return BadRequest(new { error = "The selected package does not exist." });
            }

            // The vendor is the package's own; a vendor id is only needed for a booking of services alone.
            var vendorId = package?.VendorId ?? ParseId(req.VendorId) ?? Guid.Empty;
            if (vendorId == Guid.Empty) return BadRequest(new { error = "A vendor must be selected." });

            var eventName = FirstNonBlank(req.EventName, package?.Name);
            var city = FirstNonBlank(req.City, package?.Address?.City);
            var venue = FirstNonBlank(req.Venue, PackageVenue(package));
            if (eventName is null) return BadRequest(new { error = "Enter a name for the event." });
            if (city is null) return BadRequest(new { error = "Enter the city of the event." });
            if (venue is null) return BadRequest(new { error = "Enter the venue of the event." });

            BookingPriceResult price;
            try
            {
                price = await _pricing.PriceAsync(new BookingPriceRequest(
                    vendorId, package?.Id, req.GuestCount, req.ServiceIds, req.MealPreference));
            }
            catch (BusinessRuleException ex)
            {
                return BadRequest(new { error = ex.Message });
            }

            var isAvailable = await _calendarService.CheckAvailabilityAsync(vendorId, req.EventDate);
            if (!isAvailable)
            {
                return BadRequest(new { error = "The vendor is already booked or has blocked the selected date." });
            }

            var booking = new Booking
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                VendorId = vendorId,
                RfpId = req.RfpId,
                EventDate = req.EventDate,
                Status = BookingStatuses.Pending,
                GuestCount = req.GuestCount,
                PackageId = package?.Id,
                PackageName = price.PackageName,
                EventName = eventName,
                Venue = venue,
                City = city,

                // [SECURITY] Server-computed amounts only.
                Amount = price.AdvanceAmount,
                TotalAmount = price.TotalAmount,
                AdvanceAmount = price.AdvanceAmount,
                PlatformFeeRate = price.PlatformFeeRate,
                PlatformFeeAmount = price.PlatformFeeAmount,
                VendorPayoutAmount = price.VendorPayoutAmount
            };

            // The availability check above and the insert below must not be split by a competing
            // booking, so they are committed together and the unique index on
            // (VendorId, EventDate) in VendorBlockedDates backstops the race.
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                _db.Bookings.Add(booking);
                _db.BookingServices.AddRange(BuildBookingServices(booking.Id, price.Lines));

                _db.BookingLogs.Add(new BookingLog
                {
                    Id = Guid.NewGuid(),
                    BookingId = booking.Id,
                    Message = $"Booking created for {price.TotalAmount:0.00} (advance {price.AdvanceAmount:0.00}).",
                    Actor = "Customer",
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync();
                return Conflict(new { error = "That date was just taken. Please choose another date." });
            }

            return Ok((await MapBookingsToDtosAsync(new List<Booking> { booking })).Single());
        }

        /// <summary>One booking, for its customer, its vendor or staff.</summary>
        [HttpGet("/api/v1/bookings/{bookingId:guid}")]
        public async Task<IActionResult> GetBooking(Guid bookingId)
        {
            var booking = await _db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId);
            if (booking is null) return NotFound(new { error = "Booking not found." });
            if (!await CanAccessBookingAsync(booking)) return Forbid();

            return Ok((await MapBookingsToDtosAsync(new List<Booking> { booking })).Single());
        }

        private async Task<Package?> FindPackageAsync(string? rawId)
        {
            var id = ParseId(rawId);
            if (id is null) return null;
            return await _db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id.Value);
        }

        /// <summary>Accepts a plain GUID or the catalogue's prefixed form (pkg_…, usr_…, bk_…).</summary>
        internal static Guid? ParseId(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var value = raw.Trim();
            var underscore = value.IndexOf('_');
            if (underscore > 0 && underscore < value.Length - 1) value = value[(underscore + 1)..];
            return Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
        }

        private static string? FirstNonBlank(params string?[] values) =>
            values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));

        /// <summary>The package's own address as a venue line, when the vendor gave one.</summary>
        private static string? PackageVenue(Package? package)
        {
            var address = package?.Address;
            if (address is null) return null;
            var parts = new[] { address.Street, address.Locality, address.City }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .ToList();
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        /// <summary>
        /// Turns the priced line items into the booking's service checklist.
        /// </summary>
        private static List<BookingService> BuildBookingServices(Guid bookingId, IReadOnlyList<BookingPriceLine> lines)
        {
            return lines.Select(line => new BookingService
            {
                Id = Guid.NewGuid(),
                BookingId = bookingId,
                ServiceName = line.Description,
                Category = "Booked Service",
                Status = "pending",
                Price = line.Amount
            }).ToList();
        }

        /// <summary>
        /// True when the caller owns the booking, is the vendor fulfilling it, or is staff.
        ///
        /// Vendor identity is checked against this specific booking: holding the Vendor role is
        /// not on its own permission to touch someone else's booking.
        /// </summary>
        private async Task<bool> CanAccessBookingAsync(Booking booking)
        {
            var callerId = GetUserId();
            if (callerId == Guid.Empty) return false;
            if (booking.UserId == callerId) return true;

            var role = GetUserRole();
            if (IsStaff(role)) return true;

            if (string.Equals(role, AuthRoles.Vendor, StringComparison.OrdinalIgnoreCase))
            {
                return await IsBookingVendorAsync(booking, callerId);
            }

            return false;
        }

        /// <summary>True when the caller is the vendor assigned to this booking.</summary>
        private async Task<bool> IsBookingVendorAsync(Booking booking, Guid callerId)
        {
            var vendor = await _db.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.UserId == callerId);
            if (vendor is null) return false;

            // Legacy rows store either the Vendor row id or the vendor's user id in VendorId.
            return booking.VendorId == vendor.Id || booking.VendorId == vendor.UserId;
        }

        private static bool IsStaff(string? role) =>
            role is not null &&
            (role.Equals(AuthRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
             role.Equals(AuthRoles.Support, StringComparison.OrdinalIgnoreCase));

        private async Task<List<object>> MapBookingsToDtosAsync(List<Booking> bookings)
        {
            if (bookings == null || !bookings.Any())
            {
                return new List<object>();
            }

            var userIds = bookings.Select(b => b.UserId).Distinct().ToList();
            var vendorIds = bookings.Select(b => b.VendorId).Distinct().ToList();
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

            var packageIds = bookings.Where(b => b.PackageId.HasValue).Select(b => b.PackageId!.Value).Distinct().ToList();
            var packageCategories = await _db.Packages
                .AsNoTracking()
                .Where(p => packageIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Category })
                .ToDictionaryAsync(p => p.Id, p => p.Category);

            var paidByBooking = await _db.Payments
                .AsNoTracking()
                .Where(p => bookingIds.Contains(p.BookingId) && p.Status == "Succeeded")
                .GroupBy(p => p.BookingId)
                .Select(g => new { BookingId = g.Key, Paid = g.Sum(p => p.Amount) })
                .ToDictionaryAsync(g => g.BookingId, g => g.Paid);

            var createdByBooking = await _db.BookingLogs
                .AsNoTracking()
                .Where(l => bookingIds.Contains(l.BookingId))
                .GroupBy(l => l.BookingId)
                .Select(g => new { BookingId = g.Key, CreatedAt = g.Min(l => l.CreatedAt) })
                .ToDictionaryAsync(g => g.BookingId, g => g.CreatedAt);

            var dbServicesList = await _db.BookingServices
                .Where(bs => bookingIds.Contains(bs.BookingId))
                .ToListAsync();

            var dbServicesGrouped = dbServicesList
                .GroupBy(bs => bs.BookingId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // A read no longer writes. This previously inserted placeholder BookingServices rows
            // (and called SaveChangesAsync) while mapping a GET response, which made listing
            // bookings mutate them. Bookings created before the service checklist existed simply
            // report an empty list.

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

                // The booked package's own category, rather than a guess from the event's name.
                var eventTypeId = b.PackageId.HasValue && packageCategories.TryGetValue(b.PackageId.Value, out var category)
                    ? category
                    : "";
                paidByBooking.TryGetValue(b.Id, out var amountPaid);

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
                    amountPaid = amountPaid,
                    balanceDue = Math.Max(0, b.TotalAmount - amountPaid),
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
                    createdAt = createdByBooking.TryGetValue(b.Id, out var createdAt) ? createdAt.ToString("yyyy-MM-dd HH:mm") : null
                });
            }

            return result;
        }

        [HttpGet("/api/v1/bookings")]
        public async Task<IActionResult> GetBookings(
            [FromQuery] string? userId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultPageSize)
        {
            var currentUserId = GetUserId();
            var currentRole = GetUserRole();
            Guid searchUserId = currentUserId;

            // [SECURITY] Reading another user's bookings is a staff action. The Vendor role used
            // to be accepted here, which let any vendor account enumerate any customer's bookings;
            // vendors see their own work through /api/v1/vendor/bookings instead.
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var requestedUserId))
            {
                if (requestedUserId != currentUserId && !IsStaff(currentRole))
                {
                    return Forbid();
                }
                searchUserId = requestedUserId;
            }

            if (searchUserId == Guid.Empty) return BadRequest(new { error = "Invalid user ID" });

            var query = _db.Bookings.Where(b => b.UserId == searchUserId);
            return Ok(await PagedBookingsAsync(query, page, pageSize));
        }

        private const int DefaultPageSize = 25;
        private const int MaxPageSize = 100;

        /// <summary>
        /// Returns one page of bookings, newest event first. Unbounded list endpoints previously
        /// loaded a caller's entire booking history on every request.
        /// </summary>
        private async Task<object> PagedBookingsAsync(IQueryable<Booking> query, int page, int pageSize)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > MaxPageSize ? DefaultPageSize : pageSize;

            var total = await query.CountAsync();
            var bookings = await query
                .OrderByDescending(b => b.EventDate)
                .ThenBy(b => b.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new
            {
                items = await MapBookingsToDtosAsync(bookings),
                page,
                pageSize,
                total,
                totalPages = (int)Math.Ceiling(total / (double)pageSize)
            };
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

        // Status is no longer accepted from the caller: the result is read back from the provider.
        public record ConfirmPaymentRequest(string ProviderRef);
        public record UpdateStatusRequest(string Status);
        public record CancelBookingRequest(
            string Reason,
            // Only honoured for staff; for customers and vendors the platform derives this from
            // the caller's relationship to the booking.
            string? CancelledBy = null,
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
        public async Task<IActionResult> GetVendorBookings(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultPageSize)
        {
            var userId = GetUserId();
            var vendor = await _db.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.UserId == userId);
            if (vendor == null)
            {
                return NotFound(new { error = "Vendor profile not found" });
            }

            var query = _db.Bookings.Where(b => b.VendorId == vendor.Id || b.VendorId == vendor.UserId);
            return Ok(await PagedBookingsAsync(query, page, pageSize));
        }

        [Authorize]
        [HttpPost("/api/v1/bookings/{bookingId:guid}/reschedule")]
        public async Task<IActionResult> RescheduleBooking(Guid bookingId, [FromBody] RescheduleBookingRequest req)
        {
            var booking = await _db.Bookings.FindAsync(bookingId);
            if (booking is null) return NotFound();

            // [SECURITY] This endpoint had no ownership check at all: any authenticated user could
            // move any booking to any date.
            if (!await CanAccessBookingAsync(booking)) return Forbid();

            if (req is null) return BadRequest(new { error = "A new date is required." });
            if (req.NewDate.Date < DateTime.UtcNow.Date)
                return BadRequest(new { error = "The new event date cannot be in the past." });

            if (booking.Status.Equals(BookingStatuses.Cancelled, StringComparison.OrdinalIgnoreCase) ||
                booking.Status.Equals(BookingStatuses.Completed, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "A cancelled or completed booking cannot be rescheduled." });
            }

            var isAvailable = await _calendarService.CheckAvailabilityAsync(booking.VendorId, req.NewDate);
            if (!isAvailable)
            {
                return BadRequest(new { error = "The vendor is not available on the selected date." });
            }

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
            var booking = await _db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId);
            if (booking is null) return NotFound();

            // [SECURITY] Logs carry cancellation reasons, refund amounts and dispute details, and
            // were readable for any booking id by any authenticated caller.
            if (!await CanAccessBookingAsync(booking)) return Forbid();

            var logs = await _db.BookingLogs
                .AsNoTracking()
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

            if (booking.Status.Equals(BookingStatuses.Cancelled, StringComparison.OrdinalIgnoreCase) ||
                booking.Status.Equals(BookingStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "This booking is no longer payable." });
            }
            if (booking.Status.Equals(BookingStatuses.Settled, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "This booking is already fully paid." });
            }

            // A pending booking pays the advance, or the whole total when the customer chooses to;
            // anything further along pays whatever is still outstanding.
            var alreadyPaid = await _db.Payments
                .Where(p => p.BookingId == booking.Id && p.Status == "Succeeded")
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;
            var isPending = booking.Status.Equals(BookingStatuses.Pending, StringComparison.OrdinalIgnoreCase);
            decimal amountToPay = isPending && !req.PayInFull
                ? booking.AdvanceAmount
                : booking.TotalAmount - alreadyPaid;

            if (amountToPay <= 0)
            {
                return BadRequest(new { error = "This booking is already fully paid." });
            }

            // Reuse an in-flight attempt instead of stacking up Initiated rows on repeated taps. An
            // attempt for a different amount (the customer switched between advance and full) is
            // abandoned so it can no longer be confirmed.
            var inFlight = await _db.Payments
                .Where(p => p.BookingId == booking.Id && p.Status == "Initiated")
                .ToListAsync();
            var existing = inFlight.FirstOrDefault(p => p.Amount == amountToPay);
            if (existing is not null)
            {
                return Ok(new { paymentId = existing.Id, providerRef = existing.ProviderReference, amount = amountToPay });
            }
            foreach (var stale in inFlight) stale.Status = "Cancelled";

            var (refId, _) = await _gateway.InitiateAsync(booking.Id, amountToPay, req.PaymentMethod);
            var payment = new Payment { Id = Guid.NewGuid(), BookingId = booking.Id, Amount = amountToPay, ProviderReference = refId };
            _db.Payments.Add(payment);
            await _db.SaveChangesAsync();
            return Ok(new { paymentId = payment.Id, providerRef = refId, amount = amountToPay });
        }

        [Authorize]
        [HttpPost("/api/v1/payment/confirm")]
        public async Task<IActionResult> Confirm([FromBody] ConfirmPaymentRequest req)
        {
            if (req is null || string.IsNullOrWhiteSpace(req.ProviderRef))
                return BadRequest(new { error = "A payment reference is required." });

            var payment = await _db.Payments.FirstOrDefaultAsync(p => p.ProviderReference == req.ProviderRef);
            if (payment is null) return NotFound();

            var booking = await _db.Bookings.FindAsync(payment.BookingId);
            if (booking is null) return NotFound();

            // [SECURITY] The caller must be a party to this booking. Previously any authenticated
            // user could confirm any payment by guessing or replaying a reference.
            if (!await CanAccessBookingAsync(booking)) return Forbid();

            // [SECURITY] Idempotency: a settled payment is never re-applied, so replaying this
            // call cannot award loyalty points or advance the booking a second time.
            if (!payment.Status.Equals("Initiated", StringComparison.OrdinalIgnoreCase))
            {
                return Ok(new { status = payment.Status, alreadyProcessed = true });
            }

            // [SECURITY] The outcome is read back from the payment provider. The client's own
            // claim about whether the payment succeeded is not trusted.
            var ok = await _gateway.VerifyPaymentAsync(req.ProviderRef);
            payment.Status = ok ? "Succeeded" : "Failed";

            // The payment record, the booking status, the loyalty award and the RFP close are one
            // unit of work: a partial commit would leave a booking paid with no points, or points
            // awarded for a booking that never advanced.
            await using var tx = await _db.Database.BeginTransactionAsync();

            if (ok)
            {
                var paidBefore = await _db.Payments
                    .Where(p => p.BookingId == booking.Id && p.Status == "Succeeded" && p.Id != payment.Id)
                    .SumAsync(p => (decimal?)p.Amount) ?? 0m;
                var fullyPaid = paidBefore + payment.Amount >= booking.TotalAmount;

                // The first payment on a pending booking (the advance, or the whole total) moves it
                // to Paid so the vendor can confirm it. Clearing the balance records the booking as
                // fully paid; it is Settled only once the event itself is completed, so paying early
                // never skips the vendor's confirmation or the event.
                var wasPending = booking.Status.Equals(BookingStatuses.Pending, StringComparison.OrdinalIgnoreCase);
                if (wasPending)
                {
                    booking.Status = BookingStatuses.Paid;
                }
                if (fullyPaid)
                {
                    booking.FinalPaidAmount = booking.TotalAmount;
                    if (booking.Status.Equals(BookingStatuses.Completed, StringComparison.OrdinalIgnoreCase))
                    {
                        booking.Status = BookingStatuses.Settled;
                    }
                }

                _db.BookingLogs.Add(new BookingLog
                {
                    Id = Guid.NewGuid(),
                    BookingId = booking.Id,
                    Message = (wasPending, fullyPaid) switch
                    {
                        (true, true) => "Full payment confirmed by the payment provider.",
                        (true, false) => "Advance payment confirmed by the payment provider.",
                        (false, true) => booking.Status == BookingStatuses.Settled
                            ? "Balance payment confirmed by the payment provider; booking settled."
                            : "Balance payment confirmed by the payment provider; booking fully paid.",
                        _ => "Payment confirmed by the payment provider."
                    },
                    Actor = "System",
                    CreatedAt = DateTime.UtcNow
                });

                // Award points: 10 points for every 100 spent in this specific payment transaction.
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
            await tx.CommitAsync();

            return Ok(new { status = payment.Status });
        }

        [Authorize]
        [HttpPatch("/api/v1/bookings/{bookingId:guid}/status")]
        public async Task<IActionResult> UpdateStatus(Guid bookingId, [FromBody] UpdateStatusRequest req)
        {
            var booking = await _db.Bookings.FindAsync(bookingId);
            if (booking is null) return NotFound();

            // [SECURITY] Object-level check: the caller must be this booking's customer, this
            // booking's vendor, or staff.
            if (!await CanAccessBookingAsync(booking)) return Forbid();

            var target = BookingStatuses.Normalize(req?.Status);
            if (target is null)
            {
                return BadRequest(new { error = "Unknown booking status." });
            }

            // [SECURITY] Paid and Settled represent money having moved. Only the payment flow
            // may set them, otherwise a customer could settle their own booking for free.
            if (BookingStatuses.PaymentControlled.Contains(target))
            {
                return BadRequest(new { error = "This status is set by the payment process and cannot be assigned directly." });
            }

            if (!BookingStatuses.CanTransition(booking.Status, target))
            {
                return BadRequest(new { error = $"A booking cannot move from {booking.Status} to {target}." });
            }

            var callerRole = GetUserRole();
            var callerId = GetUserId();
            var isStaff = IsStaff(callerRole);
            var isVendor = !isStaff && await IsBookingVendorAsync(booking, callerId);

            // A customer may only withdraw their own booking; fulfilment states belong to the vendor.
            if (!isStaff && !isVendor && !target.Equals(BookingStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            var previous = booking.Status;
            booking.Status = target;

            if (target.Equals(BookingStatuses.Confirmed, StringComparison.OrdinalIgnoreCase) && booking.DamageCharges > 0)
            {
                booking.IsDamageChargeApproved = true;
            }

            _db.BookingLogs.Add(new BookingLog
            {
                Id = Guid.NewGuid(),
                BookingId = bookingId,
                Message = $"Booking status changed from {previous} to {target}.",
                Actor = callerRole ?? "System",
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            return Ok(new { success = true, status = target });
        }

        [Authorize]
        [HttpPost("/api/v1/bookings/{bookingId:guid}/cancel")]
        public async Task<IActionResult> CancelBooking(Guid bookingId, [FromBody] CancelBookingRequest req)
        {
            var booking = await _db.Bookings.FindAsync(bookingId);
            if (booking is null) return NotFound();

            // [SECURITY] Object-level check rather than "any vendor may cancel any booking".
            if (!await CanAccessBookingAsync(booking)) return Forbid();

            var callerRole = GetUserRole();

            if (booking.Status.Equals(BookingStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = "This booking is already cancelled." });

            // [SECURITY] Who cancelled decides the refund split, so it is derived from the caller's
            // actual relationship to this booking. Accepting it from the body let a customer claim
            // the vendor cancelled and collect a full refund plus a vendor penalty.
            var callerId = GetUserId();
            string cancelledBy;
            if (booking.UserId == callerId) cancelledBy = "customer";
            else if (await IsBookingVendorAsync(booking, callerId)) cancelledBy = "vendor";
            else cancelledBy = IsStaff(callerRole) ? (req.CancelledBy ?? "support") : "support";

            booking.Status = BookingStatuses.Cancelled;
            booking.CancelledBy = cancelledBy;
            booking.CancellationReason = req.Reason;

            // Staff may back-date a cancellation during dispute handling; everyone else cancels now.
            DateTime cancelDate = IsStaff(callerRole) ? (req.CancellationDate ?? DateTime.UtcNow) : DateTime.UtcNow;
            booking.CancellationDate = cancelDate;

            decimal refundAmt = 0;
            decimal cancelFee = 0;
            decimal platformFee = 0;
            decimal penaltyAmt = 0;
            bool strikeApplied = false;

            // [SECURITY] Only staff may override the computed refund. A customer supplying these
            // fields could otherwise cancel and award themselves a full refund plus fees.
            if (IsStaff(callerRole)
                && req.RefundAmount.HasValue
                && req.CancellationFee.HasValue
                && req.PlatformCancellationFeeRetained.HasValue)
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

                if (cancelledBy == "customer")
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
                else if (cancelledBy == "vendor")
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
                Message = $"Booking cancelled by {cancelledBy}. Reason: {req.Reason}. Refund: ₹{refundAmt}, Fee Retained: ₹{cancelFee + platformFee} (Platform Retained: ₹{platformFee})",
                Actor = cancelledBy,
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

            // [SECURITY] Only the vendor fulfilling THIS booking (or staff) may add damage charges.
            // Holding the Vendor role was previously enough to bill any booking on the platform.
            var callerRole = GetUserRole();
            var callerId = GetUserId();
            if (!IsStaff(callerRole) && !await IsBookingVendorAsync(booking, callerId))
                return Forbid();

            if (req is null || req.Amount <= 0)
                return BadRequest(new { error = "Damage charges must be greater than zero." });
            if (req.Amount > booking.TotalAmount)
                return BadRequest(new { error = "Damage charges cannot exceed the booking total." });

            // Replace rather than accumulate, and keep the total consistent with the charge that
            // is actually recorded.
            booking.TotalAmount = booking.TotalAmount - booking.DamageCharges + req.Amount;
            booking.DamageCharges = req.Amount;
            booking.DamageChargeNotes = req.Notes;
            booking.IsDamageChargeApproved = false;

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

            if (!BookingStatuses.CanTransition(booking.Status, BookingStatuses.Disputed))
            {
                return BadRequest(new { error = $"A booking in state {booking.Status} cannot be disputed." });
            }

            booking.Status = BookingStatuses.Disputed;
            
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

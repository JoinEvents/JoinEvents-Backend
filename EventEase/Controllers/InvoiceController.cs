using EventEase.Core.Constants;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;

namespace EventEase.Api.Controllers
{
    [ApiController]
    public class InvoiceController : ControllerBase
    {
        private readonly EventEaseDbContext _db;

        public InvoiceController(EventEaseDbContext db)
        {
            _db = db;
        }

        private Guid GetUserId()
        {
            var val = User.FindFirstValue(ClaimTypes.NameIdentifier) 
                      ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? User.FindFirstValue("sub")
                      ?? User.FindFirstValue("id");
            return Guid.TryParse(val, out var guid) ? guid : Guid.Empty;
        }

        [Authorize(Policy = AuthPolicies.Vendor)]
        [HttpGet("api/v1/vendor/invoices")]
        public async Task<IActionResult> GetVendorInvoices()
        {
            var userId = GetUserId();
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
            if (vendor == null)
            {
                return Ok(new { success = true, data = new List<object>() });
            }

            var bookings = await _db.Bookings
                .Where(b => b.VendorId == vendor.Id)
                .ToListAsync();

            var userIds = bookings.Select(b => b.UserId).Distinct().ToList();
            var users = await _db.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Name);

            var invoices = bookings.Select(b =>
            {
                // Fallbacks if platform fee details aren't filled yet
                decimal platformFee = b.PlatformFeeAmount > 0 ? b.PlatformFeeAmount : Math.Round(b.TotalAmount * 0.10m, 2);
                decimal tds = b.TdsDeducted > 0 ? b.TdsDeducted : Math.Round(b.TotalAmount * 0.01m, 2);
                decimal netPayout = b.VendorPayoutAmount > 0 ? b.VendorPayoutAmount : Math.Round(b.TotalAmount - platformFee - tds, 2);

                return new
                {
                    id = $"INV-{b.Id.ToString().Substring(0, 8).ToUpper()}",
                    bookingId = b.Id.ToString(),
                    customer = users.TryGetValue(b.UserId, out var name) ? name : "Customer",
                    date = b.EventDate.ToString("yyyy-MM-dd"),
                    grossAmount = b.TotalAmount,
                    platformFee = platformFee,
                    tds = tds,
                    netPayout = netPayout,
                    status = b.Status.ToLower() == "paid" ? "paid" : "pending",
                    downloadUrl = $"/api/v1/invoices/{b.Id}/download"
                };
            }).ToList();

            return Ok(new { success = true, data = invoices });
        }

        [Authorize(Policy = AuthPolicies.Admin)]
        [HttpGet("api/v1/admin/invoices")]
        public async Task<IActionResult> GetAdminInvoices()
        {
            var bookings = await _db.Bookings.ToListAsync();

            var userIds = bookings.Select(b => b.UserId).Distinct().ToList();
            var users = await _db.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Name);

            var invoices = bookings.Select(b =>
            {
                decimal platformFee = b.PlatformFeeAmount > 0 ? b.PlatformFeeAmount : Math.Round(b.TotalAmount * 0.10m, 2);
                decimal tds = b.TdsDeducted > 0 ? b.TdsDeducted : Math.Round(b.TotalAmount * 0.01m, 2);
                decimal netPayout = b.VendorPayoutAmount > 0 ? b.VendorPayoutAmount : Math.Round(b.TotalAmount - platformFee - tds, 2);

                return new
                {
                    id = $"INV-{b.Id.ToString().Substring(0, 8).ToUpper()}",
                    bookingId = b.Id.ToString(),
                    customer = users.TryGetValue(b.UserId, out var name) ? name : "Customer",
                    date = b.EventDate.ToString("yyyy-MM-dd"),
                    grossAmount = b.TotalAmount,
                    platformFee = platformFee,
                    tds = tds,
                    netPayout = netPayout,
                    status = b.Status.ToLower() == "paid" ? "paid" : "pending",
                    downloadUrl = $"/api/v1/invoices/{b.Id}/download"
                };
            }).ToList();

            return Ok(new { success = true, data = invoices });
        }

        /// <summary>
        /// The invoice for one booking.
        /// </summary>
        /// <remarks>
        /// Two things were wrong here, and both are worth stating because the
        /// fix changes what callers get back.
        ///
        /// It was [Authorize] with no ownership check, so any signed-in user
        /// could read any booking's invoice by guessing an id — every booking
        /// on the platform, from any account. It now serves the booking's own
        /// customer, the vendor fulfilling it, and support or admin. Anyone
        /// else gets a 404 rather than a 403, so the endpoint cannot be used
        /// to discover which booking ids exist.
        ///
        /// It also answered the vendor's settlement statement — platform fee,
        /// TDS and net payout — to whoever called it, which told customers
        /// exactly what the platform takes and what their vendor nets. The
        /// customer now gets what they were charged; the payout breakdown
        /// stays with the vendor, support and admin.
        /// </remarks>
        [Authorize]
        [HttpGet("api/v1/invoices/{id}/download")]
        public async Task<IActionResult> DownloadInvoice(Guid id)
        {
            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == id);
            if (booking == null)
            {
                return NotFound(new { error = "Booking not found." });
            }

            var userId = GetUserId();
            if (userId == Guid.Empty)
            {
                return Unauthorized(new { error = "Unauthorized", details = "User ID not found in token claims." });
            }

            var isStaff = HasAnyRole(AuthRoles.Admin, AuthRoles.Support);
            var isCustomer = booking.UserId == userId;

            // A vendor account is not the vendor row: the booking points at the
            // vendor, which points back at the user.
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.Id == booking.VendorId);
            var isVendor = vendor != null && vendor.UserId == userId;

            if (!isStaff && !isCustomer && !isVendor)
            {
                // Deliberately the same answer as a booking that does not
                // exist. A 403 here would confirm the id is real.
                return NotFound(new { error = "Booking not found." });
            }

            var customer = await _db.Users.FirstOrDefaultAsync(u => u.Id == booking.UserId);
            var reference = booking.Id.ToString().Substring(0, 8).ToUpper();

            var content = isCustomer && !isStaff && !isVendor
                ? BuildCustomerReceipt(booking, customer, vendor, reference)
                : BuildSettlementStatement(booking, customer, vendor, reference);

            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            var prefix = isCustomer && !isStaff && !isVendor ? "Receipt" : "Invoice";
            return File(bytes, "text/plain", $"{prefix}-{reference}.txt");
        }

        /// <summary>What the customer paid, with no platform or payout figures.</summary>
        private static string BuildCustomerReceipt(
            Core.Entities.Booking booking,
            Core.Entities.User? customer,
            Core.Entities.Vendor? vendor,
            string reference)
        {
            decimal baseAmount = Math.Round((booking.TotalAmount - booking.DamageCharges) / 1.18m, 2);
            decimal gst = Math.Round(booking.TotalAmount - booking.DamageCharges - baseAmount, 2);
            decimal balance = Math.Max(0m, booking.TotalAmount - booking.AdvanceAmount);

            var lines = new List<string>
            {
                "==================================================",
                "               JOINEVENTS RECEIPT",
                "==================================================",
                $"Receipt No:      RCP-{reference}",
                $"Booking ID:      {booking.Id}",
                $"Date:            {DateTime.UtcNow:yyyy-MM-dd}",
                $"Event:           {booking.EventName}",
                "--------------------------------------------------",
                $"Customer:        {customer?.Name ?? "Customer"}",
                $"Vendor:          {vendor?.BusinessName ?? "Vendor"}",
                $"Venue:           {booking.Venue}, {booking.City}",
                "--------------------------------------------------",
                $"Package & services: INR {baseAmount:N2}"
            };

            if (booking.DamageCharges > 0)
            {
                lines.Add($"Damage charges:     INR {booking.DamageCharges:N2}");
            }

            lines.Add($"GST (18%):          INR {gst:N2}");
            lines.Add($"Total:              INR {booking.TotalAmount:N2}");
            lines.Add("--------------------------------------------------");
            lines.Add($"Advance paid:       INR {booking.AdvanceAmount:N2}");

            if (balance > 0)
            {
                lines.Add($"Balance due:        INR {balance:N2}");
            }

            if (booking.RefundAmount > 0)
            {
                lines.Add($"Refunded:           INR {booking.RefundAmount:N2}");
            }

            lines.Add("--------------------------------------------------");
            lines.Add("Thank you for using JoinEvents!");
            lines.Add("==================================================");

            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        /// <summary>The settlement view: what the platform took and the vendor nets.</summary>
        private static string BuildSettlementStatement(
            Core.Entities.Booking booking,
            Core.Entities.User? customer,
            Core.Entities.Vendor? vendor,
            string reference)
        {
            decimal platformFee = booking.PlatformFeeAmount > 0 ? booking.PlatformFeeAmount : Math.Round(booking.TotalAmount * 0.10m, 2);
            decimal tds = booking.TdsDeducted > 0 ? booking.TdsDeducted : Math.Round(booking.TotalAmount * 0.01m, 2);
            decimal netPayout = booking.VendorPayoutAmount > 0 ? booking.VendorPayoutAmount : Math.Round(booking.TotalAmount - platformFee - tds, 2);

            return $@"
==================================================
                 JOINEVENTS INVOICE
==================================================
Invoice ID:      INV-{reference}
Booking ID:      {booking.Id}
Date:            {DateTime.UtcNow:yyyy-MM-dd}
Event:           {booking.EventName}
--------------------------------------------------
Customer:        {customer?.Name ?? "Customer"}
Vendor:          {vendor?.BusinessName ?? "Vendor"}
Venue:           {booking.Venue}, {booking.City}
--------------------------------------------------
Gross Booking:   INR {booking.TotalAmount:N2}
Platform Fee:    INR {platformFee:N2}
TDS (194-O):     INR {tds:N2}
Net Vendor Payout: INR {netPayout:N2}
--------------------------------------------------
Thank you for using JoinEvents!
==================================================
";
        }

        /// <summary>True when the caller holds any of the given roles.</summary>
        private bool HasAnyRole(params string[] roles)
        {
            var role = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");
            return !string.IsNullOrEmpty(role)
                && roles.Any(r => role.Equals(r, StringComparison.OrdinalIgnoreCase));
        }
    }
}

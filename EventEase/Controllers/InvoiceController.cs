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

        [Authorize(Policy = "Vendor")]
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

        [Authorize(Policy = "Admin")]
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

        [Authorize]
        [HttpGet("api/v1/invoices/{id}/download")]
        public async Task<IActionResult> DownloadInvoice(Guid id)
        {
            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == id);
            if (booking == null)
            {
                return NotFound(new { error = "Booking not found." });
            }

            var customer = await _db.Users.FirstOrDefaultAsync(u => u.Id == booking.UserId);
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.Id == booking.VendorId);

            decimal platformFee = booking.PlatformFeeAmount > 0 ? booking.PlatformFeeAmount : Math.Round(booking.TotalAmount * 0.10m, 2);
            decimal tds = booking.TdsDeducted > 0 ? booking.TdsDeducted : Math.Round(booking.TotalAmount * 0.01m, 2);
            decimal netPayout = booking.VendorPayoutAmount > 0 ? booking.VendorPayoutAmount : Math.Round(booking.TotalAmount - platformFee - tds, 2);

            string invoiceContent = $@"
==================================================
                 JOINEVENTS INVOICE
==================================================
Invoice ID:      INV-{booking.Id.ToString().Substring(0, 8).ToUpper()}
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
            var bytes = System.Text.Encoding.UTF8.GetBytes(invoiceContent);
            return File(bytes, "text/plain", $"Invoice-{booking.Id.ToString().Substring(0, 8).ToUpper()}.txt");
        }
    }
}

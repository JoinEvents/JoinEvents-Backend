using EventEase.Core.Constants;
using EventEase.Core.Enums;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1/admin/dashboard")]
    [Authorize(Policy = AuthPolicies.Admin)]
    public class AdminDashboardController : ControllerBase
    {
        private readonly EventEaseDbContext _db;

        public AdminDashboardController(EventEaseDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetDashboardStats()
        {
            var currentYear = DateTime.UtcNow.Year;

            // Compute total revenue directly in DB from valid statuses
            var validStatuses = new[] { 
                BookingStatus.Paid.ToString().ToLower(), 
                BookingStatus.Confirmed.ToString().ToLower(), 
                BookingStatus.InProgress.ToString().ToLower(), 
                BookingStatus.Completed.ToString().ToLower(), 
                "settled" 
            };

            var totalRevenue = await _db.Bookings
                .Where(b => validStatuses.Contains(b.Status.ToLower()))
                .SumAsync(b => (decimal?)b.TotalAmount) ?? 0m;

            var activeEvents = await _db.Bookings
                .CountAsync(b => b.Status.ToLower() == BookingStatus.Confirmed.ToString().ToLower() || b.Status.ToLower() == BookingStatus.InProgress.ToString().ToLower());

            var pendingVerifications = await _db.Vendors
                .CountAsync(v => !v.IsValidated);

            var totalCustomers = await _db.Users
                .CountAsync(u => u.Role.ToLower() == AuthRoles.Customer.ToLower() || u.Role.ToLower() == AuthRoles.User.ToLower());

            var totalVendors = await _db.Vendors
                .CountAsync();

            var completedEvents = await _db.Bookings
                .CountAsync(b => b.Status.ToLower() == BookingStatus.Completed.ToString().ToLower() || b.Status.ToLower() == "settled");

            var openTickets = await _db.SupportTickets
                .CountAsync(t => t.Status.ToLower() == SupportTicketStatus.Open.ToString().ToLower() || 
                                 t.Status.ToLower() == SupportTicketStatus.InProgress.ToString().ToLower());

            // Compute monthly revenue for current year grouped directly in DB
            var monthlyData = await _db.Bookings
                .Where(b => b.EventDate.Year == currentYear && validStatuses.Contains(b.Status.ToLower()))
                .GroupBy(b => b.EventDate.Month)
                .Select(g => new { Month = g.Key, Revenue = g.Sum(b => b.TotalAmount) })
                .ToListAsync();

            var monthlyRevenue = new decimal[12];
            foreach (var item in monthlyData)
            {
                if (item.Month >= 1 && item.Month <= 12)
                {
                    monthlyRevenue[item.Month - 1] = item.Revenue;
                }
            }

            return Ok(new
            {
                totalRevenue,
                activeEvents,
                pendingVerifications,
                totalCustomers,
                totalVendors,
                completedEvents,
                openTickets,
                monthlyRevenue = monthlyRevenue.Select(r => (double)r).ToArray()
            });
        }
    }
}

using EventEase.Core.Constants;
using EventEase.Core.Entities;
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
    [Route("api/v1/admin/users")]
    [Authorize(Policy = AuthPolicies.Admin)]
    public class AdminUsersController : ControllerBase
    {
        private readonly EventEaseDbContext _db;

        public AdminUsersController(EventEaseDbContext db)
        {
            _db = db;
        }

        // ─── CUSTOMERS ──────────────────────────────────────────────────────────

        [HttpGet("customers")]
        public async Task<IActionResult> GetCustomers()
        {
            var customers = await _db.Users
                .Where(u => u.Role == "Customer")
                .ToListAsync();

            var customerIds = customers.Select(c => c.Id).ToList();

            // Fetch bookings counts and totals to calculate real statistics
            var bookingStats = await _db.Bookings
                .Where(b => customerIds.Contains(b.UserId))
                .GroupBy(b => b.UserId)
                .Select(g => new
                {
                    UserId = g.Key,
                    TotalBookings = g.Count(),
                    TotalSpent = g.Sum(b => b.TotalAmount)
                })
                .ToDictionaryAsync(x => x.UserId, x => x);

            var response = customers.Select(c =>
            {
                bookingStats.TryGetValue(c.Id, out var stats);
                return new
                {
                    id = c.Id.ToString(),
                    name = c.Name,
                    email = c.Email,
                    phone = c.Phone,
                    city = c.City ?? "Unknown",
                    totalBookings = stats?.TotalBookings ?? 0,
                    totalSpent = stats?.TotalSpent ?? 0m,
                    joinedDate = c.CreatedAt.ToString("yyyy-MM-dd"),
                    accountStatus = c.AccountStatus.ToLower(),
                    loyaltyPoints = c.LoyaltyPoints,
                    strikes = c.Strikes,
                    suspensionReason = c.SuspensionReason,
                    suspensionDuration = c.SuspensionDuration
                };
            }).ToList();

            return Ok(response);
        }

        public class ModerateCustomerDto
        {
            public string Action { get; set; } = string.Empty; // warn, restrict, suspend, ban, reactivate
            public string? Reason { get; set; }
            public string? Duration { get; set; }
        }

        [HttpPost("customers/{id:guid}/moderate")]
        public async Task<IActionResult> ModerateCustomer(Guid id, [FromBody] ModerateCustomerDto dto)
        {
            var customer = await _db.Users.FindAsync(id);
            if (customer == null)
            {
                return NotFound(new { error = "Customer not found" });
            }

            var action = dto.Action.ToLower();
            if (action == "warn")
            {
                customer.AccountStatus = "warning";
                customer.Strikes++;
                customer.SuspensionReason = dto.Reason;
            }
            else if (action == "restrict")
            {
                customer.AccountStatus = "restricted";
                customer.SuspensionReason = dto.Reason;
            }
            else if (action == "suspend")
            {
                customer.AccountStatus = "suspended";
                customer.SuspensionReason = dto.Reason;
                customer.SuspensionDuration = dto.Duration;
            }
            else if (action == "ban")
            {
                customer.AccountStatus = "banned";
                customer.SuspensionReason = dto.Reason;
            }
            else if (action == "reactivate")
            {
                customer.AccountStatus = "active";
                customer.Strikes = 0;
                customer.SuspensionReason = null;
                customer.SuspensionDuration = null;
            }
            else
            {
                return BadRequest(new { error = "Invalid action" });
            }

            await _db.SaveChangesAsync();

            // Log administrative action
            try
            {
                var auditLog = new AuditLog
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow,
                    ActorId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "admin",
                    ActorName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Admin",
                    ActorRole = "admin",
                    Action = $"Customer Moderation ({action})",
                    Description = $"Admin moderated customer {customer.Name} ({customer.Email}) - Action: {action}. Reason: {dto.Reason}",
                    EntityType = "customer",
                    EntityId = customer.Id.ToString(),
                    EntityName = customer.Name,
                    Severity = action == "ban" || action == "suspend" ? "critical" : "warning",
                    MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { action, reason = dto.Reason, duration = dto.Duration })
                };
                _db.AuditLogs.Add(auditLog);
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to log customer moderation audit log");
            }

            return Ok(new { success = true, id = customer.Id, status = customer.AccountStatus });
        }

        // ─── EMPLOYEES ──────────────────────────────────────────────────────────

        [HttpGet("employees")]
        public async Task<IActionResult> GetEmployees()
        {
            var employeeRoles = new[] { AuthRoles.Admin, AuthRoles.Support, AuthRoles.Moderator, AuthRoles.Finance };
            var employees = await _db.Users
                .Where(u => employeeRoles.Contains(u.Role))
                .ToListAsync();

            var response = employees.Select(e => new
            {
                id = e.Id.ToString(),
                name = e.Name,
                email = e.Email,
                phone = e.Phone,
                employeeId = e.EmployeeId ?? $"EMP-{e.Id.ToString().Substring(0, 4).ToUpper()}",
                role = e.Role.ToLower(),
                department = e.Department ?? "Operations",
                designation = e.Designation ?? "Staff Member",
                shift = e.Shift ?? "General (9 AM – 6 PM)",
                joinedDate = e.CreatedAt.ToString("yyyy-MM-dd"),
                status = e.AccountStatus.ToLower() == "suspended" ? "suspended" : (e.AccountStatus.ToLower() == "on_leave" ? "on_leave" : "active"),
                ticketsResolved = e.TicketsResolved,
                performanceScore = e.PerformanceScore,
                lastLogin = e.LastLogin?.ToString("yyyy-MM-dd HH:mm"),
                suspensionReason = e.SuspensionReason
            }).ToList();

            return Ok(response);
        }

        public class CreateEmployeeDto
        {
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Phone { get; set; } = string.Empty;
            public string EmployeeId { get; set; } = string.Empty;
            public string Role { get; set; } = "support";
            public string Department { get; set; } = string.Empty;
            public string Designation { get; set; } = string.Empty;
            public string Shift { get; set; } = "General (9 AM – 6 PM)";
            public int PerformanceScore { get; set; }
            public int TicketsResolved { get; set; }
        }

        [HttpPost("employees")]
        public async Task<IActionResult> AddEmployee([FromBody] CreateEmployeeDto dto)
        {
            if (string.IsNullOrEmpty(dto.Email) || string.IsNullOrEmpty(dto.Name))
            {
                return BadRequest(new { error = "Name and Email are required" });
            }

            var existing = await _db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (existing != null)
            {
                return BadRequest(new { error = "User with this email already exists" });
            }

            // Capitalize role to match standard "Admin", "Support", "Moderator", "Finance"
            var role = char.ToUpper(dto.Role[0]) + dto.Role.Substring(1).ToLower();

            var newEmp = new User
            {
                Id = Guid.NewGuid(),
                Name = dto.Name,
                Email = dto.Email,
                Phone = dto.Phone,
                Role = role,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("JoinEvents@2025", workFactor: 12),
                CreatedAt = DateTime.UtcNow,
                EmployeeId = dto.EmployeeId,
                Department = dto.Department,
                Designation = dto.Designation,
                Shift = dto.Shift,
                PerformanceScore = dto.PerformanceScore,
                TicketsResolved = dto.TicketsResolved,
                AccountStatus = "active"
            };

            _db.Users.Add(newEmp);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                id = newEmp.Id.ToString(),
                name = newEmp.Name,
                email = newEmp.Email,
                phone = newEmp.Phone,
                employeeId = newEmp.EmployeeId,
                role = newEmp.Role.ToLower(),
                department = newEmp.Department,
                designation = newEmp.Designation,
                shift = newEmp.Shift,
                joinedDate = newEmp.CreatedAt.ToString("yyyy-MM-dd"),
                status = "active",
                ticketsResolved = newEmp.TicketsResolved,
                performanceScore = newEmp.PerformanceScore
            });
        }

        public class UpdateEmployeeDto
        {
            public string Name { get; set; } = string.Empty;
            public string Phone { get; set; } = string.Empty;
            public string EmployeeId { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
            public string Department { get; set; } = string.Empty;
            public string Designation { get; set; } = string.Empty;
            public string Shift { get; set; } = string.Empty;
            public int PerformanceScore { get; set; }
            public int TicketsResolved { get; set; }
        }

        [HttpPut("employees/{id:guid}")]
        public async Task<IActionResult> UpdateEmployee(Guid id, [FromBody] UpdateEmployeeDto dto)
        {
            var emp = await _db.Users.FindAsync(id);
            if (emp == null)
            {
                return NotFound(new { error = "Employee not found" });
            }

            emp.Name = dto.Name;
            emp.Phone = dto.Phone;
            emp.EmployeeId = dto.EmployeeId;
            emp.Department = dto.Department;
            emp.Designation = dto.Designation;
            emp.Shift = dto.Shift;
            emp.PerformanceScore = dto.PerformanceScore;
            emp.TicketsResolved = dto.TicketsResolved;

            if (!string.IsNullOrEmpty(dto.Role))
            {
                emp.Role = char.ToUpper(dto.Role[0]) + dto.Role.Substring(1).ToLower();
            }

            await _db.SaveChangesAsync();
            return Ok(true);
        }

        public class UpdateEmployeeStatusDto
        {
            public string Status { get; set; } = string.Empty; // active, suspended, on_leave
            public string? Reason { get; set; }
        }

        [HttpPost("employees/{id:guid}/status")]
        public async Task<IActionResult> UpdateEmployeeStatus(Guid id, [FromBody] UpdateEmployeeStatusDto dto)
        {
            var emp = await _db.Users.FindAsync(id);
            if (emp == null)
            {
                return NotFound(new { error = "Employee not found" });
            }

            var status = dto.Status.ToLower();
            if (status == "active")
            {
                emp.AccountStatus = "active";
                emp.SuspensionReason = null;
            }
            else if (status == "suspended")
            {
                emp.AccountStatus = "suspended";
                emp.SuspensionReason = dto.Reason;
            }
            else if (status == "on_leave")
            {
                emp.AccountStatus = "on_leave";
                emp.SuspensionReason = dto.Reason;
            }
            else
            {
                return BadRequest(new { error = "Invalid status" });
            }

            await _db.SaveChangesAsync();
            return Ok(true);
        }
    }
}

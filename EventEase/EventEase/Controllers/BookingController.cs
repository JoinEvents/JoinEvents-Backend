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
    [Route("booking")]
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
        [HttpPost("/payment/initiate")]
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
        [HttpPost("/payment/confirm")]
        public async Task<IActionResult> Confirm([FromBody] dynamic body)
        {
            string providerRef = body.providerRef;
            string status = body.status;
            var payment = await _db.Payments.FirstOrDefaultAsync(p => p.ProviderReference == providerRef);
            if (payment is null) return NotFound();
            var ok = await _gateway.ConfirmAsync(providerRef, status);
            payment.Status = ok ? "Succeeded" : "Failed";
            var booking = await _db.Bookings.FindAsync(payment.BookingId);
            if (booking is not null && ok) booking.Status = "Paid";
            await _db.SaveChangesAsync();
            return Ok(new { status = payment.Status });
        }
    }
}

using EventEase.Application.Loyalty;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace EventEase.Controllers
{
    [ApiController]
    [Route("api/v1/loyalty")]
    public class LoyaltyController : ControllerBase
    {
        private readonly ILoyaltyService _loyaltyService;

        public LoyaltyController(ILoyaltyService loyaltyService)
        {
            _loyaltyService = loyaltyService;
        }

        [HttpGet("balance")]
        public async Task<IActionResult> GetBalance([FromQuery] Guid userId)
        {
            if (userId == Guid.Empty) return BadRequest("Invalid user ID");

            try
            {
                var balance = await _loyaltyService.GetBalanceAsync(userId);
                return Ok(balance);
            }
            catch (Exception ex)
            {
                if (ex.Message == "User not found") return NotFound(ex.Message);
                return StatusCode(500, "An error occurred while fetching balance");
            }
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory([FromQuery] Guid userId)
        {
            if (userId == Guid.Empty) return BadRequest("Invalid user ID");

            try
            {
                var history = await _loyaltyService.GetHistoryAsync(userId);
                return Ok(history);
            }
            catch (Exception)
            {
                return StatusCode(500, "An error occurred while fetching history");
            }
        }

        [HttpPost("calculate-discount")]
        public async Task<IActionResult> CalculateDiscount([FromBody] CalculateDiscountRequestDto request)
        {
            if (request == null || request.UserId == Guid.Empty) return BadRequest("Invalid request");

            try
            {
                var response = await _loyaltyService.CalculateDiscountAsync(request);
                return Ok(response);
            }
            catch (Exception)
            {
                return StatusCode(500, "An error occurred while calculating discount");
            }
        }

        [HttpPost("redeem")]
        public async Task<IActionResult> Redeem([FromBody] RedeemRequestDto request)
        {
            if (request == null || request.UserId == Guid.Empty) return BadRequest("Invalid request");

            try
            {
                var response = await _loyaltyService.RedeemPointsAsync(request);
                if (!response.Success)
                {
                    return BadRequest(response.ErrorMessage);
                }
                return Ok(response);
            }
            catch (Exception)
            {
                return StatusCode(500, "An error occurred while redeeming points");
            }
        }

        [HttpPost("refer")]
        public async Task<IActionResult> Refer([FromBody] ReferFriendRequestDto request)
        {
            if (request == null || request.UserId == Guid.Empty || string.IsNullOrWhiteSpace(request.FriendEmail))
                return BadRequest("Invalid request");

            try
            {
                // Do NOT credit points immediately. Invitation sent.
                var balance = await _loyaltyService.GetBalanceAsync(request.UserId);
                return Ok(new ReferFriendResponseDto
                {
                    Success = true,
                    PointsEarned = 0,
                    NewBalance = balance.Points,
                    NewTier = balance.Tier
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while processing referral: {ex.Message}");
            }
        }

        [HttpPost("review")]
        public async Task<IActionResult> Review([FromBody] ReviewBonusRequestDto request)
        {
            if (request == null || request.UserId == Guid.Empty || request.BookingId == Guid.Empty)
                return BadRequest("Invalid request");

            try
            {
                string description = $"Review Reward: Booking BK-{request.BookingId.ToString().Substring(0, 8).ToUpper()}";
                await _loyaltyService.AddPointsAsync(request.UserId, 50, description, request.BookingId);
                
                var balance = await _loyaltyService.GetBalanceAsync(request.UserId);
                return Ok(new
                {
                    Success = true,
                    PointsEarned = 50,
                    NewBalance = balance.Points,
                    NewTier = balance.Tier
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while processing review points: {ex.Message}");
            }
        }
    }
}

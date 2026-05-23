using System;

namespace EventEase.Application.Loyalty
{
    public class LoyaltyBalanceDto
    {
        public int Points { get; set; }
        public string Tier { get; set; } = string.Empty;
        public int? PointsToNextTier { get; set; }
    }

    public class LoyaltyTransactionDto
    {
        public Guid Id { get; set; }
        public int Points { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime Date { get; set; }
    }

    public class RedeemRequestDto
    {
        public Guid UserId { get; set; }
        public Guid? BookingId { get; set; }
        public int PointsToRedeem { get; set; }
    }

    public class RedeemResponseDto
    {
        public bool Success { get; set; }
        public int NewBalance { get; set; }
        public int DiscountApplied { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class CalculateDiscountRequestDto
    {
        public Guid UserId { get; set; }
        public int PointsToRedeem { get; set; }
    }

    public class CalculateDiscountResponseDto
    {
        public bool Valid { get; set; }
        public int DiscountAmount { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class ReferFriendRequestDto
    {
        public Guid UserId { get; set; }
        public string FriendEmail { get; set; } = string.Empty;
    }

    public class ReferFriendResponseDto
    {
        public bool Success { get; set; }
        public int PointsEarned { get; set; }
        public int NewBalance { get; set; }
        public string NewTier { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }

    public class ReviewBonusRequestDto
    {
        public Guid UserId { get; set; }
        public Guid BookingId { get; set; }
    }
}

using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventEase.Application.Loyalty
{
    public class LoyaltyService : ILoyaltyService
    {
        private readonly EventEaseDbContext _context;

        public LoyaltyService(EventEaseDbContext context)
        {
            _context = context;
        }

        public async Task<LoyaltyBalanceDto> GetBalanceAsync(Guid userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null) throw new Exception("User not found");

            // Calculate balance dynamically from transactions to auto-heal manual DB entries
            var earned = await _context.LoyaltyTransactions
                .Where(t => t.UserId == userId && t.Type == "earned")
                .SumAsync(t => t.Points);
                
            var redeemed = await _context.LoyaltyTransactions
                .Where(t => t.UserId == userId && t.Type == "redeemed")
                .SumAsync(t => t.Points);
                
            var calculatedPoints = earned - redeemed;

            // Auto-heal the User table if it got out of sync from manual DB inserts
            if (user.LoyaltyPoints != calculatedPoints)
            {
                user.LoyaltyPoints = calculatedPoints;
                UpdateUserTier(user);
                await _context.SaveChangesAsync();
            }

            return new LoyaltyBalanceDto
            {
                Points = user.LoyaltyPoints,
                Tier = user.LoyaltyTier ?? "Bronze",
                PointsToNextTier = CalculatePointsToNextTier(user.LoyaltyPoints, user.LoyaltyTier)
            };
        }

        public async Task<List<LoyaltyTransactionDto>> GetHistoryAsync(Guid userId)
        {
            var transactions = await _context.LoyaltyTransactions
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.Date)
                .ToListAsync();

            return transactions.Select(t => new LoyaltyTransactionDto
            {
                Id = t.Id,
                Points = t.Points,
                Type = t.Type,
                Description = t.Description,
                Date = t.Date
            }).ToList();
        }

        public async Task<CalculateDiscountResponseDto> CalculateDiscountAsync(CalculateDiscountRequestDto request)
        {
            var user = await _context.Users.FindAsync(request.UserId);
            if (user == null) return new CalculateDiscountResponseDto { Valid = false, ErrorMessage = "User not found" };

            if (request.PointsToRedeem <= 0 || request.PointsToRedeem % 100 != 0)
            {
                return new CalculateDiscountResponseDto { Valid = false, ErrorMessage = "Points must be a multiple of 100" };
            }

            if (user.LoyaltyPoints < request.PointsToRedeem)
            {
                return new CalculateDiscountResponseDto { Valid = false, ErrorMessage = "Insufficient points" };
            }

            int discount = request.PointsToRedeem * 5;
            return new CalculateDiscountResponseDto { Valid = true, DiscountAmount = discount };
        }

        public async Task<RedeemResponseDto> RedeemPointsAsync(RedeemRequestDto request)
        {
            var user = await _context.Users.FindAsync(request.UserId);
            if (user == null) return new RedeemResponseDto { Success = false, ErrorMessage = "User not found" };

            if (request.PointsToRedeem <= 0 || request.PointsToRedeem % 100 != 0)
            {
                return new RedeemResponseDto { Success = false, ErrorMessage = "Points must be a multiple of 100" };
            }

            if (user.LoyaltyPoints < request.PointsToRedeem)
            {
                return new RedeemResponseDto { Success = false, ErrorMessage = "Insufficient points" };
            }

            // Deduct points
            user.LoyaltyPoints -= request.PointsToRedeem;
            UpdateUserTier(user);

            // Create transaction record
            var transaction = new LoyaltyTransaction
            {
                UserId = request.UserId,
                Points = request.PointsToRedeem,
                Type = "redeemed",
                Description = "Points redeemed for discount",
                BookingId = request.BookingId,
                Date = DateTime.UtcNow
            };

            _context.LoyaltyTransactions.Add(transaction);
            await _context.SaveChangesAsync();

            // 1 point = 5 discount
            int discount = request.PointsToRedeem * 5;

            return new RedeemResponseDto
            {
                Success = true,
                NewBalance = user.LoyaltyPoints,
                DiscountApplied = discount
            };
        }

        public async Task AddPointsAsync(Guid userId, int points, string description, Guid? bookingId = null)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null) throw new Exception("User not found");

            if (bookingId.HasValue && description.StartsWith("Review Reward"))
            {
                var exists = await _context.LoyaltyTransactions
                    .AnyAsync(t => t.UserId == userId && t.BookingId == bookingId.Value && t.Description.StartsWith("Review Reward"));
                if (exists)
                {
                    return;
                }
            }

            user.LoyaltyPoints += points;
            UpdateUserTier(user);

            var transaction = new LoyaltyTransaction
            {
                UserId = userId,
                Points = points,
                Type = "earned",
                Description = description,
                BookingId = bookingId,
                Date = DateTime.UtcNow
            };

            _context.LoyaltyTransactions.Add(transaction);
            await _context.SaveChangesAsync();
        }

        private void UpdateUserTier(User user)
        {
            if (user.LoyaltyPoints >= 15000)
                user.LoyaltyTier = "Gold";
            else if (user.LoyaltyPoints >= 5000)
                user.LoyaltyTier = "Silver";
            else
                user.LoyaltyTier = "Bronze";
        }

        private int? CalculatePointsToNextTier(int points, string? currentTier)
        {
            var tier = currentTier ?? "Bronze";
            if (tier == "Bronze") return Math.Max(0, 5000 - points);
            if (tier == "Silver") return Math.Max(0, 15000 - points);
            return null; // Gold is max
        }
    }
}

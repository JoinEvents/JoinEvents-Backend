using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EventEase.Application.Loyalty
{
    public interface ILoyaltyService
    {
        Task<LoyaltyBalanceDto> GetBalanceAsync(Guid userId);
        Task<List<LoyaltyTransactionDto>> GetHistoryAsync(Guid userId);
        Task<CalculateDiscountResponseDto> CalculateDiscountAsync(CalculateDiscountRequestDto request);
        Task<RedeemResponseDto> RedeemPointsAsync(RedeemRequestDto request);
        Task AddPointsAsync(Guid userId, int points, string description, Guid? bookingId = null);
    }
}

using EventEase.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Application.Auth
{
    public class Dtos
    {
        public record RegisterDto(string name, string phone, string email, string role); // Role optional
        public record VerifyDto(string Phone, string Otp);
        public record AuthTokens(string AccessToken, string RefreshToken, DateTime RefreshExpires, User User);
        public record LoginDto(string email, string password, string? role);
        public record UserProfileDto(Guid id, string name, string email, string phone, string city, string address, string bio, string joinedDate, string accountStatus, int loyaltyPoints, string loyaltyTier, string? referralCode, bool emailNotifications, bool inAppNotifications, bool smsNotifications, string? avatar);
        public record RegisterWithPasswordDto(string name, string email, string password, string phone, string role = "Customer", string? referralCode = null, string? city = null, string? businessName = null);
        public record UpdateProfileDto(string? name, string? phone, string? city, string? address, string? bio, bool? emailNotifications, bool? inAppNotifications, bool? smsNotifications);
        public record UpdatePasswordDto(string currentPassword, string newPassword);
    }
}

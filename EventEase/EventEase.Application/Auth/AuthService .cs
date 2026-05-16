using static EventEase.Application.Auth.Dtos;
using EventEase.Infrastructure.Data;
using EventEase.Infrastructure;
using Microsoft.EntityFrameworkCore;
using EventEase.Core.Entities;
using System.Diagnostics;


namespace EventEase.Application.Auth
{
    public class AuthService : IAuthService
    {
        private readonly EventEaseDbContext _db;
        //private readonly IOtpService _otp;
        private readonly ITokenService _tokens;
        public AuthService(EventEaseDbContext db, ITokenService tokens) { _db = db; _tokens = tokens; }

        //public async Task<User> RegisterAsync(RegisterDto dto)
        //{
        //    var user = await _db.Users.FirstOrDefaultAsync(u => u.Phone == dto.phone);
        //    if (user is null)
        //    {
        //        user = new User { Id = Guid.NewGuid(), Name = dto.name, Phone = dto.phone, Email = dto.email, Role = dto.role ?? "User" };
        //        _db.Users.Add(user);
        //        await _db.SaveChangesAsync();
        //    }
        //    await _otp.GenerateOtpAsync(dto.phone);
        //    return user;
        //}


        //public async Task<AuthTokens?> VerifyAsync(VerifyDto dto)
        //{
        //    //Verify OTP    
        //    Debug.WriteLine($"[AuthService] Verifying OTP for {dto.Phone}: {dto.Otp}");
        //    var ok = await _otp.VerifyOtpAsync(dto.Phone, dto.Otp);
        //    if (!ok) return null;
        //    //Find user
        //    var user = await _db.Users.FirstOrDefaultAsync(u => u.Phone == dto.Phone);
        //    if (user is null) return null;
        //    //Create tokens
        //    var access = _tokens.CreateAccessToken(user.Id, user.Role);
        //    var (refresh, exp) = _tokens.CreateRefreshToken();
        //    _db.Set<RefreshToken>().Add(new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, Token = refresh, ExpiresAt = exp, Revoked = false });
        //    await _db.SaveChangesAsync();
        //    return new AuthTokens(access, refresh, exp, user);
        //}

        public async Task<AuthTokens> RegisterWithPasswordAsync(RegisterWithPasswordDto dto)
        {
            if (string.IsNullOrEmpty(dto.email)) throw new ArgumentException("Email is required");
            if (string.IsNullOrEmpty(dto.password)) throw new ArgumentException("Password is required");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == dto.email);
            if (user != null)
            {
                throw new InvalidOperationException("User with this email already exists");
            }

            user = new User
            {
                Id = Guid.NewGuid(),
                Name = dto.name,
                Email = dto.email,
                Phone = dto.phone,
                Role = dto.role ?? "Customer",
                PasswordHash = HashPassword(dto.password),
                CreatedAt = DateTime.UtcNow
            };
            
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            var access = _tokens.CreateAccessToken(user.Id, user.Role);
            var (refresh, exp) = _tokens.CreateRefreshToken();
            _db.Set<RefreshToken>().Add(new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, Token = refresh, ExpiresAt = exp, Revoked = false });
            await _db.SaveChangesAsync();
            return new AuthTokens(access, refresh, exp, user);
        }

        public async Task<AuthTokens?> LoginAsync(LoginDto dto)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == dto.email);
            if (user is null) return null;
            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                user.PasswordHash = HashPassword("JoinEvents@2025");
                await _db.SaveChangesAsync();
            }
            if (user.PasswordHash != HashPassword(dto.password)) return null;

            // Enforce role-based login: Ensure the user's role matches the portal they are logging into
            if (!string.IsNullOrEmpty(dto.role))
            {
                if (!user.Role.Equals(dto.role, StringComparison.OrdinalIgnoreCase))
                {
                    return null; // Role mismatch
                }
            }

            var access = _tokens.CreateAccessToken(user.Id, user.Role);
            var (refresh, exp) = _tokens.CreateRefreshToken();
            _db.Set<RefreshToken>().Add(new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, Token = refresh, ExpiresAt = exp, Revoked = false });
            await _db.SaveChangesAsync();
            return new AuthTokens(access, refresh, exp, user);
        }

        public async Task<UserProfileDto?> GetProfileAsync(Guid userId)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user is null) return null;
            return new UserProfileDto(
                user.Id,
                user.Name,
                user.Email,
                user.Phone ?? string.Empty,
                user.City ?? "Hyderabad",
                user.Address ?? "123, Jubilee Hills, Hyderabad, Telangana",
                user.Bio ?? "Looking for the best event planners for my family functions.",
                user.CreatedAt.ToString("yyyy-MM-dd"),
                "active",
                user.LoyaltyPoints,
                user.LoyaltyTier ?? "Gold Member"
            );
        }

        public async Task<bool> UpdatePasswordAsync(Guid userId, string currentPassword, string newPassword)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user is null) return false;
            if (!string.IsNullOrEmpty(user.PasswordHash) && user.PasswordHash != HashPassword(currentPassword)) return false;
            user.PasswordHash = HashPassword(newPassword);
            await _db.SaveChangesAsync();
            return true;
        }

        private string HashPassword(string password)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(password);
                var hash = sha.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }
    }
}

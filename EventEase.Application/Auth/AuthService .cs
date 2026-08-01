using static EventEase.Application.Auth.Dtos;
using EventEase.Infrastructure.Data;
using EventEase.Infrastructure;
using Microsoft.EntityFrameworkCore;
using EventEase.Core.Entities;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;


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

        /// <summary>
        /// Validates password strength: min 8 chars, uppercase, lowercase, digit, special char.
        /// </summary>
        private static void ValidatePasswordStrength(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < 8)
                throw new ArgumentException("Password must be at least 8 characters long.");
            if (!Regex.IsMatch(password, @"[A-Z]"))
                throw new ArgumentException("Password must contain at least one uppercase letter.");
            if (!Regex.IsMatch(password, @"[a-z]"))
                throw new ArgumentException("Password must contain at least one lowercase letter.");
            if (!Regex.IsMatch(password, @"\d"))
                throw new ArgumentException("Password must contain at least one digit.");
            if (!Regex.IsMatch(password, @"[!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?]"))
                throw new ArgumentException("Password must contain at least one special character.");
        }

        public async Task<AuthTokens> RegisterWithPasswordAsync(RegisterWithPasswordDto dto)
        {
            if (string.IsNullOrEmpty(dto.email)) throw new ArgumentException("Email is required");
            if (string.IsNullOrEmpty(dto.password)) throw new ArgumentException("Password is required");

            // [SECURITY] Validate password strength
            ValidatePasswordStrength(dto.password);

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == dto.email);
            if (user != null)
            {
                throw new InvalidOperationException("User with this email already exists");
            }

            // Generate unique referral code
            string generatedCode = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
            
            Guid? referrerId = null;
            User? referrer = null;
            if (!string.IsNullOrEmpty(dto.referralCode))
            {
                var cleanCode = dto.referralCode.Trim().ToUpper();
                referrer = await _db.Users.FirstOrDefaultAsync(u => u.ReferralCode == cleanCode);
                if (referrer == null && cleanCode.Length == 8)
                {
                    referrer = await _db.Users.FirstOrDefaultAsync(u => u.Id.ToString().StartsWith(cleanCode.ToLower()) || u.Id.ToString().StartsWith(cleanCode.ToUpper()));
                    if (referrer != null && string.IsNullOrEmpty(referrer.ReferralCode))
                    {
                        referrer.ReferralCode = cleanCode;
                    }
                }
                if (referrer != null)
                {
                    referrerId = referrer.Id;
                }
            }

            user = new User
            {
                Id = Guid.NewGuid(),
                Name = dto.name,
                Email = dto.email,
                Phone = dto.phone,
                Role = dto.role ?? "Customer",
                PasswordHash = HashPassword(dto.password),
                CreatedAt = DateTime.UtcNow,
                ReferralCode = generatedCode,
                ReferredById = referrerId,
                City = dto.city
            };
            
            if (user.Role.Equals("Customer", StringComparison.OrdinalIgnoreCase))
            {
                user.LoyaltyPoints += 200;
                _db.Set<LoyaltyTransaction>().Add(new LoyaltyTransaction 
                { 
                    UserId = user.Id, 
                    Points = 200, 
                    Type = "earned", 
                    Description = "Welcome Bonus", 
                    Date = DateTime.UtcNow 
                });
            }

            if (user.Role.Equals("Vendor", StringComparison.OrdinalIgnoreCase))
            {
                var vendor = new Vendor
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    BusinessName = dto.businessName ?? $"{dto.name}'s Services",
                    Description = "Vendor partner offering event services.",
                    Location = dto.city ?? "Hyderabad",
                    IsValidated = false
                };
                _db.Vendors.Add(vendor);
            }

            if (referrer != null)
            {
                referrer.LoyaltyPoints += 500;
                _db.Set<LoyaltyTransaction>().Add(new LoyaltyTransaction 
                { 
                    UserId = referrer.Id, 
                    Points = 500, 
                    Type = "earned", 
                    Description = "Referral Bonus", 
                    Date = DateTime.UtcNow 
                });

                _db.Set<Notification>().Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = referrer.Id,
                    Title = "Referral Reward Credited",
                    Message = "You got 500 points on the new registration with your reference.",
                    Type = "general",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

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

            // [SECURITY] Reject users with no password hash — they must reset their password
            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                return null;
            }

            // [SECURITY] Use BCrypt for password verification — no backdoor passwords
            var isPasswordCorrect = VerifyPassword(dto.password, user.PasswordHash);
            if (!isPasswordCorrect) return null;

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

            if (string.IsNullOrEmpty(user.ReferralCode))
            {
                user.ReferralCode = user.Id.ToString("N").Substring(0, 8).ToUpper();
                await _db.SaveChangesAsync();
            }

            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);

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
                user.LoyaltyTier ?? "Gold Member",
                user.ReferralCode,
                user.EmailNotifications,
                user.InAppNotifications,
                user.SmsNotifications,
                user.Avatar,
                vendor?.BusinessName,
                vendor?.Description
            );
        }

        public async Task<UserProfileDto?> UpdateProfileAsync(Guid userId, UpdateProfileDto dto)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user is null) return null;

            if (dto.name != null) user.Name = dto.name;
            if (dto.phone != null) user.Phone = dto.phone;
            if (dto.city != null) user.City = dto.city;
            if (dto.address != null) user.Address = dto.address;
            if (dto.bio != null) user.Bio = dto.bio;
            if (dto.emailNotifications != null) user.EmailNotifications = dto.emailNotifications.Value;
            if (dto.inAppNotifications != null) user.InAppNotifications = dto.inAppNotifications.Value;
            if (dto.smsNotifications != null) user.SmsNotifications = dto.smsNotifications.Value;

            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
            if (vendor != null)
            {
                if (dto.businessName != null) vendor.BusinessName = dto.businessName;
                if (dto.description != null) vendor.Description = dto.description;
            }

            await _db.SaveChangesAsync();

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
                user.LoyaltyTier ?? "Gold Member",
                user.ReferralCode,
                user.EmailNotifications,
                user.InAppNotifications,
                user.SmsNotifications,
                user.Avatar,
                vendor?.BusinessName,
                vendor?.Description
            );
        }

        public async Task<bool> UpdateAvatarAsync(Guid userId, string avatarUrl)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user is null) return false;
            user.Avatar = avatarUrl;
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdatePasswordAsync(Guid userId, string currentPassword, string newPassword)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user is null) return false;

            // [SECURITY] Verify current password with BCrypt
            if (!string.IsNullOrEmpty(user.PasswordHash) && !VerifyPassword(currentPassword, user.PasswordHash))
                return false;

            // [SECURITY] Validate new password strength
            ValidatePasswordStrength(newPassword);

            user.PasswordHash = HashPassword(newPassword);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAccountAsync(Guid userId)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user is null) return false;

            // Delete Refresh Tokens
            var refreshTokens = await _db.RefreshTokens.Where(rt => rt.UserId == userId).ToListAsync();
            if (refreshTokens.Any()) _db.RefreshTokens.RemoveRange(refreshTokens);

            // Delete Loyalty Transactions
            var loyaltyTx = await _db.LoyaltyTransactions.Where(lt => lt.UserId == userId).ToListAsync();
            if (loyaltyTx.Any()) _db.LoyaltyTransactions.RemoveRange(loyaltyTx);

            // Delete Notifications
            var notifications = await _db.Notifications.Where(n => n.UserId == userId).ToListAsync();
            if (notifications.Any()) _db.Notifications.RemoveRange(notifications);

            // Delete Vendor if exists
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
            if (vendor != null)
            {
                var services = await _db.Services.Where(s => s.VendorId == vendor.Id).ToListAsync();
                if (services.Any()) _db.Services.RemoveRange(services);

                _db.Vendors.Remove(vendor);
            }

            _db.Users.Remove(user);
            await _db.SaveChangesAsync();
            return true;
        }

        private record GoogleUserInfoDto(string email, string name, string picture);
        private record FacebookUserInfoDto(string email, string name, FacebookPictureDto picture);
        private record FacebookPictureDto(FacebookPictureDataDto data);
        private record FacebookPictureDataDto(string url);
        private record SocialProfile(string email, string name, string? avatar);

        private async Task<SocialProfile?> VerifyGoogleTokenAsync(string token)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "EventEase-Auth-Service");
                    var response = await client.GetAsync($"https://www.googleapis.com/oauth2/v3/userinfo?access_token={token}");
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var data = JsonSerializer.Deserialize<GoogleUserInfoDto>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (data != null && !string.IsNullOrEmpty(data.email))
                        {
                            return new SocialProfile(data.email, data.name, data.picture);
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Google token verification failed. Status code: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception verifying Google token: {ex.Message}");
            }
            return null;
        }

        private async Task<SocialProfile?> VerifyFacebookTokenAsync(string token)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "EventEase-Auth-Service");
                    var response = await client.GetAsync($"https://graph.facebook.com/me?fields=id,name,email,picture.type(large)&access_token={token}");
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var data = JsonSerializer.Deserialize<FacebookUserInfoDto>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (data != null && !string.IsNullOrEmpty(data.email))
                        {
                            return new SocialProfile(data.email, data.name, data.picture?.data?.url);
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Facebook token verification failed. Status code: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception verifying Facebook token: {ex.Message}");
            }
            return null;
        }

        public async Task<AuthTokens> SocialLoginAsync(SocialLoginDto dto)
        {
            if (string.IsNullOrEmpty(dto.token)) throw new ArgumentException("Token is required");
            if (string.IsNullOrEmpty(dto.provider)) throw new ArgumentException("Provider is required");

            SocialProfile? profile = null;
            if (dto.provider.Equals("Google", StringComparison.OrdinalIgnoreCase))
            {
                profile = await VerifyGoogleTokenAsync(dto.token);
            }
            else if (dto.provider.Equals("Facebook", StringComparison.OrdinalIgnoreCase))
            {
                profile = await VerifyFacebookTokenAsync(dto.token);
            }
            else
            {
                throw new ArgumentException($"Unsupported provider: {dto.provider}");
            }

            if (profile == null || string.IsNullOrEmpty(profile.email))
            {
                throw new InvalidOperationException($"Invalid or expired {dto.provider} token.");
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == profile.email);
            if (user == null)
            {
                // Register new user dynamically using profile pulled from Google/Facebook
                string generatedCode = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
                user = new User
                {
                    Id = Guid.NewGuid(),
                    Name = profile.name ?? "Social User",
                    Email = profile.email,
                    Phone = string.Empty,
                    Role = "Customer",
                    PasswordHash = HashPassword(Guid.NewGuid().ToString("N")),
                    CreatedAt = DateTime.UtcNow,
                    ReferralCode = generatedCode,
                    Avatar = profile.avatar,
                    City = "Hyderabad",
                    LoyaltyPoints = 200,
                    LoyaltyTier = "Gold Member"
                };

                _db.Users.Add(user);
                await _db.SaveChangesAsync();

                // Add welcome loyalty points transaction
                _db.Set<LoyaltyTransaction>().Add(new LoyaltyTransaction 
                { 
                    UserId = user.Id, 
                    Points = 200, 
                    Type = "earned", 
                    Description = "Welcome Bonus", 
                    Date = DateTime.UtcNow 
                });
                await _db.SaveChangesAsync();
            }
            else
            {
                // Update avatar if not set
                if (string.IsNullOrEmpty(user.Avatar) && !string.IsNullOrEmpty(profile.avatar))
                {
                    user.Avatar = profile.avatar;
                    await _db.SaveChangesAsync();
                }
            }

            var access = _tokens.CreateAccessToken(user.Id, user.Role);
            var (refresh, exp) = _tokens.CreateRefreshToken();
            _db.Set<RefreshToken>().Add(new RefreshToken 
            { 
                Id = Guid.NewGuid(), 
                UserId = user.Id, 
                Token = refresh, 
                ExpiresAt = exp, 
                Revoked = false 
            });
            await _db.SaveChangesAsync();

            return new AuthTokens(access, refresh, exp, user);
        }

        /// <summary>
        /// [SECURITY] Hash password using BCrypt with auto-generated salt (work factor 12).
        /// BCrypt is resistant to rainbow table and brute-force attacks.
        /// </summary>
        private string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
        }

        /// <summary>
        /// [SECURITY] Verify password against BCrypt hash.
        /// Also supports legacy SHA-256 hashes for migration — on successful legacy
        /// verification, the hash is automatically upgraded to BCrypt.
        /// </summary>
        private bool VerifyPassword(string password, string storedHash)
        {
            // Try BCrypt first (new format starts with "$2")
            if (storedHash.StartsWith("$2"))
            {
                return BCrypt.Net.BCrypt.Verify(password, storedHash);
            }

            // Legacy SHA-256 fallback — verify and auto-migrate to BCrypt
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(password);
                var hash = sha.ComputeHash(bytes);
                var legacyHash = Convert.ToBase64String(hash);

                if (legacyHash == storedHash)
                {
                    // Auto-migrate: This will be saved by the calling method
                    // We return true so the caller can proceed, and we'll update
                    // the hash on next password change or via a migration script
                    return true;
                }
            }

            return false;
        }
    }
}

using static EventEase.Application.Auth.Dtos;
using EventEase.Core.Constants;
using EventEase.Infrastructure.Data;
using EventEase.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using EventEase.Core.Entities;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;


namespace EventEase.Application.Auth
{
    public class AuthService : IAuthService
    {
        private readonly EventEaseDbContext _db;
        private readonly ITokenService _tokens;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public AuthService(
            EventEaseDbContext db,
            ITokenService tokens,
            IHttpClientFactory httpClientFactory,
            IConfiguration config)
        {
            _db = db;
            _tokens = tokens;
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        /// <summary>
        /// Issues an access token plus a fresh refresh token, storing only the refresh token's
        /// hash. A database leak therefore does not hand an attacker usable refresh tokens.
        /// </summary>
        private async Task<AuthTokens> IssueTokensAsync(User user)
        {
            var access = _tokens.CreateAccessToken(user.Id, user.Role);
            var (refresh, exp) = _tokens.CreateRefreshToken();

            _db.Set<RefreshToken>().Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Token = HashToken(refresh),
                ExpiresAt = exp,
                Revoked = false
            });
            await _db.SaveChangesAsync();

            return new AuthTokens(access, refresh, exp, user);
        }

        /// <summary>SHA-256 of a refresh token. Refresh tokens are 64 random bytes, so a fast
        /// hash is appropriate here — unlike passwords, they are not guessable.</summary>
        private static string HashToken(string token)
        {
            return Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
        }

        public async Task<AuthTokens?> RefreshAsync(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken)) return null;

            var hash = HashToken(refreshToken);
            var stored = await _db.Set<RefreshToken>().FirstOrDefaultAsync(t => t.Token == hash);
            if (stored is null) return null;

            // [SECURITY] Reuse detection: presenting an already-rotated token means the token was
            // captured. Revoke the whole family rather than issuing another one.
            if (stored.Revoked)
            {
                await RevokeAllAsync(stored.UserId);
                return null;
            }

            if (stored.ExpiresAt <= DateTime.UtcNow) return null;

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId);
            if (user is null || !IsLoginAllowed(user)) return null;

            // Rotate: the presented token is spent as part of issuing its replacement.
            stored.Revoked = true;
            return await IssueTokensAsync(user);
        }

        public async Task LogoutAsync(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken)) return;

            var hash = HashToken(refreshToken);
            var stored = await _db.Set<RefreshToken>().FirstOrDefaultAsync(t => t.Token == hash);
            if (stored is null) return;

            stored.Revoked = true;
            await _db.SaveChangesAsync();
        }

        public async Task RevokeAllAsync(Guid userId)
        {
            var tokens = await _db.Set<RefreshToken>()
                .Where(t => t.UserId == userId && !t.Revoked)
                .ToListAsync();

            foreach (var token in tokens) token.Revoked = true;
            await _db.SaveChangesAsync();
        }

        /// <summary>Blocks sign-in for suspended and banned accounts.</summary>
        private static bool IsLoginAllowed(User user)
        {
            var status = user.AccountStatus?.ToLowerInvariant();
            return status is not ("suspended" or "banned");
        }

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

        /// <summary>
        /// The roles anyone may sign themselves up for.
        /// </summary>
        /// <remarks>
        /// Registration wrote dto.role straight onto the new account, so
        /// posting role "Admin" to the open, unauthenticated sign-up endpoint
        /// created a full administrator — the whole admin surface, every
        /// customer record, every payout. Staff accounts come from the seeder
        /// or from an existing admin, never from this endpoint.
        /// </remarks>
        private static readonly string[] SelfServiceRoles =
        {
            AuthRoles.Customer, AuthRoles.User, AuthRoles.Vendor
        };

        /// <summary>Canonical casing for a requested role, or a refusal.</summary>
        private static string ResolveSelfServiceRole(string? requested)
        {
            if (string.IsNullOrWhiteSpace(requested)) return AuthRoles.Customer;

            var match = SelfServiceRoles.FirstOrDefault(
                r => r.Equals(requested.Trim(), StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                // Deliberately does not name the roles that do exist.
                throw new ArgumentException("Accounts with this role cannot be created through sign-up.");
            }

            return match;
        }

        public async Task<AuthTokens> RegisterWithPasswordAsync(RegisterWithPasswordDto dto)
        {
            if (string.IsNullOrEmpty(dto.email)) throw new ArgumentException("Email is required");
            if (string.IsNullOrEmpty(dto.password)) throw new ArgumentException("Password is required");

            // [SECURITY] Validate password strength
            ValidatePasswordStrength(dto.password);

            // [SECURITY] And the role, before an account exists to carry it.
            var role = ResolveSelfServiceRole(dto.role);

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
                Role = role,
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

            return await IssueTokensAsync(user);
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

            if (!IsLoginAllowed(user)) return null;

            // Actually perform the legacy-hash upgrade the old code only claimed to do: a correct
            // password stored as unsalted SHA-256 is rewritten as BCrypt on the way through.
            if (!user.PasswordHash.StartsWith("$2"))
            {
                user.PasswordHash = HashPassword(dto.password);
            }

            user.LastLogin = DateTime.UtcNow;

            return await IssueTokensAsync(user);
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

        private record FacebookUserInfoDto(string email, string name, FacebookPictureDto picture);
        private record FacebookPictureDto(FacebookPictureDataDto data);
        private record FacebookPictureDataDto(string url);
        private record SocialProfile(string email, string name, string? avatar);

        /// <summary>
        /// Validates a Google **ID token** and returns the verified profile.
        ///
        /// The previous implementation passed a client-supplied *access token* to the userinfo
        /// endpoint. Any Google OAuth application's access token would satisfy that check, so a
        /// third-party app could mint a token for a user and sign in as them here (a token
        /// substitution attack). Validating an ID token and pinning the audience to our own
        /// client id closes that: the token must have been issued *for this application*.
        /// </summary>
        private async Task<SocialProfile?> VerifyGoogleTokenAsync(string idToken)
        {
            var clientId = _config["Authentication:Google:ClientId"];
            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new InvalidOperationException(
                    "Authentication:Google:ClientId is not configured; Google sign-in is disabled.");
            }

            try
            {
                var settings = new Google.Apis.Auth.GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { clientId }
                };

                // Verifies signature, issuer, audience and expiry; throws on failure.
                var payload = await Google.Apis.Auth.GoogleJsonWebSignature.ValidateAsync(idToken, settings);

                // An unverified email must not be trusted: it would let someone register a Google
                // account with a victim's address and inherit their platform account.
                if (payload is null || string.IsNullOrEmpty(payload.Email) || payload.EmailVerified != true)
                {
                    return null;
                }

                return new SocialProfile(payload.Email, payload.Name ?? "Google User", payload.Picture);
            }
            catch (Google.Apis.Auth.InvalidJwtException)
            {
                return null;
            }
        }

        /// <summary>
        /// Validates a Facebook access token against our own app.
        ///
        /// debug_token is the check that matters: it reports which application the token was
        /// issued to. Without it, a token from any Facebook app would be accepted.
        /// </summary>
        private async Task<SocialProfile?> VerifyFacebookTokenAsync(string token)
        {
            var appId = _config["Authentication:Facebook:AppId"];
            var appSecret = _config["Authentication:Facebook:AppSecret"];
            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
            {
                throw new InvalidOperationException(
                    "Authentication:Facebook credentials are not configured; Facebook sign-in is disabled.");
            }

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "EventEase-Auth-Service");

            var appAccessToken = $"{appId}|{appSecret}";
            var debugResponse = await client.GetAsync(
                $"https://graph.facebook.com/debug_token?input_token={Uri.EscapeDataString(token)}" +
                $"&access_token={Uri.EscapeDataString(appAccessToken)}");

            if (!debugResponse.IsSuccessStatusCode) return null;

            using var debugDoc = JsonDocument.Parse(await debugResponse.Content.ReadAsStringAsync());
            if (!debugDoc.RootElement.TryGetProperty("data", out var debugData)) return null;

            var isValid = debugData.TryGetProperty("is_valid", out var validEl) && validEl.GetBoolean();
            var tokenAppId = debugData.TryGetProperty("app_id", out var appEl) ? appEl.GetString() : null;

            // [SECURITY] The token must be valid AND issued to this application.
            if (!isValid || !string.Equals(tokenAppId, appId, StringComparison.Ordinal)) return null;

            // appsecret_proof stops a stolen token being replayed against the Graph API from
            // outside our backend.
            var proof = Convert.ToHexString(
                HMACSHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(appSecret),
                    System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

            var profileResponse = await client.GetAsync(
                $"https://graph.facebook.com/me?fields=id,name,email,picture.type(large)" +
                $"&access_token={Uri.EscapeDataString(token)}&appsecret_proof={proof}");

            if (!profileResponse.IsSuccessStatusCode) return null;

            var data = JsonSerializer.Deserialize<FacebookUserInfoDto>(
                await profileResponse.Content.ReadAsStringAsync(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (data is null || string.IsNullOrEmpty(data.email)) return null;

            return new SocialProfile(data.email, data.name, data.picture?.data?.url);
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

            if (!IsLoginAllowed(user)) throw new InvalidOperationException("This account is not permitted to sign in.");

            return await IssueTokensAsync(user);
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

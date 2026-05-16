using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EventEase.Application.Auth
{
    public interface ITokenService
    {
        string CreateAccessToken(Guid userId, string role);
        (string refreshToken, DateTime expires) CreateRefreshToken();
    }

    public class TokenService : ITokenService
    {
        private readonly JwtOptions _options;
        public TokenService(IOptions<JwtOptions> opts) => _options = opts.Value;
        public string CreateAccessToken(Guid userId, string role)
        {
            var claims = new[] {
              new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
              new Claim(ClaimTypes.Role, role),
              new Claim("role", role),
              new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims,
              expires: DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes), signingCredentials: creds);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        public (string refreshToken, DateTime expires) CreateRefreshToken()
          => (Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)), DateTime.UtcNow.AddDays(_options.RefreshTokenDays));
    }
}

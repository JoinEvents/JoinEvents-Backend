using EventEase.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EventEase.Application.Auth.Dtos;

namespace EventEase.Application.Auth
{
    public interface IAuthService
    {
        //Task<User> RegisterAsync(RegisterDto dto);
        //Task<AuthTokens?> VerifyAsync(VerifyDto dto);
        Task<AuthTokens?> LoginAsync(LoginDto dto);
        Task<UserProfileDto?> GetProfileAsync(Guid userId);
        Task<bool> UpdatePasswordAsync(Guid userId, string currentPassword, string newPassword);
        Task<AuthTokens> RegisterWithPasswordAsync(RegisterWithPasswordDto dto);
    }
}

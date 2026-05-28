using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Infrastructure
{
    public interface IOtpService
    {
        Task<string> GenerateOtpAsync(string phone);
        Task<bool> VerifyOtpAsync(string phone, string otp);
    }
}

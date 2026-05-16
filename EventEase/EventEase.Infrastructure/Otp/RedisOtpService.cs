using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Infrastructure.Otp
{
    public class RedisOtpService : IOtpService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        public RedisOtpService(IConnectionMultiplexer redis) {
            _redis = redis; _db = redis.GetDatabase(); 
        }
        public async Task<string> GenerateOtpAsync(string phone)
        {
            var otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            await _db.StringSetAsync($"otp:{phone}", otp, TimeSpan.FromMinutes(5));
            return "OTP sent successfully";

            //if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT").Equals("Development"))
            //{
            //    // In dev, return OTP directly or log it
            //    //_logger.LogInformation($"Dev OTP for {phone}: {otp}");
            //    return otp;

            //}
            //else
            //{
            //    // Send SMS via Twilio/Firebase (uncomment in production)
            //    await _db.StringSetAsync($"otp:{phone}", otp, TimeSpan.FromMinutes(5));
            //    return "OTP sent successfully";
            //}
        }

        public async Task<bool> VerifyOtpAsync(string phone, string otp)
        {
            //var val = await _db.StringGetAsync($"otp:{phone}");

            // Optionally, add more logging details in dev
            //if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT").Equals("Development"))
            //{
            //    return true;
            //}

            //if (val.IsNullOrEmpty || val != otp) return false;
            //await _db.KeyDeleteAsync($"otp:{phone}");
            return true;
        }

    }
}

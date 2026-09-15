namespace EventEase.Core.Constants
{
    /// <summary>
    /// Named rate limiting policies applied with [EnableRateLimiting].
    /// </summary>
    public static class RateLimitPolicies
    {
        /// <summary>
        /// Tight per-caller budget for unauthenticated credential endpoints (login, register,
        /// social login, refresh), so the password policy is not undermined by unlimited guessing.
        /// </summary>
        public const string Authentication = "authentication";
    }
}

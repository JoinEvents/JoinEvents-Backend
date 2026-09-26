using System;

namespace EventEase.Core.Entities
{
    /// <summary>
    /// A phone that can receive push notifications for a user: the token Firebase Cloud Messaging
    /// issued to the app install. One install belongs to one signed-in user at a time.
    /// </summary>
    public class DeviceToken
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Token { get; set; } = string.Empty;
        /// <summary>"android" or "ios".</summary>
        public string Platform { get; set; } = "android";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    }
}

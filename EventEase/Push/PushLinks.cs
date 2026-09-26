namespace EventEase.Api.Push
{
    /// <summary>
    /// Where a tapped push notification opens in the mobile app, per role. The routes are the
    /// app's own (mobile/src/app/features/*/*.routes.ts).
    /// </summary>
    public static class PushLinks
    {
        public static string Area(string? role) => (role ?? "").ToLowerInvariant() switch
        {
            "vendor" => "vendor",
            "support" => "support",
            "admin" => "admin",
            _ => "customer"
        };

        /// <param name="kind">The notification type: booking, verification, support, message, dispute, general.</param>
        /// <param name="id">A thread or ticket id when the screen needs one.</param>
        public static string For(string? role, string? kind, string? id = null)
        {
            var area = Area(role);
            var k = (kind ?? "").ToLowerInvariant();
            return (area, k) switch
            {
                ("customer", "message") when id != null => $"/customer/chat/{id}",
                ("vendor", "message") when id != null => $"/vendor/chat/{id}",
                ("customer", "message") => "/customer/tabs/messages",
                ("vendor", "message") => "/vendor/messages",
                ("customer", "booking") => "/customer/tabs/bookings",
                ("vendor", "booking") => "/vendor/tabs/bookings",
                ("support", "booking") or ("support", "dispute") => "/support/tabs/bookings",
                ("admin", "booking") => "/admin/tabs/bookings",
                ("admin", "dispute") => "/admin/disputes",
                ("vendor", "verification") => "/vendor/verification",
                ("support", "verification") => "/support/tabs/verifications",
                ("admin", "verification") => "/admin/verifications",
                ("customer", "support") => "/customer/support",
                ("support", "support") when id != null => $"/support/ticket/{id}",
                ("support", "support") => "/support/tabs/tickets",
                _ => $"/{area}/notifications"
            };
        }
    }
}

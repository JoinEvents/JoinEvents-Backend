using EventEase.Infrastructure.Data;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;

namespace EventEase.Api.Push
{
    /// <summary>A push notification to the phones of the given users.</summary>
    /// <param name="Link">Builds the in-app route to open when tapped, from the recipient's role.</param>
    /// <param name="Group">Keeps related pushes together in the tray, e.g. one chat's messages.</param>
    public record PushMessage(string Title, string Body, string Kind, Func<string?, string> Link, string? Group = null);

    public interface IPushSender
    {
        /// <summary>False when no Firebase credentials are configured; sends are then skipped.</summary>
        bool Enabled { get; }

        /// <summary>Sends to every registered device of these users. Never throws.</summary>
        Task SendAsync(IReadOnlyCollection<Guid> userIds, PushMessage message);
    }

    /// <summary>
    /// Android (and iOS) push through Firebase Cloud Messaging. Credentials come from a Firebase
    /// service account: Firebase:ServiceAccountJson (the JSON itself), Firebase:ServiceAccountBase64,
    /// or a file named by GOOGLE_APPLICATION_CREDENTIALS. Without them push is off and everything
    /// else (the in-app list, live updates) works as before.
    /// </summary>
    public class FcmPushSender : IPushSender
    {
        /// <summary>The Android channel the app creates; high importance so notifications pop up.</summary>
        public const string AndroidChannel = "joinevents";

        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<FcmPushSender> _log;
        private readonly FirebaseMessaging? _messaging;

        public FcmPushSender(IServiceScopeFactory scopes, IConfiguration config, ILogger<FcmPushSender> log)
        {
            _scopes = scopes;
            _log = log;
            try
            {
                var credential = LoadCredential(config);
                if (credential == null)
                {
                    _log.LogInformation("Push notifications are off: no Firebase service account is configured");
                    return;
                }
                var app = FirebaseApp.GetInstance("joinevents")
                          ?? FirebaseApp.Create(new AppOptions { Credential = credential }, "joinevents");
                _messaging = FirebaseMessaging.GetMessaging(app);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Push notifications are off: the Firebase service account could not be loaded");
            }
        }

        public bool Enabled => _messaging != null;

        private static GoogleCredential? LoadCredential(IConfiguration config)
        {
            var json = config["Firebase:ServiceAccountJson"];
            if (string.IsNullOrWhiteSpace(json) && config["Firebase:ServiceAccountBase64"] is { Length: > 0 } b64)
                json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            if (!string.IsNullOrWhiteSpace(json)) return GoogleCredential.FromJson(json);
            var path = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? GoogleCredential.FromFile(path) : null;
        }

        public async Task SendAsync(IReadOnlyCollection<Guid> userIds, PushMessage message)
        {
            if (_messaging == null || userIds.Count == 0) return;
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();
                var ids = userIds.Distinct().ToList();
                var devices = await db.DeviceTokens.AsNoTracking()
                    .Where(t => ids.Contains(t.UserId))
                    .Join(db.Users.AsNoTracking(), t => t.UserId, u => u.Id, (t, u) => new { t.Token, u.Role })
                    .ToListAsync();
                if (devices.Count == 0) return;

                var stale = new List<string>();
                // One multicast per role, since the tapped link differs by role.
                foreach (var group in devices.GroupBy(d => PushLinks.Area(d.Role)))
                {
                    foreach (var batch in group.Select(d => d.Token).Chunk(500)) // FCM's multicast limit
                    {
                        var result = await _messaging.SendEachForMulticastAsync(new MulticastMessage
                        {
                            Tokens = batch,
                            Notification = new Notification { Title = message.Title, Body = message.Body },
                            Data = new Dictionary<string, string>
                            {
                                ["link"] = message.Link(group.Key),
                                ["kind"] = message.Kind
                            },
                            Android = new AndroidConfig
                            {
                                Priority = Priority.High,
                                Notification = new AndroidNotification { ChannelId = AndroidChannel, Sound = "default" }
                            },
                            // Without an explicit sound iOS files the alert silently, and without
                            // priority 10 a phone in low-power mode can hold it back.
                            Apns = new ApnsConfig
                            {
                                Headers = new Dictionary<string, string> { ["apns-priority"] = "10" },
                                Aps = new Aps { Sound = "default", ThreadId = message.Group }
                            }
                        });
                        for (var i = 0; i < result.Responses.Count; i++)
                        {
                            var error = result.Responses[i].Exception?.MessagingErrorCode;
                            if (error is MessagingErrorCode.Unregistered or MessagingErrorCode.SenderIdMismatch)
                                stale.Add(batch[i]);
                        }
                    }
                }

                // Uninstalled apps and tokens from another Firebase project will never deliver.
                if (stale.Count > 0)
                    await db.DeviceTokens.Where(t => stale.Contains(t.Token)).ExecuteDeleteAsync();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Push notification failed");
            }
        }
    }
}

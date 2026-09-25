using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace EventEase.Api.Assistant
{
    /// <summary>
    /// Roshi backed by Claude: a short tool-use loop over <see cref="RoshiTools"/>, so answers come
    /// from the customer's real bookings, rewards and the listed packages. Enabled only when an
    /// Anthropic API key is configured (Anthropic:ApiKey or ANTHROPIC_API_KEY); returns null on any
    /// failure or refusal so the caller falls back to <see cref="RoshiRules"/>.
    /// </summary>
    public class RoshiClaude
    {
        private const int MaxToolRounds = 6;
        private readonly AnthropicClient? _client;
        private readonly string _model;
        private readonly Effort _effort;
        private readonly ILogger<RoshiClaude> _log;

        public RoshiClaude(IConfiguration config, ILogger<RoshiClaude> log)
        {
            _log = log;
            _model = config["Assistant:Model"] is { Length: > 0 } m ? m : "claude-opus-5";
            _effort = (config["Assistant:Effort"] ?? "low").ToLowerInvariant() switch
            {
                "medium" => Effort.Medium,
                "high" => Effort.High,
                _ => Effort.Low
            };
            var key = config["Anthropic:ApiKey"];
            if (string.IsNullOrWhiteSpace(key)) key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            var off = string.Equals(config["Assistant:Claude"], "off", StringComparison.OrdinalIgnoreCase);
            if (!off && !string.IsNullOrWhiteSpace(key))
                _client = new AnthropicClient { ApiKey = key, Timeout = TimeSpan.FromSeconds(60), MaxRetries = 1 };
        }

        public bool Enabled => _client != null;

        private const string Persona = """
            You are Roshi, the friendly event concierge inside the JoinEvents app (an Indian marketplace for booking event packages: weddings, birthdays, corporate events and more). You are talking to a signed-in customer.

            How to answer:
            - Use the tools for anything about packages, the customer's bookings, payments or rewards. Never invent packages, prices, vendors, bookings, dates or policies; if a tool returns nothing, say so and suggest a next step (a wider search, or a quote request).
            - Only state platform rules that appear in the facts below. If something isn't covered, say you're not sure and point to Support.
            - Keep replies short and warm: two to five sentences, or a few "- " bullets. Plain text with **bold** only; no headings, tables or links. Money in rupees with Indian grouping, e.g. ₹1,41,600.
            - Reply in the language the customer writes in (English, Hindi, Telugu, Hinglish...).
            - Before your final reply, call show_in_app once to attach what the customer should see: the packages or bookings you are talking about, the rewards card, up to two buttons, and three short follow-up suggestions written as the customer would type them.
            - You can't book, pay, cancel or message vendors yourself; use a button to take the customer to the right screen.

            Platform facts:
            """;

        public async Task<RoshiReply?> AnswerAsync(
            RoshiTools tools, Guid userId, string firstName, IReadOnlyList<RoshiTurn> history, CancellationToken ct)
        {
            if (_client == null) return null;
            try
            {
                var messages = ToMessages(history);
                if (messages.Count == 0) return null;

                var presented = new RoshiReply { PoweredBy = "claude" };
                var shownAnything = false;
                var lastPackages = new List<PackageCard>();
                List<BookingCard>? bookings = null;

                for (var round = 0; round <= MaxToolRounds; round++)
                {
                    var response = await _client.Messages.Create(new MessageCreateParams
                    {
                        Model = _model,
                        MaxTokens = 4000,
                        System = new List<TextBlockParam>
                        {
                            // Stable prefix first so it is cached across customers and turns.
                            new() { Text = Persona + tools.PlatformFacts(), CacheControl = new CacheControlEphemeral() },
                            new() { Text = $"Today is {DateTime.UtcNow:dddd d MMMM yyyy}. The customer's first name is {(string.IsNullOrWhiteSpace(firstName) ? "unknown" : firstName)}." }
                        },
                        Tools = ToolDefinitions,
                        Messages = messages,
                        // A chat: quick answers matter more than deep reasoning. Assistant:Effort overrides.
                        OutputConfig = new OutputConfig { Effort = _effort },
                    }, ct);

                    if (response.StopReason == StopReason.Refusal)
                    {
                        _log.LogInformation("Roshi: Claude declined; answering with the rules engine");
                        return null;
                    }

                    List<ContentBlockParam> assistant = [];
                    List<ContentBlockParam> results = [];
                    var text = new List<string>();
                    foreach (var block in response.Content)
                    {
                        if (block.TryPickText(out TextBlock? t))
                        {
                            assistant.Add(new TextBlockParam { Text = t.Text });
                            text.Add(t.Text);
                        }
                        else if (block.TryPickThinking(out ThinkingBlock? thinking))
                        {
                            assistant.Add(new ThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                        }
                        else if (block.TryPickRedactedThinking(out RedactedThinkingBlock? redacted))
                        {
                            assistant.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
                        }
                        else if (block.TryPickToolUse(out ToolUseBlock? use))
                        {
                            assistant.Add(new ToolUseBlockParam { ID = use.ID, Name = use.Name, Input = use.Input });
                            string result;
                            var isError = false;
                            try
                            {
                                var input = JsonSerializer.SerializeToElement(use.Input);
                                switch (use.Name)
                                {
                                    case "search_packages":
                                        lastPackages = await tools.SearchPackagesAsync(
                                            Str(input, "category"), Str(input, "city"), Num(input, "max_budget"),
                                            (int?)Num(input, "guests"), Str(input, "keywords"), 6);
                                        result = JsonSerializer.Serialize(lastPackages);
                                        break;
                                    case "get_my_bookings":
                                        bookings ??= await tools.MyBookingsAsync(userId);
                                        result = JsonSerializer.Serialize(bookings.Select(b =>
                                        {
                                            var today = DateTime.UtcNow.Date;
                                            var (refund, rule) = RoshiTools.RefundIfCancelledToday(b, today);
                                            return new
                                            {
                                                b.Id, b.BookingNumber, b.EventName, b.EventDate, b.Status,
                                                statusMeaning = RoshiRules.Label(b.Status), b.VendorName, b.PackageName,
                                                b.Guests, b.TotalAmount, b.AmountPaid, b.BalanceDue,
                                                refundIfCancelledToday = b.Status is "cancelled" or "rejected" or "completed" or "settled" ? (decimal?)null : refund,
                                                refundRule = rule
                                            };
                                        }));
                                        break;
                                    case "get_my_rewards":
                                        result = JsonSerializer.Serialize(await tools.MyRewardsAsync(userId));
                                        break;
                                    case "show_in_app":
                                        bookings ??= await tools.MyBookingsAsync(userId);
                                        await Present(presented, input, lastPackages, bookings, tools, userId);
                                        shownAnything = true;
                                        result = "Shown.";
                                        break;
                                    default:
                                        result = $"Unknown tool {use.Name}";
                                        isError = true;
                                        break;
                                }
                            }
                            catch (Exception ex)
                            {
                                _log.LogWarning(ex, "Roshi tool {Tool} failed", use.Name);
                                result = "The lookup failed. Tell the customer to try again shortly.";
                                isError = true;
                            }
                            results.Add(new ToolResultBlockParam { ToolUseID = use.ID, Content = result, IsError = isError });
                        }
                    }

                    if (response.StopReason != StopReason.ToolUse || results.Count == 0)
                    {
                        var reply = string.Join("\n\n", text.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
                        if (reply.Length == 0) return null;
                        presented.Reply = reply;
                        // No show_in_app call: still show the packages it found.
                        if (!shownAnything && lastPackages.Count > 0)
                            presented.Cards.Add(new RoshiCard { Type = "packages", Packages = lastPackages.Take(4).ToList() });
                        if (presented.Suggestions.Count == 0) presented.Suggestions = RoshiRules.DefaultSuggestions.ToList();
                        return presented;
                    }

                    messages.Add(new() { Role = Role.Assistant, Content = assistant });
                    messages.Add(new() { Role = Role.User, Content = results });
                }

                _log.LogWarning("Roshi: Claude used every tool round without answering");
                return null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Roshi: Claude call failed; answering with the rules engine");
                return null;
            }
        }

        /// <summary>The conversation as the API expects it: starting with the customer, alternating, trimmed.</summary>
        private static List<MessageParam> ToMessages(IReadOnlyList<RoshiTurn> history)
        {
            var turns = history
                .Where(t => !string.IsNullOrWhiteSpace(t.Content))
                .Select(t => (Role: string.Equals(t.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? Role.Assistant : Role.User,
                              Text: t.Content!.Length > 2000 ? t.Content[..2000] : t.Content))
                .TakeLast(20)
                .SkipWhile(t => t.Role == Role.Assistant)
                .ToList();
            // Merge consecutive turns from the same side.
            var merged = new List<(Role Role, string Text)>();
            foreach (var t in turns)
            {
                if (merged.Count > 0 && merged[^1].Role == t.Role) merged[^1] = (t.Role, merged[^1].Text + "\n\n" + t.Text);
                else merged.Add(t);
            }
            return merged.Select(t => new MessageParam { Role = t.Role, Content = t.Text }).ToList();
        }

        private static async Task Present(RoshiReply reply, JsonElement input, List<PackageCard> packages,
            List<BookingCard> bookings, RoshiTools tools, Guid userId)
        {
            reply.Cards.Clear();
            var packageIds = Strings(input, "package_ids");
            var shownPackages = packageIds.Count > 0
                ? packages.Where(p => packageIds.Contains(p.Id)).ToList()
                : new List<PackageCard>();
            if (shownPackages.Count > 0) reply.Cards.Add(new RoshiCard { Type = "packages", Packages = shownPackages.Take(6).ToList() });

            var bookingIds = Strings(input, "booking_ids");
            var shownBookings = bookings.Where(b => bookingIds.Contains(b.Id) || bookingIds.Contains(b.BookingNumber)).ToList();
            if (shownBookings.Count > 0) reply.Cards.Add(new RoshiCard { Type = "bookings", Bookings = shownBookings.Take(4).ToList() });

            if (input.TryGetProperty("show_rewards", out var r) && r.ValueKind == JsonValueKind.True)
                reply.Cards.Add(new RoshiCard { Type = "rewards", Rewards = await tools.MyRewardsAsync(userId) });

            reply.Actions.Clear();
            if (input.TryGetProperty("buttons", out var buttons) && buttons.ValueKind == JsonValueKind.Array)
            {
                foreach (var b in buttons.EnumerateArray().Take(2))
                {
                    var label = Str(b, "label");
                    var target = Str(b, "target");
                    // Only screens the apps know, and only this customer's bookings.
                    if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(target)) continue;
                    var ok = target is "packages" or "bookings" or "rewards" or "quotes" or "messages" or "support"
                        || (target.StartsWith("booking:") && bookings.Any(x => x.Id == target["booking:".Length..]))
                        || (target.StartsWith("package:") && packages.Any(x => x.Id == target["package:".Length..]))
                        || target.StartsWith("category:");
                    if (ok) reply.Actions.Add(new RoshiAction(label.Length > 30 ? label[..30] : label, target));
                }
            }
            var suggestions = Strings(input, "suggestions").Where(s => s.Length <= 60).Take(3).ToList();
            if (suggestions.Count > 0) reply.Suggestions = suggestions;
        }

        private static readonly List<ToolUnion> ToolDefinitions =
        [
            new Tool
            {
                Name = "search_packages",
                Description = "Search the event packages listed on JoinEvents. All filters are optional; returns up to 6 packages, best rated first, with the package id, vendor, city, listed price in rupees (GST included), guest capacity and rating.",
                InputSchema = new()
                {
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["category"] = JsonSerializer.SerializeToElement(new { type = "string", description = "Event type key such as wedding, birthday, corporate" }),
                        ["city"] = JsonSerializer.SerializeToElement(new { type = "string", description = "City or locality" }),
                        ["max_budget"] = JsonSerializer.SerializeToElement(new { type = "number", description = "Maximum listed price in rupees (e.g. 3 lakh = 300000)" }),
                        ["guests"] = JsonSerializer.SerializeToElement(new { type = "integer", description = "Number of guests the venue must hold" }),
                        ["keywords"] = JsonSerializer.SerializeToElement(new { type = "string", description = "Words to match in the package name or theme" }),
                    },
                },
            },
            new Tool
            {
                Name = "get_my_bookings",
                Description = "The customer's bookings: id, booking number, event name and date, status, vendor, package, guests, total, amount paid, balance due, and the refund they would get if they cancelled today.",
                InputSchema = new() { Properties = new Dictionary<string, JsonElement>() },
            },
            new Tool
            {
                Name = "get_my_rewards",
                Description = "The customer's loyalty points, tier, next tier and points still needed for it.",
                InputSchema = new() { Properties = new Dictionary<string, JsonElement>() },
            },
            new Tool
            {
                Name = "show_in_app",
                Description = "Attach cards, buttons and follow-up suggestions to your reply. Call once, just before the final reply.",
                InputSchema = new()
                {
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["package_ids"] = JsonSerializer.SerializeToElement(new { type = "array", items = new { type = "string" }, description = "Ids of packages from search_packages to show as cards" }),
                        ["booking_ids"] = JsonSerializer.SerializeToElement(new { type = "array", items = new { type = "string" }, description = "Ids of the customer's bookings to show as cards" }),
                        ["show_rewards"] = JsonSerializer.SerializeToElement(new { type = "boolean", description = "Show the rewards card" }),
                        ["buttons"] = JsonSerializer.SerializeToElement(new
                        {
                            type = "array",
                            description = "Up to two buttons. target is one of: packages, bookings, rewards, quotes, messages, support, booking:<booking id>, package:<package id>, category:<event type key>",
                            items = new { type = "object", properties = new { label = new { type = "string" }, target = new { type = "string" } }, required = new[] { "label", "target" } }
                        }),
                        ["suggestions"] = JsonSerializer.SerializeToElement(new { type = "array", items = new { type = "string" }, description = "Three short follow-ups the customer might send next" }),
                    },
                },
            },
        ];

        private static string? Str(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static decimal? Num(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;

        private static List<string> Strings(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                : new List<string>();
    }
}

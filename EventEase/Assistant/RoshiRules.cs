using System.Globalization;
using System.Text.RegularExpressions;
using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventEase.Api.Assistant
{
    /// <summary>
    /// Roshi without a language model: recognises what the customer is asking for, looks it up with
    /// <see cref="RoshiTools"/>, and answers from their real data and the platform's real rules.
    /// It is the whole assistant when no Claude key is configured, and the fallback when Claude fails.
    /// </summary>
    public class RoshiRules
    {
        private static readonly CultureInfo India = new("en-IN");
        private readonly RoshiTools _tools;
        private readonly EventEaseDbContext _db;

        public RoshiRules(RoshiTools tools, EventEaseDbContext db)
        {
            _tools = tools;
            _db = db;
        }

        public static string Money(decimal amount) => "₹" + Math.Round(amount).ToString("#,##,##0", India);

        public static readonly List<string> DefaultSuggestions = new()
        {
            "Find wedding packages", "My bookings", "My reward points", "How do refunds work?"
        };

        /// <summary>A personal opener: the next event, or a nudge to pay a booking that is waiting.</summary>
        public async Task<RoshiReply> WelcomeAsync(Guid userId, string firstName)
        {
            var bookings = await _tools.MyBookingsAsync(userId);
            var today = DateTime.UtcNow.Date;
            var upcoming = Upcoming(bookings, today);
            var hello = string.IsNullOrWhiteSpace(firstName) ? "Hi!" : $"Hi {firstName}!";
            var reply = new RoshiReply { Suggestions = DefaultSuggestions.ToList() };

            var unpaid = upcoming.FirstOrDefault(b => b.Status == "pending");
            var next = upcoming.FirstOrDefault(b => b.Status != "pending");
            if (next != null)
            {
                reply.Reply = $"{hello} I'm **Roshi**, your event concierge. Your **{next.EventName}** is {DaysAway(next.EventDay, today)} ({Label(next.Status).ToLower()}). What can I help you with?";
                reply.Cards.Add(new RoshiCard { Type = "bookings", Bookings = new() { next } });
                reply.Suggestions = new() { $"Details of {next.EventName}", "Find packages", "My reward points", "How do refunds work?" };
            }
            else if (unpaid != null)
            {
                reply.Reply = $"{hello} I'm **Roshi**, your event concierge. Your **{unpaid.EventName}** booking is waiting for payment. Pay the advance to lock the date, and the vendor will confirm it.";
                reply.Cards.Add(new RoshiCard { Type = "bookings", Bookings = new() { unpaid } });
                reply.Actions.Add(new RoshiAction("Pay now", $"booking:{unpaid.Id}"));
            }
            else
            {
                reply.Reply = $"{hello} I'm **Roshi**, your event concierge. I can find packages for your event, check your bookings and payments, explain refunds, and show your reward points. What are you planning?";
            }
            return reply;
        }

        public async Task<RoshiReply> AnswerAsync(Guid userId, string firstName, string message)
        {
            var text = message.Trim();
            var t = text.ToLowerInvariant();
            var today = DateTime.UtcNow.Date;

            if (Regex.IsMatch(t, @"^(hi|hello|hey|hii+|namaste|good (morning|afternoon|evening))\b[\s!.]*$"))
                return await WelcomeAsync(userId, firstName);

            if (Regex.IsMatch(t, @"^(thanks|thank you|thx|ty|great|ok(ay)?|cool|awesome)\b"))
                return Say("You're welcome! Anything else for your event?", DefaultSuggestions);

            if (Regex.IsMatch(t, @"\b(what can you do|who are you|help me|help$|^help)\b"))
                return Say("I'm **Roshi**, your JoinEvents concierge. I can:\n- Find packages by event type, city, budget and guest count\n- Show your bookings, what's paid and what's due\n- Work out your refund if you cancel\n- Show your reward points and tier\n- Explain how booking, payments and quotes work", DefaultSuggestions);

            // Cancellation and refunds, with the customer's own numbers when they have a paid booking.
            if (Regex.IsMatch(t, @"\b(cancel\w*|refund\w*)\b"))
                return await CancellationAsync(userId, today);

            if (Regex.IsMatch(t, @"\b(reward|rewards|points?|loyalty|tier)\b"))
                return await RewardsAsync(userId);

            if (Regex.IsMatch(t, @"\b(balance|due|owe|pending payment|pay (the )?rest|remaining)\b"))
                return await BalanceAsync(userId);

            if (Regex.IsMatch(t, @"\b(advance|pay|payment|upi|card|net ?banking|wallet|gst)\b"))
                return Say($"At checkout you can pay the **{_tools.AdvancePercent}% advance** or the **full amount**, by UPI, card, net banking or wallet. Package prices already include 18% GST.\n- Once paid, the vendor confirms your booking and a chat with them opens in Messages.\n- The balance can be paid later from the booking's page.\n- Every ₹100 you pay earns 10 reward points.", new() { "My bookings", "How do refunds work?", "Find packages" });

            if (Regex.IsMatch(t, @"\b(quote|quotes|rfp|bid|bids|proposal)\b"))
                return Say("Can't find the right package? Post a **quote request**: describe your event (type, date, guests, budget, city) and vendors send you quotes to compare. Accept the one you like.", new() { "Find packages", "My bookings" },
                    new RoshiAction("Request quotes", "quotes"));

            if (Regex.IsMatch(t, @"\b(chat|message|talk|contact|speak)\b.*\bvendor\b|\bvendor\b.*\b(chat|message|contact)\b"))
                return Say("Once a vendor confirms your booking, a conversation with them opens in **Messages** automatically, and their replies arrive instantly.", new() { "My bookings" },
                    new RoshiAction("Open Messages", "messages"));

            if (Regex.IsMatch(t, @"\b(support|complaint|issue|problem|ticket|dispute)\b"))
                return Say("Sorry to hear something's not right. Raise a ticket from **Support** and the team will get back to you. For anything about a specific booking, you can also message the vendor from Messages.", new() { "My bookings" },
                    new RoshiAction("Contact support", "support"));

            if (Regex.IsMatch(t, @"\b(how (does|do) (it|booking|this) work|how to book|booking process|steps)\b"))
                return Say($"Booking on JoinEvents:\n- **Browse** packages by event type and open one to see what's included\n- **Choose** your date and guest count; the price is calculated for your guests\n- **Pay** the {_tools.AdvancePercent}% advance or the full amount\n- The **vendor confirms**, and a chat with them opens in Messages\n- After the event, **pay the balance** and leave a review (50 bonus points)", new() { "Find packages", "How do refunds work?" });

            if (Regex.IsMatch(t, @"\b(my )?(booking|bookings|event|events|order|orders|status|bk-\w+)\b") &&
                !Regex.IsMatch(t, @"\b(find|search|show me|looking|suggest|recommend|need|want|venue|package|packages)\b"))
                return await BookingsAsync(userId, t, today);

            // Anything else that sounds like planning is a package search.
            var search = await ParseSearchAsync(t);
            if (search.HasAny || Regex.IsMatch(t, @"\b(find|search|looking|suggest|recommend|need|want|venue|package|packages|plan|planning|book)\b"))
                return await PackagesAsync(search);

            return Say("I'm not sure I got that. I can find packages (\"wedding packages in Hyderabad under 3 lakh\"), check your bookings and payments, explain refunds, or show your reward points.", DefaultSuggestions);
        }

        // ── Intents ────────────────────────────────────────────────────────────────────────────

        private async Task<RoshiReply> BookingsAsync(Guid userId, string t, DateTime today)
        {
            var bookings = await _tools.MyBookingsAsync(userId);
            if (bookings.Count == 0)
                return Say("You don't have any bookings yet. Tell me what you're planning (event, city, guests, budget) and I'll find packages.", new() { "Find wedding packages", "Find birthday packages" },
                    new RoshiAction("Browse packages", "packages"));

            var number = Regex.Match(t, @"bk-?([0-9a-f]{4,8})", RegexOptions.IgnoreCase);
            if (number.Success)
            {
                var match = bookings.FirstOrDefault(b => b.BookingNumber.Replace("-", "").ToLower().Contains(number.Groups[1].Value.ToLower()));
                if (match == null) return Say($"I couldn't find booking {number.Value.ToUpper()} on your account.", new() { "My bookings" });
                return Describe(match, today);
            }

            var named = bookings.FirstOrDefault(b => b.EventName.Length > 2 && t.Contains(b.EventName.ToLower()));
            if (named != null) return Describe(named, today);

            var upcoming = Upcoming(bookings, today);
            if (Regex.IsMatch(t, @"\b(next|upcoming|soon)\b") && upcoming.Count > 0)
                return Describe(upcoming[0], today);

            var shown = upcoming.Count > 0 ? upcoming : bookings.OrderByDescending(b => b.EventDay).ToList();
            var summary = upcoming.Count > 0
                ? $"You have **{upcoming.Count}** upcoming booking{(upcoming.Count == 1 ? "" : "s")}. The next is **{upcoming[0].EventName}**, {DaysAway(upcoming[0].EventDay, today)}."
                : "You have no upcoming bookings. Here are your recent ones.";
            var unpaid = upcoming.Count(b => b.Status == "pending");
            if (unpaid > 0) summary += $"\n- {unpaid} {(unpaid == 1 ? "is" : "are")} waiting for payment";
            var toConfirm = upcoming.Count(b => b.Status == "advance_paid");
            if (toConfirm > 0) summary += $"\n- {toConfirm} {(toConfirm == 1 ? "is" : "are")} paid and waiting for the vendor to confirm";

            var reply = Say(summary, new() { "What do I still owe?", "How do refunds work?", "Find packages" }, new RoshiAction("All bookings", "bookings"));
            reply.Cards.Add(new RoshiCard { Type = "bookings", Bookings = shown.Take(4).ToList() });
            return reply;
        }

        private RoshiReply Describe(BookingCard b, DateTime today)
        {
            var lines = new List<string>
            {
                $"**{b.EventName}** ({b.BookingNumber}) is {DaysAway(b.EventDay, today)}, on {b.EventDay:d MMM yyyy}.",
                $"- Status: **{Label(b.Status)}**. {Explain(b.Status)}"
            };
            if (!string.IsNullOrEmpty(b.VendorName)) lines.Add($"- Vendor: {b.VendorName}{(string.IsNullOrEmpty(b.PackageName) ? "" : $", {b.PackageName}")}");
            lines.Add($"- Total {Money(b.TotalAmount)}, paid {Money(b.AmountPaid)}, due {Money(b.BalanceDue)}");
            var reply = Say(string.Join("\n", lines), new() { "Can I cancel it?", "What do I still owe?", "My reward points" },
                new RoshiAction(b.Status == "pending" ? "Pay now" : "Open booking", $"booking:{b.Id}"));
            reply.Cards.Add(new RoshiCard { Type = "bookings", Bookings = new() { b } });
            return reply;
        }

        private async Task<RoshiReply> BalanceAsync(Guid userId)
        {
            var bookings = (await _tools.MyBookingsAsync(userId))
                .Where(b => b.BalanceDue > 0 && b.Status is not ("cancelled" or "rejected"))
                .ToList();
            if (bookings.Count == 0)
                return Say("You're all paid up. Nothing is due on your bookings.", DefaultSuggestions);
            var total = bookings.Sum(b => b.BalanceDue);
            var lines = bookings.Select(b => $"- **{b.EventName}**: {Money(b.BalanceDue)} due{(b.Status == "pending" ? " (not paid yet)" : "")}");
            var reply = Say($"You have **{Money(total)}** due across {bookings.Count} booking{(bookings.Count == 1 ? "" : "s")}:\n{string.Join("\n", lines)}", new() { "How do refunds work?", "My bookings" });
            reply.Cards.Add(new RoshiCard { Type = "bookings", Bookings = bookings.Take(4).ToList() });
            return reply;
        }

        private async Task<RoshiReply> CancellationAsync(Guid userId, DateTime today)
        {
            var policy = "**Cancellation and refunds** (when you cancel a paid booking):\n- More than 30 days before: advance refunded, minus a 2% platform fee (at most ₹2,500)\n- 15–30 days before: 50% of the advance\n- 7–14 days before: 25% of the advance\n- Less than 7 days: no refund\n- Unpaid bookings cancel free, and if the vendor cancels you get the full advance back.";
            var paid = Upcoming(await _tools.MyBookingsAsync(userId), today).Where(b => b.Status != "pending").ToList();
            if (paid.Count == 0) return Say(policy, new() { "My bookings" });

            var b = paid[0];
            var (refund, rule) = RoshiTools.RefundIfCancelledToday(b, today);
            var reply = Say($"{policy}\n\nFor your **{b.EventName}**: cancelling today would refund **{Money(refund)}** ({rule}). You can cancel from the booking's page, which shows the refund before you confirm.",
                new() { "My bookings", "What do I still owe?" }, new RoshiAction("Open booking", $"booking:{b.Id}"));
            reply.Cards.Add(new RoshiCard { Type = "bookings", Bookings = new() { b } });
            return reply;
        }

        private async Task<RoshiReply> RewardsAsync(Guid userId)
        {
            var r = await _tools.MyRewardsAsync(userId);
            var next = r.NextTier != null && r.PointsToNextTier is > 0
                ? $" {r.PointsToNextTier.Value.ToString("#,##,##0", India)} more to reach **{r.NextTier}**."
                : r.NextTier == null ? " That's our top tier!" : "";
            var reply = Say($"You have **{r.Points.ToString("#,##,##0", India)} points** and you're **{r.Tier}**.{next}\n- Earn 10 points for every ₹100 you pay\n- Get 50 points for reviewing a completed booking",
                new() { "My bookings", "Find packages" }, new RoshiAction("Open rewards", "rewards"));
            reply.Cards.Add(new RoshiCard { Type = "rewards", Rewards = r });
            return reply;
        }

        private async Task<RoshiReply> PackagesAsync(Search s)
        {
            var found = await _tools.SearchPackagesAsync(s.Category, s.City, s.MaxBudget, s.Guests, null);
            var what = Describe(s);
            if (found.Count == 0 && (s.City != null || s.MaxBudget != null || s.Guests != null))
            {
                // Relax everything but the event type, and say so.
                var wider = await _tools.SearchPackagesAsync(s.Category, null, null, null, null);
                if (wider.Count > 0)
                {
                    var r = Say($"I couldn't find packages{what}. Here {(wider.Count == 1 ? "is one" : "are some")} without those limits:", new() { "Request quotes instead", "Find packages" },
                        new RoshiAction("Request quotes", "quotes"));
                    r.Cards.Add(new RoshiCard { Type = "packages", Packages = wider });
                    return r;
                }
            }
            if (found.Count == 0)
                return Say($"There aren't any packages{what} listed yet. Post a quote request and vendors will send you offers.", new() { "Find wedding packages", "My bookings" },
                    new RoshiAction("Request quotes", "quotes"));

            var reply = Say($"Here {(found.Count == 1 ? "is a package" : $"are {found.Count} packages")}{what}. Tap one to see what's included and get the price for your guests.",
                new() { "How does booking work?", "How do refunds work?", "My bookings" }, new RoshiAction("Browse all packages", s.Category != null ? $"category:{s.Category}" : "packages"));
            reply.Cards.Add(new RoshiCard { Type = "packages", Packages = found });
            return reply;
        }

        // ── Understanding a search ─────────────────────────────────────────────────────────────

        private record Search(string? Category, string? City, decimal? MaxBudget, int? Guests)
        {
            public bool HasAny => Category != null || City != null || MaxBudget != null || Guests != null;
        }

        private static readonly Dictionary<string, string> Synonyms = new()
        {
            ["shaadi"] = "wedding", ["marriage"] = "wedding", ["reception"] = "wedding", ["sangeet"] = "wedding", ["mehendi"] = "wedding", ["engagement"] = "wedding",
            ["bday"] = "birthday", ["party"] = "birthday",
            ["office"] = "corporate", ["conference"] = "corporate", ["offsite"] = "corporate",
        };

        private async Task<Search> ParseSearchAsync(string t)
        {
            var categories = await _db.EventCategories.AsNoTracking().Where(c => c.IsActive)
                .Select(c => new { c.CategoryKey, c.Name }).ToListAsync();
            string? category = categories
                .FirstOrDefault(c => Mentions(t, c.CategoryKey) || Mentions(t, c.Name))?.CategoryKey;
            category ??= Synonyms.FirstOrDefault(kv => Mentions(t, kv.Key)).Value;

            var cities = await _db.Packages.AsNoTracking().Where(p => p.IsActive && p.IsVerified)
                .Select(p => p.Address.City).Distinct().ToListAsync();
            string? city = cities.Where(c => !string.IsNullOrWhiteSpace(c)).FirstOrDefault(c => Mentions(t, c.ToLower()));
            if (city == null)
            {
                var m = Regex.Match(t, @"\b(?:in|at|near)\s+([a-z][a-z ]{2,20}?)(?=\s+(?:under|below|for|with|within|budget)\b|[,.?!]|$)");
                if (m.Success && !Regex.IsMatch(m.Groups[1].Value, @"\b(budget|my|the|a)\b")) city = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(m.Groups[1].Value.Trim());
            }

            int? guests = null;
            var g = Regex.Match(t, @"(\d{2,5})\s*(guests?|people|persons?|pax|members|attendees)");
            if (g.Success) guests = int.Parse(g.Groups[1].Value);

            decimal? budget = null;
            var b = Regex.Match(t, @"(?:under|below|within|upto|up to|less than|max(?:imum)?|budget(?: of| is)?)\s*(?:₹|rs\.?|inr)?\s*([\d,.]+)\s*(k|thousand|l|lakh|lakhs|lac|lacs|cr|crore)?\b");
            if (!b.Success) b = Regex.Match(t, @"(?:₹|rs\.?|inr)\s*([\d,.]+)\s*(k|thousand|l|lakh|lakhs|lac|lacs|cr|crore)?\b");
            if (!b.Success) b = Regex.Match(t, @"\b([\d.]+)\s*(k|lakh|lakhs|lac|lacs|l|cr|crore)\b");
            if (b.Success && decimal.TryParse(b.Groups[1].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                budget = b.Groups[2].Value switch
                {
                    "k" or "thousand" => amount * 1_000m,
                    "l" or "lakh" or "lakhs" or "lac" or "lacs" => amount * 1_00_000m,
                    "cr" or "crore" => amount * 1_00_00_000m,
                    _ => amount
                };
                if (budget < 1000) budget = null; // "under 5" is not a budget
            }
            return new Search(category, city, budget, guests);
        }

        private static bool Mentions(string text, string? word) =>
            !string.IsNullOrWhiteSpace(word) && Regex.IsMatch(text, $@"\b{Regex.Escape(word.ToLowerInvariant())}s?\b");

        private static string Describe(Search s)
        {
            var parts = new List<string>();
            if (s.Category != null) parts.Add($" for your {s.Category}");
            if (s.City != null) parts.Add($" in {s.City}");
            if (s.Guests != null) parts.Add($" for {s.Guests} guests");
            if (s.MaxBudget != null) parts.Add($" under {Money(s.MaxBudget.Value)}");
            return string.Concat(parts);
        }

        // ── Helpers ────────────────────────────────────────────────────────────────────────────

        public static List<BookingCard> Upcoming(List<BookingCard> bookings, DateTime today) => bookings
            .Where(b => b.EventDay >= today && b.Status is not ("cancelled" or "rejected" or "completed" or "settled"))
            .OrderBy(b => b.EventDay)
            .ToList();

        public static string DaysAway(DateTime day, DateTime today)
        {
            var days = (day.Date - today.Date).Days;
            return days switch { < 0 => $"was {-days} day{(days == -1 ? "" : "s")} ago", 0 => "today", 1 => "tomorrow", _ => $"in {days} days" };
        }

        public static string Label(string status) => status switch
        {
            "pending" => "Awaiting payment",
            "advance_paid" => "Paid, awaiting vendor confirmation",
            "confirmed" => "Confirmed",
            "in_progress" => "In progress",
            "completed" => "Completed",
            "settled" => "Settled",
            "cancelled" => "Cancelled",
            "rejected" => "Declined by vendor",
            "disputed" => "Under dispute",
            _ => status
        };

        private string Explain(string status) => status switch
        {
            "pending" => $"Pay the {_tools.AdvancePercent}% advance or the full amount to lock the date.",
            "advance_paid" => "The vendor will confirm it shortly; a chat with them opens when they do.",
            "confirmed" => "You're all set, and you can message the vendor in Messages.",
            "in_progress" => "Enjoy your event!",
            "completed" => "Leave a review to earn 50 points.",
            "cancelled" => "Any refund is shown on the booking.",
            _ => ""
        };

        private static RoshiReply Say(string text, List<string> suggestions, params RoshiAction[] actions) => new()
        {
            Reply = text,
            Suggestions = suggestions,
            Actions = actions.ToList(),
            PoweredBy = "rules"
        };
    }
}

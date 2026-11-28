using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using POSSystem.Models;

namespace POSSystem.Data
{
    // Sends the one-time sign-in code. The server uses its Telegram bot (Alerts:TelegramToken, Alerts:TelegramChatId).
    public interface IOtpSender
    {
        bool Ready { get; }
        Task<bool> SendAsync(string text, CancellationToken ct = default);
    }

    public class TelegramOtpSender : IOtpSender
    {
        private readonly IHttpClientFactory _http;
        private readonly string? _token;
        private readonly string? _chat;

        public TelegramOtpSender(IHttpClientFactory http, IConfiguration config)
        {
            _http = http;
            _token = config["Alerts:TelegramToken"];
            _chat = config["Alerts:TelegramChatId"];
        }

        public bool Ready => !string.IsNullOrWhiteSpace(_token) && !string.IsNullOrWhiteSpace(_chat);

        public async Task<bool> SendAsync(string text, CancellationToken ct = default)
        {
            if (!Ready) return false;
            try
            {
                var client = _http.CreateClient("telegram-otp");
                client.Timeout = TimeSpan.FromSeconds(10);
                var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["chat_id"] = _chat!, ["text"] = text });
                using var res = await client.PostAsync($"https://api.telegram.org/bot{_token}/sendMessage", body, ct);
                return res.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    // Second sign-in step for the platform owner: after the right password a 6-digit code goes to Telegram.
    // The code works once, for 5 minutes, and 5 wrong tries end it. Only a hash of the code is kept, in memory.
    // When the server has no Telegram bot set up (local PC, tests), the platform owner signs in with the password only.
    public class PlatformOtp
    {
        public const int Minutes = 5;
        public const int MaxTries = 5;

        private sealed record Pending(string UserId, byte[] Hash, int Tries, DateTimeOffset Expires);

        private readonly IOtpSender _sender;
        private readonly IMemoryCache _cache;

        public PlatformOtp(IOtpSender sender, IMemoryCache cache)
        {
            _sender = sender;
            _cache = cache;
        }

        public bool Required => _sender.Ready;

        private static string Key(string challenge) => "platform-otp:" + challenge;

        private static byte[] Hash(string code) => SHA256.HashData(Encoding.UTF8.GetBytes(code));

        // Sends a new code; returns the challenge the browser sends back with the code, or null when sending failed.
        public async Task<string?> StartAsync(AppUser user, string? ip, CancellationToken ct = default)
        {
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            var challenge = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
            var text = $"AllInOne POS platform sign-in code: {code}\n"
                + $"It works for {Minutes} minutes. Sign-in for {user.Email} from {ip ?? "unknown address"}.\n"
                + "If this was not you, do not share the code and change the platform password.";
            if (!await _sender.SendAsync(text, ct)) return null;
            var expires = DateTimeOffset.UtcNow.AddMinutes(Minutes);
            _cache.Set(Key(challenge), new Pending(user.Id, Hash(code), 0, expires), expires);
            return challenge;
        }

        // Returns the user id when the code is right; null otherwise.
        public string? Verify(string? challenge, string? code)
        {
            if (string.IsNullOrWhiteSpace(challenge) || string.IsNullOrWhiteSpace(code)) return null;
            if (!_cache.TryGetValue(Key(challenge), out Pending? pending) || pending == null) return null;
            if (pending.Expires < DateTimeOffset.UtcNow)
            {
                _cache.Remove(Key(challenge));
                return null;
            }
            if (CryptographicOperations.FixedTimeEquals(pending.Hash, Hash(code.Trim())))
            {
                _cache.Remove(Key(challenge));
                return pending.UserId;
            }
            var tries = pending.Tries + 1;
            if (tries >= MaxTries) _cache.Remove(Key(challenge));
            else _cache.Set(Key(challenge), pending with { Tries = tries }, pending.Expires);
            return null;
        }
    }
}

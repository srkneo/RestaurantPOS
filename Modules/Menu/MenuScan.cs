using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace POSSystem.Menu
{
    public record MenuPhoto(byte[] Bytes, string ContentType);

    // One dish read from a menu photo. Nothing is saved until the owner confirms the review table.
    public record MenuDraftItem(string Category, string Name, decimal Price, bool IsVeg, string? Description);

    public class MenuScanUnavailableException : Exception
    {
        public MenuScanUnavailableException(string message) : base(message) { }
    }

    public class MenuScanFailedException : Exception
    {
        public MenuScanFailedException(string message, Exception? inner = null) : base(message, inner) { }
    }

    // Reads dishes from photos of a paper menu. Replaced by a fake in tests.
    public interface IMenuReader
    {
        Task<IReadOnlyList<MenuDraftItem>> ReadAsync(IReadOnlyList<MenuPhoto> photos, CancellationToken ct);
    }

    // Google Gemini vision model. Settings: Gemini__ApiKey (required), Gemini__Model (optional).
    public class GeminiMenuReader : IMenuReader
    {
        public const string DefaultModel = "gemini-2.5-flash";

        public const string Prompt =
            "These photos are the pages of one restaurant's printed menu, in order. List every dish with its price.\n" +
            "Rules:\n" +
            "- Keep each dish name exactly as printed, in the same script (Gujarati, Hindi or English). Do not translate.\n" +
            "- category: the section heading the dish is under, as printed. If there is none, use \"Other\".\n" +
            "- price: the rupee price of one portion, as a number. If a dish has several sizes or prices (for example half and full), " +
            "make one entry per size and add the size to the name, like \"Paneer Tikka (Half)\".\n" +
            "- isVeg: false only for egg, chicken, mutton, fish, prawn or other meat; otherwise true.\n" +
            "- description: only if a description is printed under the dish; otherwise an empty string.\n" +
            "- Skip headings without a price, phone numbers, addresses, timings, taxes and notes. Never invent dishes or prices.";

        private readonly HttpClient _http;
        private readonly string? _apiKey;
        private readonly string _model;

        public GeminiMenuReader(HttpClient http, IConfiguration config)
        {
            _http = http;
            _apiKey = config["Gemini:ApiKey"];
            _model = string.IsNullOrWhiteSpace(config["Gemini:Model"]) ? DefaultModel : config["Gemini:Model"]!;
        }

        public async Task<IReadOnlyList<MenuDraftItem>> ReadAsync(IReadOnlyList<MenuPhoto> photos, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new MenuScanUnavailableException("Menu scan is not set up on this server (Gemini__ApiKey is missing).");

            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(_model)}:generateContent");
            request.Headers.Add("x-goog-api-key", _apiKey);
            request.Content = JsonContent.Create(BuildRequest(photos));

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                throw new MenuScanFailedException("The menu reader did not answer. Try again in a minute.", ex);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                    throw new MenuScanFailedException($"The menu reader refused the request ({(int)response.StatusCode}). Try again later.");
                return ParseResponse(body);
            }
        }

        public static object BuildRequest(IReadOnlyList<MenuPhoto> photos)
        {
            var parts = new List<object> { new { text = Prompt } };
            parts.AddRange(photos.Select(p => (object)new { inline_data = new { mime_type = p.ContentType, data = Convert.ToBase64String(p.Bytes) } }));
            return new
            {
                contents = new[] { new { parts } },
                generationConfig = new
                {
                    temperature = 0,
                    responseMimeType = "application/json",
                    responseSchema = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            items = new
                            {
                                type = "ARRAY",
                                items = new
                                {
                                    type = "OBJECT",
                                    properties = new
                                    {
                                        category = new { type = "STRING" },
                                        name = new { type = "STRING" },
                                        price = new { type = "NUMBER" },
                                        isVeg = new { type = "BOOLEAN" },
                                        description = new { type = "STRING" }
                                    },
                                    required = new[] { "category", "name", "price", "isVeg" }
                                }
                            }
                        },
                        required = new[] { "items" }
                    }
                }
            };
        }

        // Gemini answers { candidates: [ { content: { parts: [ { text: "<our JSON>" } ] } } ] }.
        public static IReadOnlyList<MenuDraftItem> ParseResponse(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                    throw new MenuScanFailedException("The menu reader gave no answer. Try clearer photos.");
                var first = candidates[0];
                if (!first.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
                    throw new MenuScanFailedException("The menu reader could not read these photos. Try clearer photos.");

                var text = string.Concat(parts.EnumerateArray()
                    .Where(p => p.TryGetProperty("text", out _))
                    .Select(p => p.GetProperty("text").GetString()));
                using var menu = JsonDocument.Parse(text);
                if (!menu.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                    return Array.Empty<MenuDraftItem>();

                var raw = new List<MenuDraftItem>();
                foreach (var item in items.EnumerateArray())
                {
                    raw.Add(new MenuDraftItem(
                        Str(item, "category"),
                        Str(item, "name"),
                        item.TryGetProperty("price", out var price) && price.ValueKind == JsonValueKind.Number && price.TryGetDecimal(out var p) ? p : -1m,
                        !item.TryGetProperty("isVeg", out var veg) || veg.ValueKind != JsonValueKind.False,
                        Str(item, "description")));
                }
                return MenuDraftRules.Clean(raw);
            }
            catch (JsonException ex)
            {
                throw new MenuScanFailedException("The menu reader's answer could not be understood. Try again.", ex);
            }
        }

        private static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
    }

    public static class MenuDraftRules
    {
        public const int MaxItems = 300;

        // Tidy what the AI (or a CSV) produced: trim, fit the field sizes, drop rows without a name or a sensible price, drop repeats.
        public static IReadOnlyList<MenuDraftItem> Clean(IEnumerable<MenuDraftItem> items)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<MenuDraftItem>();
            foreach (var i in items)
            {
                var name = Fit(i.Name, 100);
                if (name.Length == 0) continue;
                if (i.Price < 0 || i.Price > 100000) continue;
                var category = Fit(i.Category, 100);
                if (category.Length == 0) category = "Other";
                if (!seen.Add(category + "\n" + name)) continue;
                var description = Fit(i.Description, 500);
                result.Add(new MenuDraftItem(category, name, Math.Round(i.Price, 2), i.IsVeg, description.Length == 0 ? null : description));
                if (result.Count == MaxItems) break;
            }
            return result;
        }

        private static string Fit(string? value, int max)
        {
            var v = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return v.Length <= max ? v : v[..max].TrimEnd();
        }
    }
}

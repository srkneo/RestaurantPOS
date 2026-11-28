using System;
using System.Collections.Generic;
using System.Linq;

namespace POSSystem.Platform
{
    public sealed record Capability(string Key, string Name, string Description, IReadOnlyList<string> DependsOn);

    public static class Capabilities
    {
        public const string Tables = "tables";
        public const string KitchenScreen = "kitchen-screen";
        public const string Tokens = "tokens";
        public const string Reservations = "reservations";
        public const string QrOrdering = "qr-ordering";
        public const string Feedback = "feedback";
        public const string Loyalty = "loyalty";
        public const string Stock = "stock";
        public const string Recipes = "recipes";
        public const string Combos = "combos";
        public const string SellByWeight = "sell-by-weight";
        public const string SimpleCounter = "simple-counter";
        public const string Shifts = "shifts";
        public const string CreditNotes = "credit-notes";
        public const string Tips = "tips";
        public const string Expenses = "expenses";

        public static IReadOnlyList<Capability> All { get; } = new List<Capability>
        {
            new Capability(Tables, "Tables", "Dine-in tables and table status on the POS.", Array.Empty<string>()),
            new Capability(KitchenScreen, "Kitchen screen", "Kitchen order screen that shows KOTs to the cooks.", Array.Empty<string>()),
            new Capability(Tokens, "Tokens", "Token numbers for takeaway and counter orders.", Array.Empty<string>()),
            new Capability(Reservations, "Reservations", "Table bookings for guests.", new[] { Tables }),
            new Capability(QrOrdering, "QR ordering", "Guests order from a QR code on the table.", new[] { Tables }),
            new Capability(Feedback, "Feedback", "Guest feedback through a QR code.", Array.Empty<string>()),
            new Capability(Loyalty, "Loyalty", "Loyalty points and guest rewards.", Array.Empty<string>()),
            new Capability(Stock, "Stock", "Stock items, purchases and stock counts.", Array.Empty<string>()),
            new Capability(Recipes, "Recipes", "Recipes that use up stock when an item is sold.", new[] { Stock }),
            new Capability(Combos, "Combos", "Combo items made of several menu items.", Array.Empty<string>()),
            new Capability(SellByWeight, "Sell by weight", "Items sold by kg or gram.", Array.Empty<string>()),
            new Capability(SimpleCounter, "Simple counter", "Quick counter billing without tables or a kitchen screen.", Array.Empty<string>()),
            new Capability(Shifts, "Shifts", "Cashier shifts with cash count at close.", Array.Empty<string>()),
            new Capability(CreditNotes, "Credit notes", "Credit notes for returned or cancelled bills.", Array.Empty<string>()),
            new Capability(Tips, "Tips", "Tips on bills.", Array.Empty<string>()),
            new Capability(Expenses, "Expenses", "Daily expenses paid from the cash drawer.", Array.Empty<string>())
        };

        public static IReadOnlyList<string> Keys { get; } = All.Select(c => c.Key).ToList();

        public static Capability? Find(string? key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return All.FirstOrDefault(c => c.Key == key);
        }

        public static bool IsKnown(string? key) => Find(key) != null;

        public static IReadOnlyList<string> WithDependencies(IEnumerable<string> keys)
        {
            if (keys == null) throw new ArgumentException("Capabilities are missing.");
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                if (!IsKnown(key)) throw new ArgumentException($"Unknown capability: {key}");
                Add(key, result);
            }
            return All.Where(c => result.Contains(c.Key)).Select(c => c.Key).ToList();
        }

        private static void Add(string key, HashSet<string> result)
        {
            if (!result.Add(key)) return;
            foreach (var d in Find(key)!.DependsOn) Add(d, result);
        }

        public static string? Validate(IEnumerable<string>? keys)
        {
            if (keys == null) return "Capabilities are missing.";
            var list = keys.ToList();
            foreach (var key in list)
                if (!IsKnown(key)) return $"Unknown capability: {key}";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in list)
                if (!seen.Add(key)) return $"Capability {key} is listed twice.";
            foreach (var key in list)
                foreach (var d in Find(key)!.DependsOn)
                    if (!seen.Contains(d)) return $"Capability {key} needs {d}.";
            return null;
        }
    }
}

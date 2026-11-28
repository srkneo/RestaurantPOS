using System;
using System.Collections.Generic;
using System.Linq;
using POSSystem.Models;

namespace POSSystem.Platform
{
    public sealed record OutletType(string Key, string Name, string Description, IReadOnlyList<string> Capabilities);

    public static class OutletTypes
    {
        public const string Restaurant = "restaurant";
        public const string Cafe = "cafe";
        public const string TakeawayCounter = "takeaway-counter";
        public const string CloudKitchen = "cloud-kitchen";
        public const string Bakery = "bakery";
        public const string Dairy = "dairy";

        public static IReadOnlyList<OutletType> All { get; } = new List<OutletType>
        {
            new OutletType(Restaurant, "Restaurant", "Dine-in restaurant with tables, kitchen screen and takeaway.", new[] { Capabilities.Tables, Capabilities.KitchenScreen, Capabilities.Tokens, Capabilities.Reservations, Capabilities.QrOrdering, Capabilities.Feedback, Capabilities.Loyalty, Capabilities.Stock, Capabilities.Recipes, Capabilities.Combos, Capabilities.Shifts, Capabilities.CreditNotes, Capabilities.Tips, Capabilities.Expenses }),
            new OutletType(Cafe, "Cafe", "Cafe with a few tables; most orders are takeaway.", new[] { Capabilities.Tables, Capabilities.KitchenScreen, Capabilities.Tokens, Capabilities.QrOrdering, Capabilities.Feedback, Capabilities.Loyalty, Capabilities.Stock, Capabilities.Recipes, Capabilities.Combos, Capabilities.Shifts, Capabilities.CreditNotes, Capabilities.Tips, Capabilities.Expenses }),
            new OutletType(TakeawayCounter, "Takeaway counter", "Counter that only sells takeaway with token numbers.", new[] { Capabilities.KitchenScreen, Capabilities.Tokens, Capabilities.Feedback, Capabilities.Loyalty, Capabilities.Stock, Capabilities.Recipes, Capabilities.Combos, Capabilities.Shifts, Capabilities.CreditNotes, Capabilities.Expenses }),
            new OutletType(CloudKitchen, "Cloud kitchen", "Kitchen that only cooks delivery and takeaway orders.", new[] { Capabilities.KitchenScreen, Capabilities.Tokens, Capabilities.Stock, Capabilities.Recipes, Capabilities.Combos, Capabilities.Shifts, Capabilities.CreditNotes, Capabilities.Expenses }),
            new OutletType(Bakery, "Bakery", "Bakery or sweet shop with quick counter bills.", new[] { Capabilities.KitchenScreen, Capabilities.Tokens, Capabilities.Feedback, Capabilities.Loyalty, Capabilities.Stock, Capabilities.Recipes, Capabilities.Combos, Capabilities.SellByWeight, Capabilities.SimpleCounter, Capabilities.Shifts, Capabilities.CreditNotes, Capabilities.Expenses }),
            new OutletType(Dairy, "Dairy", "Dairy shop selling milk, curd and paneer by kg or litre.", new[] { Capabilities.KitchenScreen, Capabilities.Tokens, Capabilities.Loyalty, Capabilities.Stock, Capabilities.SellByWeight, Capabilities.SimpleCounter, Capabilities.Shifts, Capabilities.CreditNotes, Capabilities.Expenses })
        };

        public static IReadOnlyList<string> Keys { get; } = All.Select(t => t.Key).ToList();

        public static OutletType? Find(string? key) => key == null ? null : All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));

        public static bool IsKnown(string? key) => Find(key) != null;

        public static OutletSettings ApplyPreset(string? key, OutletSettings settings)
        {
            if (settings == null) throw new ArgumentException("Settings are missing.");
            if (!IsKnown(key)) throw new ArgumentException($"Unknown outlet type: {key}");
            var s = OutletSettings.Parse(settings.ToJson());
            switch (key)
            {
                case Restaurant:
                    Set(s, dineIn: true, defaultType: OrderType.DineIn, takeawayNeedsName: true, simpleCounter: false);
                    break;
                case Cafe:
                    Set(s, dineIn: true, defaultType: OrderType.Takeaway, takeawayNeedsName: true, simpleCounter: false);
                    break;
                case TakeawayCounter:
                    Set(s, dineIn: false, defaultType: OrderType.Takeaway, takeawayNeedsName: false, simpleCounter: false);
                    break;
                case CloudKitchen:
                    Set(s, dineIn: false, defaultType: OrderType.Takeaway, takeawayNeedsName: true, simpleCounter: false);
                    break;
                case Bakery:
                    Set(s, dineIn: true, defaultType: OrderType.Takeaway, takeawayNeedsName: false, simpleCounter: true);
                    break;
                case Dairy:
                    Set(s, dineIn: false, defaultType: OrderType.Takeaway, takeawayNeedsName: false, simpleCounter: true);
                    break;
            }
            return s;
        }

        private static void Set(OutletSettings s, bool dineIn, OrderType defaultType, bool takeawayNeedsName, bool simpleCounter)
        {
            s.DineInEnabled = dineIn;
            s.TakeawayEnabled = true;
            s.DefaultOrderType = defaultType;
            s.TakeawayNeedsName = takeawayNeedsName;
            s.KitchenScreenEnabled = true;
            s.SimpleCounter = simpleCounter;
        }
    }
}

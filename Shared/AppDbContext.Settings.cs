using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using POSSystem.Models;

namespace POSSystem.Data
{
    // All settings of an outlet are one JSON text. Two screens saving different settings at the same moment used
    // to undo each other: the later save wrote back the whole JSON it had read before the first save.
    // Now, just before saving, only the settings this request changed are put onto the latest saved JSON,
    // one save at a time per outlet, so the other screen's change stays.
    public partial class AppDbContext
    {
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> SettingsLocks = new();

        // The settings that differ between original and proposed are written onto current; the rest of current stays.
        public static string MergeSettings(string? original, string? proposed, string? current)
        {
            try
            {
                var before = JsonNode.Parse(string.IsNullOrWhiteSpace(original) ? "{}" : original) as JsonObject ?? new JsonObject();
                var mine = JsonNode.Parse(string.IsNullOrWhiteSpace(proposed) ? "{}" : proposed) as JsonObject ?? new JsonObject();
                var latest = JsonNode.Parse(string.IsNullOrWhiteSpace(current) ? "{}" : current) as JsonObject ?? new JsonObject();
                foreach (var (key, value) in mine)
                {
                    if (!before.TryGetPropertyValue(key, out var old) || !JsonNode.DeepEquals(old, value))
                        latest[key] = value?.DeepClone();
                }
                foreach (var key in before.Select(kv => kv.Key).ToList())
                {
                    if (!mine.ContainsKey(key)) latest.Remove(key);
                }
                return latest.ToJsonString();
            }
            catch (JsonException)
            {
                return proposed ?? "{}";
            }
        }

        private static string Normalize(string? json) => OutletSettings.Parse(json).ToJson();

        private List<EntityEntry<Outlet>> ChangedSettings() =>
            ChangeTracker.Entries<Outlet>()
                .Where(e => e.State == EntityState.Modified && e.Property(o => o.Settings).IsModified)
                .ToList();

        private void MergeInto(EntityEntry<Outlet> entry, string? current)
        {
            var original = entry.Property(o => o.Settings).OriginalValue;
            if (current == null || current == original) return;
            // Saved JSON can leave out settings that still have their default ("{}" for a new outlet):
            // fill those in first, so only what this request really changed counts as its change.
            entry.Entity.Settings = MergeSettings(Normalize(original), entry.Entity.Settings, Normalize(current));
            entry.Property(o => o.Settings).OriginalValue = current;
        }

        private async Task<List<SemaphoreSlim>> LockAndMergeSettingsAsync(CancellationToken ct)
        {
            var held = new List<SemaphoreSlim>();
            var changed = ChangedSettings();
            if (changed.Count == 0) return held;
            try
            {
                foreach (var id in changed.Select(e => e.Entity.Id).Distinct().OrderBy(i => i))
                {
                    var gate = SettingsLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
                    await gate.WaitAsync(ct);
                    held.Add(gate);
                }
                foreach (var entry in changed)
                {
                    var current = await Outlets.AsNoTracking().IgnoreQueryFilters()
                        .Where(o => o.Id == entry.Entity.Id).Select(o => o.Settings).FirstOrDefaultAsync(ct);
                    MergeInto(entry, current);
                }
                return held;
            }
            catch
            {
                foreach (var gate in held) gate.Release();
                throw;
            }
        }

        private List<SemaphoreSlim> LockAndMergeSettings()
        {
            var held = new List<SemaphoreSlim>();
            var changed = ChangedSettings();
            if (changed.Count == 0) return held;
            try
            {
                foreach (var id in changed.Select(e => e.Entity.Id).Distinct().OrderBy(i => i))
                {
                    var gate = SettingsLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
                    gate.Wait();
                    held.Add(gate);
                }
                foreach (var entry in changed)
                {
                    var current = Outlets.AsNoTracking().IgnoreQueryFilters()
                        .Where(o => o.Id == entry.Entity.Id).Select(o => o.Settings).FirstOrDefault();
                    MergeInto(entry, current);
                }
                return held;
            }
            catch
            {
                foreach (var gate in held) gate.Release();
                throw;
            }
        }
    }
}

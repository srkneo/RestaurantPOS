using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace POSSystem.Platform
{
    // How support wants a shop PC updated: auto (the newest release), paused, or pinned to one version.
    public record UpdatePolicyDto(string Mode, string? Version);

    // What deploy/auto-update.sh did last time, read from update/status.json.
    public record UpdateStatusDto(string? CheckedAt, string? Trigger, string? Current, string? Target, string? Result, string? Message);

    public record ShopUpdatesDto(bool Available, string Mode, string? PinnedVersion, bool Requested, UpdateStatusDto? Status);

    // The app's side of automatic updates on a shop PC. It shares a folder (Updates:Folder, /app/update) with
    // deploy/auto-update.sh: policy (written here after each licence renewal), request ("Update now"),
    // status.json (written by the script).
    public class ShopUpdates
    {
        public static readonly string[] Modes = { "auto", "paused", "pinned" };
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private static readonly Regex VersionPattern = new(@"^\d{1,4}\.\d{1,4}\.\d{1,4}$");

        private readonly IConfiguration _config;
        private readonly LicenceService _licence;

        public ShopUpdates(IConfiguration config, LicenceService licence)
        {
            _config = config;
            _licence = licence;
        }

        public static string FolderOf(IConfiguration config) =>
            string.IsNullOrWhiteSpace(config["Updates:Folder"]) ? "/app/update" : config["Updates:Folder"]!;

        public static bool IsVersion(string? text) => text is not null && VersionPattern.IsMatch(text);

        private string Folder => FolderOf(_config);

        // Only on a shop PC (licence required) where the update script's folder is mounted.
        public bool Available => _licence.Required && Directory.Exists(Folder);

        public ShopUpdatesDto Describe()
        {
            if (!Available) return new ShopUpdatesDto(false, "auto", null, false, null);
            var policy = ReadPolicy(Folder);
            return new ShopUpdatesDto(true, policy.Mode, policy.Version, File.Exists(Path.Combine(Folder, "request")), ReadStatus());
        }

        // "Update now": the script looks for this file every 2 minutes.
        public ShopUpdatesDto RequestNow()
        {
            if (!Available) throw new ArgumentException("Updates from here work only on a shop PC.");
            if (ReadPolicy(Folder).Mode == "paused") throw new ArgumentException("Updates are paused by support. Contact support to update this PC.");
            WriteAtomically(Path.Combine(Folder, "request"), DateTime.UtcNow.ToString("o") + "\n");
            return Describe();
        }

        // Called after each licence renewal. Does nothing where the folder is missing (cloud server, development).
        public static void WritePolicy(IConfiguration config, UpdatePolicyDto policy)
        {
            var folder = FolderOf(config);
            if (!Directory.Exists(folder)) return;
            var mode = Array.IndexOf(Modes, policy.Mode) >= 0 ? policy.Mode : "auto";
            // Only digits and dots reach the file the update script reads.
            var version = mode == "pinned" && IsVersion(policy.Version) ? policy.Version : "";
            if (mode == "pinned" && version == "") mode = "auto";
            WriteAtomically(Path.Combine(folder, "policy"), $"mode={mode}\nversion={version}\n");
        }

        public static UpdatePolicyDto ReadPolicy(string folder)
        {
            var mode = "auto";
            string? version = null;
            var file = Path.Combine(folder, "policy");
            if (File.Exists(file))
            {
                foreach (var line in File.ReadAllLines(file))
                {
                    if (line.StartsWith("mode=", StringComparison.Ordinal)) mode = line[5..].Trim();
                    else if (line.StartsWith("version=", StringComparison.Ordinal)) version = line[8..].Trim();
                }
            }
            if (Array.IndexOf(Modes, mode) < 0) mode = "auto";
            return new UpdatePolicyDto(mode, string.IsNullOrEmpty(version) ? null : version);
        }

        private UpdateStatusDto? ReadStatus()
        {
            var file = Path.Combine(Folder, "status.json");
            try
            {
                return File.Exists(file) ? JsonSerializer.Deserialize<UpdateStatusDto>(File.ReadAllText(file), Json) : null;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void WriteAtomically(string path, string text)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
        }
    }
}

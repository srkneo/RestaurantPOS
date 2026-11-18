using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace POSSystem.Platform
{
    public record BackupStatusDto(bool Available, string? CheckedAt, string? BackupAt, string? LastCopiedAt, string? Target,
        string? Result, string? Message, bool Warning, bool Requested);

    // The app's side of backups on a shop PC. deploy/copy-backup.sh copies each nightly backup to a USB disk or NAS and
    // writes update/backup-status.json; "Back up now" leaves update/backup-request for it.
    public class ShopBackups
    {
        // The owner is warned when the newest backup, or the newest copy off the PC, is older than this.
        public static readonly TimeSpan WarnAfter = TimeSpan.FromDays(2);
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private sealed record FileStatus(string? CheckedAt, string? BackupAt, string? LastCopiedAt, string? Target, string? Result, string? Message);

        private readonly IConfiguration _config;
        private readonly LicenceService _licence;
        private readonly TimeProvider _time;

        public ShopBackups(IConfiguration config, LicenceService licence, TimeProvider time)
        {
            _config = config;
            _licence = licence;
            _time = time;
        }

        private string Folder => ShopUpdates.FolderOf(_config);

        public bool Available => _licence.Required && Directory.Exists(Folder);

        public BackupStatusDto Describe()
        {
            if (!Available) return new BackupStatusDto(false, null, null, null, null, null, null, false, false);
            var requested = File.Exists(Path.Combine(Folder, "backup-request"));
            var s = Read();
            // Nothing written yet: the copy script has not run once, so there is nothing to warn about.
            if (s is null) return new BackupStatusDto(true, null, null, null, null, null, null, false, requested);
            var warning = TooOld(s.BackupAt) || TooOld(s.LastCopiedAt);
            return new BackupStatusDto(true, Blank(s.CheckedAt), Blank(s.BackupAt), Blank(s.LastCopiedAt), Blank(s.Target),
                Blank(s.Result), Blank(s.Message), warning, requested);
        }

        // "Back up now": the copy script looks for this file every 2 minutes, makes a fresh backup and copies it.
        public BackupStatusDto RequestNow()
        {
            if (!Available) throw new ArgumentException("Backups from here work only on a shop PC.");
            var path = Path.Combine(Folder, "backup-request");
            File.WriteAllText(path + ".tmp", DateTime.UtcNow.ToString("o") + "\n", new UTF8Encoding(false));
            File.Move(path + ".tmp", path, overwrite: true);
            return Describe();
        }

        private bool TooOld(string? isoTime)
        {
            if (string.IsNullOrWhiteSpace(isoTime)) return true;
            if (!DateTime.TryParse(isoTime, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)) return true;
            return _time.GetUtcNow().UtcDateTime - at > WarnAfter;
        }

        private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        private FileStatus? Read()
        {
            var file = Path.Combine(Folder, "backup-status.json");
            try
            {
                return File.Exists(file) ? JsonSerializer.Deserialize<FileStatus>(File.ReadAllText(file), Json) : null;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CocTimeManager.Models;

public sealed class TrackedUpgrade : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";

    /// <summary>Stable int id for OS notification / alarm APIs.</summary>
    [JsonIgnore] public int NotificationId => Id.GetHashCode() & 0x7FFFFFFF;

    // Stored and compared in UTC so timezone / daylight-saving changes don't shift the countdown.
    // The setters also convert entries saved by older versions, which stored local time.
    private DateTime _startedAt, _finishAt;
    public DateTime StartedAt { get => _startedAt; set => _startedAt = ToUtc(value); }
    public DateTime FinishAt { get => _finishAt; set => _finishAt = ToUtc(value); }

    private static DateTime ToUtc(DateTime t) => t.Kind switch
    {
        DateTimeKind.Utc => t,
        _ when t == default => t,
        DateTimeKind.Local => t.ToUniversalTime(),
        _ => DateTime.SpecifyKind(t, DateTimeKind.Local).ToUniversalTime(), // no offset saved: was local time
    };

    /// <summary>Set for upgrades that came from a village export (null = added by hand).
    /// The next import replaces these, so they stay in sync with the game.</summary>
    public string? ImportKey { get; set; }

    /// <summary>Imported from a Builder Base section (buildings2, units2, ...); see <see cref="VillageImport"/>.</summary>
    [JsonIgnore] public bool IsBuilderBase => ImportKey?.Split(':')[0].EndsWith('2') == true;

    /// <summary>Export time of the import that created/updated this entry (UTC).</summary>
    public DateTime ImportedAt { get => _importedAt; set => _importedAt = ToUtc(value); }
    private DateTime _importedAt;

    /// <summary>When this entry was created or last changed (UTC); the newer copy wins when syncing devices.
    /// Entries saved before sync existed have the default (oldest) value.</summary>
    public DateTime UpdatedAt { get => _updatedAt; set => _updatedAt = ToUtc(value); }
    private DateTime _updatedAt;

    /// <summary>True once the "done" sound/notification has fired, so it only fires once.</summary>
    public bool Notified { get; set; }

    /// <summary>0-100, how far along the upgrade is in real time.</summary>
    [JsonIgnore]
    public double Progress
    {
        get
        {
            var total = (FinishAt - StartedAt).TotalSeconds;
            if (total <= 0) return 100;
            return Math.Clamp((DateTime.UtcNow - StartedAt).TotalSeconds / total * 100.0, 0, 100);
        }
    }

    [JsonIgnore] public string FinishText => FinishAt.ToLocalTime().ToString("f", CultureInfo.CurrentCulture);

    [JsonIgnore]
    public string RemainingText
    {
        get
        {
            var left = FinishAt - DateTime.UtcNow;
            return left <= TimeSpan.Zero ? "Done!" : UpgradeCalculator.Format(left) + " left";
        }
    }

    [JsonIgnore] public bool IsDone => FinishAt <= DateTime.UtcNow;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RemainingText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDone)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
    }
}

public sealed class AppSettings
{
    /// <summary>Off: Builder Base upgrades stay stored (for import and sync) but are hidden and don't notify.</summary>
    public bool ShowBuilderBase { get; set; } = true;
}

public static class UpgradeStore
{
    private static readonly string Dir = Path.Combine(ConfigRoot(), "CocTimeManager");

    /// <summary>
    /// The user config folder. GetFolderPath returns "" when the folder doesn't exist yet (e.g. no ~/.config),
    /// which made the app save to a relative path in whatever folder it was started from; so ask .NET to
    /// create it, and never fall back to a relative path.
    /// </summary>
    private static string ConfigRoot()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
        if (Path.IsPathRooted(dir)) return dir;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.IsPathRooted(home) ? Path.Combine(home, ".config") : Path.GetTempPath();
    }
    private static readonly string FilePath = Path.Combine(Dir, "upgrades.json");
    private static readonly string DeletedPath = Path.Combine(Dir, "deleted.json");
    private static readonly string SettingsPath = Path.Combine(Dir, "settings.json");

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Unreadable: use the defaults rather than crash.
        }
        return new AppSettings();
    }

    public static void SaveSettings(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings));
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    /// <summary>False for Builder Base upgrades while they're toggled off. Read from disk because Android's
    /// alarm receivers run without the app.</summary>
    public static bool ShouldNotify(TrackedUpgrade upgrade) => !upgrade.IsBuilderBase || LoadSettings().ShowBuilderBase;

    /// <summary>How long deletions are remembered for syncing (so a deleted upgrade isn't brought back).</summary>
    public static readonly TimeSpan DeletedRetention = TimeSpan.FromDays(60);

    /// <summary>Ids of upgrades removed by hand -> when (UTC).</summary>
    public static Dictionary<Guid, DateTime> LoadDeleted()
    {
        try
        {
            if (File.Exists(DeletedPath))
            {
                var cutoff = DateTime.UtcNow - DeletedRetention;
                return (JsonSerializer.Deserialize<Dictionary<Guid, DateTime>>(File.ReadAllText(DeletedPath)) ?? [])
                    .Select(kv => KeyValuePair.Create(kv.Key, kv.Value.ToUniversalTime()))
                    .Where(kv => kv.Value > cutoff)
                    .ToDictionary();
            }
        }
        catch (Exception)
        {
            // Unreadable: forget old deletions rather than crash.
        }
        return [];
    }

    public static void SaveDeleted(Dictionary<Guid, DateTime> deleted)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(DeletedPath, JsonSerializer.Serialize(deleted));
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    public static List<TrackedUpgrade> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<TrackedUpgrade>>(File.ReadAllText(FilePath)) ?? [];
                // Entries saved before StartedAt existed: count progress from now on.
                foreach (var u in list.Where(u => u.StartedAt == default))
                    u.StartedAt = u.FinishAt <= DateTime.UtcNow ? u.FinishAt : DateTime.UtcNow;
                return list;
            }
        }
        catch (Exception)
        {
            // Corrupt or unreadable file: start with an empty list rather than crashing.
        }
        return [];
    }

    public static void Save(IEnumerable<TrackedUpgrade> upgrades)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(upgrades, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Saving is best effort.
        }
    }
}

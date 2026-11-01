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

public static class UpgradeStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CocTimeManager", "upgrades.json");

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

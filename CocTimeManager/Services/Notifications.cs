using CocTimeManager.Models;

namespace CocTimeManager.Services;

/// <summary>
/// Plays the "upgrade done" sound and shows a system notification.
/// Each platform project sets <see cref="Notifications.Current"/> at startup.
/// </summary>
public interface INotificationService
{
    /// <summary>Called by the app when it sees an upgrade has finished while it is running.</summary>
    void UpgradeFinished(TrackedUpgrade upgrade);

    /// <summary>Called when an upgrade starts being tracked. Platforms that can notify while the app
    /// is closed (Android) schedule it here; desktop does nothing.</summary>
    void Schedule(TrackedUpgrade upgrade);

    /// <summary>Called when a tracked upgrade is removed.</summary>
    void Cancel(TrackedUpgrade upgrade);
}

public static class Notifications
{
    public static INotificationService Current { get; set; } = new NullNotificationService();
}

public sealed class NullNotificationService : INotificationService
{
    public void UpgradeFinished(TrackedUpgrade upgrade) { }
    public void Schedule(TrackedUpgrade upgrade) { }
    public void Cancel(TrackedUpgrade upgrade) { }
}

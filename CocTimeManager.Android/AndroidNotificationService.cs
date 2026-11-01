using System;
using Android.App;
using Android.Content;
using Android.Media;
using Android.OS;
using CocTimeManager.Models;
using CocTimeManager.Services;
using Uri = Android.Net.Uri;

namespace CocTimeManager.Android;

/// <summary>
/// Android: an AlarmManager alarm per tracked upgrade fires <see cref="UpgradeAlarmReceiver"/> at FinishAt,
/// even if the app is closed. The notification channel plays snd/notif.mp3 (bundled as res/raw/notif).
/// </summary>
public sealed class AndroidNotificationService(Context context) : INotificationService
{
    // The channel's sound is fixed once created; bump the id if the sound ever changes.
    public const string ChannelId = "upgrade_done_v1";
    public const string ExtraId = "upgrade_id";
    public const string ExtraName = "upgrade_name";

    private readonly Context _context = context.ApplicationContext ?? context;

    public void UpgradeFinished(TrackedUpgrade upgrade)
    {
        CancelAlarm(upgrade);
        Show(_context, upgrade.NotificationId, upgrade.Name);
    }

    public void Schedule(TrackedUpgrade upgrade)
    {
        var alarms = (AlarmManager?)_context.GetSystemService(Context.AlarmService);
        if (alarms is null) return;

        var triggerAt = new DateTimeOffset(upgrade.FinishAt).ToUnixTimeMilliseconds();
        var pending = AlarmIntent(_context, upgrade);

        if (!OperatingSystem.IsAndroidVersionAtLeast(31) || alarms.CanScheduleExactAlarms())
            alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAt, pending);
        else
            alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAt, pending); // may be a few minutes late
    }

    public void Cancel(TrackedUpgrade upgrade)
    {
        CancelAlarm(upgrade);
        var nm = (NotificationManager?)_context.GetSystemService(Context.NotificationService);
        nm?.Cancel(upgrade.NotificationId);
    }

    private void CancelAlarm(TrackedUpgrade upgrade)
    {
        var alarms = (AlarmManager?)_context.GetSystemService(Context.AlarmService);
        alarms?.Cancel(AlarmIntent(_context, upgrade));
    }

    private static PendingIntent AlarmIntent(Context context, TrackedUpgrade upgrade)
    {
        var intent = new Intent(context, typeof(UpgradeAlarmReceiver));
        intent.PutExtra(ExtraId, upgrade.Id.ToString());
        intent.PutExtra(ExtraName, upgrade.Name);
        return PendingIntent.GetBroadcast(context, upgrade.NotificationId, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    public static void Show(Context context, int notificationId, string upgradeName)
    {
        var nm = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        if (nm is null) return;
        EnsureChannel(context, nm);

        var openApp = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        var contentIntent = openApp is null
            ? null
            : PendingIntent.GetActivity(context, 0, openApp, PendingIntentFlags.Immutable);

        var notification = new Notification.Builder(context, ChannelId)
            .SetSmallIcon(Resource.Drawable.icon)
            .SetContentTitle("Upgrade complete!")
            .SetContentText($"{upgradeName} is done.")
            .SetAutoCancel(true)
            .SetOnlyAlertOnce(true) // app timer + alarm firing together won't play the sound twice
            .SetContentIntent(contentIntent)
            .Build();

        nm.Notify(notificationId, notification);
    }

    private static void EnsureChannel(Context context, NotificationManager nm)
    {
        if (nm.GetNotificationChannel(ChannelId) is not null) return;

        var channel = new NotificationChannel(ChannelId, "Finished upgrades", NotificationImportance.High)
        {
            Description = "Plays a sound when a tracked upgrade is done",
        };
        var sound = Uri.Parse($"android.resource://{context.PackageName}/{Resource.Raw.notif}");
        var attrs = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Notification)!
            .SetContentType(AudioContentType.Sonification)!
            .Build();
        channel.SetSound(sound, attrs);
        channel.EnableVibration(true);
        nm.CreateNotificationChannel(channel);
    }
}

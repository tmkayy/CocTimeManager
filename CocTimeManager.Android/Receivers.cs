using System;
using System.Linq;
using Android.App;
using Android.Content;
using CocTimeManager.Models;

namespace CocTimeManager.Android;

/// <summary>Fired by AlarmManager when a tracked upgrade finishes (app may be closed).</summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public class UpgradeAlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;
        if (!Guid.TryParse(intent.GetStringExtra(AndroidNotificationService.ExtraId), out var id)) return;

        var all = UpgradeStore.Load();
        var upgrade = all.FirstOrDefault(u => u.Id == id);
        if (upgrade is null || upgrade.Notified) return; // removed, or the app already notified

        upgrade.Notified = true;
        UpgradeStore.Save(all);
        AndroidNotificationService.Show(context, upgrade.NotificationId, upgrade.Name);
    }
}

/// <summary>Alarms don't survive a reboot: re-schedule pending upgrades and notify any that finished meanwhile.</summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted, "android.intent.action.QUICKBOOT_POWERON"])]
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null) return;
        var service = new AndroidNotificationService(context);
        var all = UpgradeStore.Load();
        var changed = false;

        foreach (var u in all.Where(u => !u.Notified))
        {
            if (u.IsDone)
            {
                u.Notified = true;
                changed = true;
                AndroidNotificationService.Show(context, u.NotificationId, u.Name);
            }
            else
            {
                service.Schedule(u);
            }
        }
        if (changed) UpgradeStore.Save(all);
    }
}

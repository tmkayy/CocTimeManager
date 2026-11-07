using System;
using System.Linq;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using CocTimeManager.Models;

namespace CocTimeManager.Android;

/// <summary>
/// Home screen widget listing running upgrades with live countdowns; Android ticks each Chronometer itself, so
/// nothing runs in the background. It's redrawn when the saved list changes, including from the alarm receiver,
/// and also offered for the lock screen on the Android versions that allow widgets there.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true, Label = "Upgrade timers")]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData("android.appwidget.provider", Resource = "@xml/timers_widget_info")]
public class TimersWidget : AppWidgetProvider
{
    // Keep in step with Resources/layout: the row's height and the root's padding plus the "+N more" line.
    private const int RowHeightDp = 42, ReservedHeightDp = 40, DefaultRows = 4;

    public override void OnUpdate(Context? context, AppWidgetManager? manager, int[]? ids)
    {
        if (context is null || manager is null || ids is null) return;
        Draw(context, manager, ids);
    }

    public override void OnAppWidgetOptionsChanged(Context? context, AppWidgetManager? manager, int appWidgetId, Bundle? newOptions)
    {
        if (context is null || manager is null) return;
        Draw(context, manager, [appWidgetId]); // resized: a different number of rows fits
    }

    /// <summary>Redraws every placed widget.</summary>
    public static void UpdateAll(Context context)
    {
        var manager = AppWidgetManager.GetInstance(context);
        var ids = manager?.GetAppWidgetIds(new ComponentName(context, Java.Lang.Class.FromType(typeof(TimersWidget))));
        if (manager is null || ids is null || ids.Length == 0) return;
        Draw(context, manager, ids);
    }

    private static void Draw(Context context, AppWidgetManager manager, int[] ids)
    {
        try
        {
            foreach (var id in ids)
            {
                var minHeight = manager.GetAppWidgetOptions(id)?.GetInt(AppWidgetManager.OptionAppwidgetMinHeight) ?? 0;
                var rows = minHeight > 0 ? Math.Max(1, (minHeight - ReservedHeightDp) / RowHeightDp) : DefaultRows;
                manager.UpdateAppWidget(id, Render(context, rows));
            }
        }
        catch (Exception)
        {
            // A display extra: never let it break saving or an alarm.
        }
    }

    private static RemoteViews Render(Context context, int maxRows)
    {
        var timers = RunningTimer.List(UpgradeStore.Load(), UpgradeStore.LoadSettings().ShowBuilderBase, DateTime.UtcNow);
        var views = new RemoteViews(context.PackageName, Resource.Layout.timers_widget);

        var openApp = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        if (openApp is not null)
            views.SetOnClickPendingIntent(Resource.Id.widget_root,
                PendingIntent.GetActivity(context, 0, openApp, PendingIntentFlags.Immutable));

        views.SetViewVisibility(Resource.Id.widget_empty, timers.Count == 0 ? ViewStates.Visible : ViewStates.Gone);
        views.RemoveAllViews(Resource.Id.widget_rows);

        // Chronometers count against the time since boot, so a reboot needs a redraw (AndroidApp does that).
        var nowUtc = DateTime.UtcNow;
        var elapsed = SystemClock.ElapsedRealtime();
        foreach (var timer in timers.Take(maxRows))
        {
            var row = new RemoteViews(context.PackageName, Resource.Layout.timers_widget_row);
            row.SetTextViewText(Resource.Id.row_name, timer.Name);
            row.SetTextViewText(Resource.Id.row_finish, timer.FinishText);
            row.SetChronometer(Resource.Id.row_countdown, elapsed + (long)(timer.FinishAt - nowUtc).TotalMilliseconds, null, true);
            row.SetChronometerCountDown(Resource.Id.row_countdown, true);
            views.AddView(Resource.Id.widget_rows, row);
        }

        var hidden = timers.Count - maxRows;
        views.SetViewVisibility(Resource.Id.widget_more, hidden > 0 ? ViewStates.Visible : ViewStates.Gone);
        if (hidden > 0) views.SetTextViewText(Resource.Id.widget_more, $"+{hidden} more");

        return views;
    }
}

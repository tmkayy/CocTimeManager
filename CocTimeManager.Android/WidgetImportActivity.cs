using System;
using Android.App;
using Android.Content;
using Android.Widget;
using CocTimeManager.Models;
using CocTimeManager.Services;

namespace CocTimeManager.Android;

/// <summary>
/// Opened by the widget's Import button. Android only lets the focused foreground app read the clipboard, so this
/// is an invisible screen that reads it once it has focus, imports, shows the result and closes. The empty task
/// affinity keeps it from bringing the main app to the front.
/// </summary>
[Activity(Theme = "@android:style/Theme.Translucent.NoTitleBar", Exported = false, NoHistory = true,
          ExcludeFromRecents = true, TaskAffinity = "")]
public class WidgetImportActivity : Activity
{
    private bool _imported;

    public override async void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (!hasFocus || _imported) return;
        _imported = true;

        var message = Import();
        // Send it to the paired PC now: this screen closes right after, and the app may not be running.
        if (PairedSync.Pair is { Listens: true } pair)
        {
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
            try
            {
                if (await System.Threading.Tasks.Task.Run(() => PairedSync.SyncNowAsync(timeout.Token)))
                    message += $" Sent to {pair.Name}.";
            }
            catch (OperationCanceledException)
            {
                // Not reachable in time: the app sends it next time it opens.
            }
        }
        Toast.MakeText(ApplicationContext, message, ToastLength.Long)?.Show();
        Finish();
    }

    private string Import()
    {
        try
        {
            var clipboard = (ClipboardManager?)GetSystemService(ClipboardService);
            var clip = clipboard?.PrimaryClip;
            var text = clip is { ItemCount: > 0 } ? clip.GetItemAt(0)?.CoerceToText(this) : null;
            if (string.IsNullOrWhiteSpace(text))
                return "The clipboard is empty. In the game use Settings → More Settings → Export village data first.";
            return VillageImport.ImportIntoStore(text);
        }
        catch (FormatException ex)
        {
            return ex.Message + " In the game use Settings → More Settings → Export village data, then try again.";
        }
        catch (Exception ex)
        {
            return "Import failed: " + ex.Message;
        }
    }
}

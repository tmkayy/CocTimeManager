using System;
using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

namespace CocTimeManager.Android;

[Activity(
    Label = "CoC Upgrade Timer",
    Theme = "@style/CocTheme",
    Icon = "@drawable/icon",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Android 13+ needs runtime permission to show notifications.
        if (OperatingSystem.IsAndroidVersionAtLeast(33) &&
            CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            RequestPermissions([Manifest.Permission.PostNotifications], 1);
        }
    }

    /// <summary>Back in front: fetch the paired PC's changes (a phone can't receive them in the background).</summary>
    protected override void OnResume()
    {
        base.OnResume();
        CocTimeManager.Services.PairedSync.SyncSoon(TimeSpan.Zero);
    }
}

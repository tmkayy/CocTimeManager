using System;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Android.Net.Wifi;
using CocTimeManager.Services;

namespace CocTimeManager.Android;

[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer) { }

    public override void OnCreate()
    {
        Notifications.Current = new AndroidNotificationService(this);
        LanSync.AcquireBroadcastLock = () =>
        {
            // Many phones drop incoming broadcast packets to save power unless an app holds this lock.
            var wifi = (WifiManager?)GetSystemService(WifiService);
            var lck = wifi?.CreateMulticastLock("CocTimeManager.sync");
            if (lck is null) return new NoLock();
            lck.SetReferenceCounted(false);
            lck.Acquire();
            return new LockRelease(lck);
        };
        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        App.Configure(base.CustomizeAppBuilder(builder));
}

internal sealed class LockRelease(WifiManager.MulticastLock lck) : IDisposable
{
    public void Dispose()
    {
        if (lck.IsHeld) lck.Release();
    }
}

internal sealed class NoLock : IDisposable
{
    public void Dispose() { }
}

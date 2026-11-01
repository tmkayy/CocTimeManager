using System;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using CocTimeManager.Services;

namespace CocTimeManager.Android;

[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer) { }

    public override void OnCreate()
    {
        Notifications.Current = new AndroidNotificationService(this);
        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        App.Configure(base.CustomizeAppBuilder(builder));
}

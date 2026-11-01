using System;
using Avalonia;
using Avalonia.Controls;
using CocTimeManager.Services;

namespace CocTimeManager.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        args = DesktopUpdateInstaller.HandleStartup(args); // after an in-app update: wait for the old version to exit
        if (SingleInstance.SignalExisting()) return; // already running: it shows its window instead

        Notifications.Current = new DesktopNotificationService();
        var autostart = new DesktopAutostart();
        autostart.RefreshIfEnabled();
        Autostart.Current = autostart;
        Updater.Installer = new DesktopUpdateInstaller();
        SingleInstance.Listen(() => App.RequestShow());

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => App.Configure(AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Matches StartupWMClass in the Linux .desktop file so the launcher shows the right icon.
            .With(new X11PlatformOptions { WmClass = "CocTimeManager" })
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace());
}

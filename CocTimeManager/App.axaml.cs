using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using CocTimeManager.Models;
using CocTimeManager.Services;
using CocTimeManager.Views;

namespace CocTimeManager;

public partial class App : Application
{
    private MainWindow? _window;
    private bool _quitting;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ApplyTheme(UpgradeStore.LoadSettings().LightMode);
    }

    public static void ApplyTheme(bool lightMode)
    {
        if (Current is { } app) app.RequestedThemeVariant = lightMode ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            SetupDesktop(desktop);
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView) // Android / iOS
            singleView.MainView = new MainView();

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Desktop: the app lives in the system tray. Closing the window only hides it, so the
    /// upgrade timer keeps running and can notify; "Quit" in the tray menu exits for real.
    /// </summary>
    private void SetupDesktop(IClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Created even when starting hidden: MainView owns the 1s timer that fires notifications.
        _window = new MainWindow();
        _window.Closing += (_, e) =>
        {
            if (_quitting || e.CloseReason is not WindowCloseReason.WindowClosing) return;
            e.Cancel = true;
            _window.Hide();
        };

        SetupTray(desktop);

        var startHidden = desktop.Args?.Contains(Autostart.MinimizedArg) == true;
        if (!startHidden) ShowWindow();
    }

    private void SetupTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var open = new NativeMenuItem("Open");
        open.Click += (_, _) => ShowWindow();

        var login = new NativeMenuItem("Start at login")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = Autostart.Current?.IsEnabled == true,
            IsVisible = Autostart.Current is not null,
        };
        login.Click += (_, _) => Autostart.Set(!(Autostart.Current?.IsEnabled ?? false));
        Autostart.Changed += () => Dispatcher.UIThread.Post(() => login.IsChecked = Autostart.Current?.IsEnabled == true);

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) =>
        {
            _quitting = true;
            desktop.Shutdown();
        };

        var tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://CocTimeManager/icons/hammer.png"))),
            ToolTipText = "CoC Upgrade Timer",
            Menu = new NativeMenu { Items = { open, login, new NativeMenuItemSeparator(), quit } },
        };
        tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, [tray]);
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    /// <summary>Called from any thread when a second launch asks this copy to show itself.</summary>
    public static void RequestShow() =>
        Dispatcher.UIThread.Post(() => (Current as App)?.ShowWindow());

    /// <summary>Shared AppBuilder setup used by every platform project.</summary>
    public static AppBuilder Configure(AppBuilder builder) => builder
        .WithInterFont()
        .With(new FontManagerOptions { DefaultFamilyName = "avares://CocTimeManager/fonts#Maple Mono" });
}

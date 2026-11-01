using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using CocTimeManager.Views;

namespace CocTimeManager;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView) // Android / iOS
            singleView.MainView = new MainView();

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Shared AppBuilder setup used by every platform project.</summary>
    public static AppBuilder Configure(AppBuilder builder) => builder
        .WithInterFont()
        .With(new FontManagerOptions { DefaultFamilyName = "avares://CocTimeManager/fonts#Maple Mono" });
}

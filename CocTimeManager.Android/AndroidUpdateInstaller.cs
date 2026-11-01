using System;
using System.IO;
using System.Threading.Tasks;
using Android.Content;
using Android.Provider;
using AndroidX.Core.Content;
using CocTimeManager.Services;
using Uri = Android.Net.Uri;

namespace CocTimeManager.Android;

/// <summary>
/// Hands the downloaded APK to Android's package installer, which always asks the user to confirm.
/// The first time, Android also asks to allow installing apps from this app.
/// The new APK is signed with the same key, so it installs over this one and keeps the data.
/// </summary>
public sealed class AndroidUpdateInstaller(Context context) : IUpdateInstaller
{
    public const string Authority = "com.tmk.coctimemanager.updates";

    /// <summary>Where downloads go: the cache-path "updates/" shared in Resources/xml/update_paths.xml.</summary>
    public static string DownloadDir(Context context) => Path.Combine(context.CacheDir!.AbsolutePath, "updates");

    public Version CurrentVersion
    {
        get
        {
            var info = context.PackageManager!.GetPackageInfo(context.PackageName!, 0);
            return Updater.ParseVersion(info?.VersionName ?? "0.0.0") ?? new Version(0, 0, 0);
        }
    }

    public string AssetSuffix => "-android.apk";

    public string DownloadDirectory => DownloadDir(context);

    public string? CannotUpdateReason => null;

    public Task InstallAsync(string downloadedFile)
    {
        // Android 8+: the user must allow this app to install apps once. Send them to that switch first.
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && !context.PackageManager!.CanRequestPackageInstalls())
        {
            var settings = new Intent(Settings.ActionManageUnknownAppSources, Uri.Parse("package:" + context.PackageName));
            settings.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(settings);
            throw new InvalidOperationException("Allow \"Install unknown apps\" for CoC Upgrade Timer, then press Update again.");
        }

        var uri = FileProvider.GetUriForFile(context, Authority, new Java.IO.File(downloadedFile));
        var install = new Intent(Intent.ActionView);
        install.SetDataAndType(uri, "application/vnd.android.package-archive");
        install.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        context.StartActivity(install);
        return Task.CompletedTask;
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Platform;
using CocTimeManager.Models;
using CocTimeManager.Services;

namespace CocTimeManager.Desktop;

/// <summary>Windows / Linux / macOS implementation with no extra NuGet packages.</summary>
public sealed class DesktopNotificationService : INotificationService
{
    private const string AppName = "CoC Upgrade Timer";
    private string? _soundPath;
    private string? _iconPath;

    public void UpgradeFinished(TrackedUpgrade upgrade)
    {
        PlaySound();
        ShowNotification("Upgrade complete!", $"{upgrade.Name} is done.");
    }

    // The desktop app only notifies while it is running (the 1s timer in MainView), nothing to schedule.
    public void Schedule(TrackedUpgrade upgrade) { }
    public void Cancel(TrackedUpgrade upgrade) { }

    // ---------- sound ----------

    public void PlaySound()
    {
        var path = _soundPath ??= ExtractAsset("snd/notif.mp3");
        if (path is null) return;

        if (OperatingSystem.IsWindows())
        {
            PlayWithMci(path);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            TryStart("afplay", path);
            return;
        }

        // Linux: use whichever player is installed.
        _ = TryStart("ffplay", "-nodisp", "-autoexit", "-loglevel", "quiet", path)
            || TryStart("mpg123", "-q", path)
            || TryStart("mpv", "--no-video", "--really-quiet", path)
            || TryStart("pw-play", path)
            || TryStart("paplay", path);
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? ret, int retLen, IntPtr hwnd);

    private static void PlayWithMci(string path)
    {
        try
        {
            mciSendString("close cocnotif", null, 0, IntPtr.Zero);
            mciSendString($"open \"{path}\" type mpegvideo alias cocnotif", null, 0, IntPtr.Zero);
            mciSendString("play cocnotif", null, 0, IntPtr.Zero);
        }
        catch (Exception)
        {
            // No sound is better than a crash.
        }
    }

    // ---------- system notification ----------

    public void ShowNotification(string title, string body)
    {
        var icon = _iconPath ??= ExtractAsset("icons/hammer.png");

        if (OperatingSystem.IsLinux())
        {
            var args = new System.Collections.Generic.List<string> { "-a", AppName, "-u", "normal" };
            if (icon is not null) args.AddRange(["-i", icon]);
            args.AddRange([title, body]);
            TryStart("notify-send", args.ToArray());
        }
        else if (OperatingSystem.IsMacOS())
        {
            TryStart("osascript", "-e",
                $"display notification \"{Escape(body)}\" with title \"{Escape(title)}\"");
        }
        else if (OperatingSystem.IsWindows())
        {
            // Toast via PowerShell / WinRT, avoids pulling in a Windows-only NuGet package.
            var xml = $"<toast><visual><binding template='ToastGeneric'><text>{Xml(title)}</text><text>{Xml(body)}</text></binding></visual><audio silent='true'/></toast>";
            var script =
                "[Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime]>$null;" +
                "[Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom.XmlDocument,ContentType=WindowsRuntime]>$null;" +
                "$x=New-Object Windows.Data.Xml.Dom.XmlDocument;" +
                $"$x.LoadXml(\"{xml}\");" +
                "$t=[Windows.UI.Notifications.ToastNotification]::new($x);" +
                // PowerShell's AppUserModelID, so the toast shows without registering our own.
                "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\WindowsPowerShell\\v1.0\\powershell.exe').Show($t)";
            TryStart("powershell", "-NoProfile", "-WindowStyle", "Hidden", "-Command", script);
        }
    }

    // ---------- helpers ----------

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string Xml(string s) => System.Security.SecurityElement.Escape(s) ?? "";

    /// <summary>Copies an embedded asset to a temp file, since external players need a real path.</summary>
    private static string? ExtractAsset(string relativePath)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "CocTimeManager");
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, Path.GetFileName(relativePath));
            using var src = AssetLoader.Open(new Uri($"avares://CocTimeManager/{relativePath}"));
            using var dst = File.Create(target);
            src.CopyTo(dst);
            return target;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TryStart(string exe, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            var p = Process.Start(psi);
            if (p is null) return false;
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) => p.Dispose();
            return true;
        }
        catch (Exception)
        {
            return false; // not installed
        }
    }
}

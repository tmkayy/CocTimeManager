using System;
using System.IO;
using System.Security;
using CocTimeManager.Services;
using Microsoft.Win32;

namespace CocTimeManager.Desktop;

/// <summary>
/// Registers the app to start at login, hidden in the tray:
/// Windows: HKCU Run key · Linux: XDG autostart .desktop file · macOS: LaunchAgent plist.
/// </summary>
public sealed class DesktopAutostart : IAutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "CocUpgradeTimer";
    private const string MacLabel = "com.tmk.coctimemanager";

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string LinuxFile => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } cfg ? cfg : Path.Combine(Home, ".config"),
        "autostart", "coc-upgrade-timer.desktop");

    private static string MacFile => Path.Combine(Home, "Library", "LaunchAgents", MacLabel + ".plist");

    private static string? ExePath => Environment.ProcessPath;

    public bool IsEnabled
    {
        get
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                    return key?.GetValue(RunValue) is not null;
                }
                if (OperatingSystem.IsMacOS()) return File.Exists(MacFile);
                if (OperatingSystem.IsLinux()) return File.Exists(LinuxFile);
            }
            catch (Exception)
            {
                // Unreadable: treat as off.
            }
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            if (enabled) Write();
            else Remove();
        }
        catch (Exception)
        {
            // Best effort: the checkbox will show the real state next time it is read.
        }
    }

    /// <summary>If enabled, rewrite the entry so it points at the current exe (the app may have been moved/updated).</summary>
    public void RefreshIfEnabled()
    {
        if (IsEnabled) SetEnabled(true);
    }

    private static void Write()
    {
        if (ExePath is not { } exe) return;

        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(RunValue, $"\"{exe}\" {Autostart.MinimizedArg}");
        }
        else if (OperatingSystem.IsMacOS())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MacFile)!);
            File.WriteAllText(MacFile, $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key><string>{MacLabel}</string>
                    <key>ProgramArguments</key>
                    <array>
                        <string>{SecurityElement.Escape(exe)}</string>
                        <string>{Autostart.MinimizedArg}</string>
                    </array>
                    <key>RunAtLoad</key><true/>
                </dict>
                </plist>
                """);
        }
        else if (OperatingSystem.IsLinux())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LinuxFile)!);
            var quoted = "\"" + exe.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`") + "\"";
            File.WriteAllText(LinuxFile, $"""
                [Desktop Entry]
                Type=Application
                Name=CoC Upgrade Timer
                Comment=Upgrade finished notifications
                Exec={quoted} {Autostart.MinimizedArg}
                Icon=coc-upgrade-timer
                Terminal=false
                X-GNOME-Autostart-enabled=true

                """);
        }
    }

    private static void Remove()
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(RunValue, throwOnMissingValue: false);
        }
        else if (OperatingSystem.IsMacOS())
        {
            File.Delete(MacFile);
        }
        else if (OperatingSystem.IsLinux())
        {
            File.Delete(LinuxFile);
        }
    }
}

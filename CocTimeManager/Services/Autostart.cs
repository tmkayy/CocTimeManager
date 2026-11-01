using System;

namespace CocTimeManager.Services;

/// <summary>"Start at login" support. Only the desktop project provides one; null elsewhere (Android).</summary>
public interface IAutostartService
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

public static class Autostart
{
    /// <summary>Command-line flag the login entry passes, so the app starts hidden in the tray.</summary>
    public const string MinimizedArg = "--minimized";

    public static IAutostartService? Current { get; set; }

    /// <summary>Raised after the setting changes, so the tray menu and the checkbox stay in sync.</summary>
    public static event Action? Changed;

    public static void Set(bool enabled)
    {
        if (Current is null) return;
        if (Current.IsEnabled != enabled) Current.SetEnabled(enabled);
        Changed?.Invoke(); // also when writing failed, so the UI snaps back to the real state
    }
}

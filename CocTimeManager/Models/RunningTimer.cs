using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CocTimeManager.Models;

/// <summary>A running upgrade as listed on the Android widget.</summary>
/// <param name="FinishAt">UTC.</param>
/// <param name="FinishText">Local finish time, e.g. "14:30", "Sat 14:30" or "16 Nov 14:30".</param>
public sealed record RunningTimer(string Name, DateTime FinishAt, string FinishText)
{
    /// <summary>Upgrades still running, soonest first; hidden Builder Base upgrades are left out.</summary>
    public static List<RunningTimer> List(IEnumerable<TrackedUpgrade> upgrades, bool showBuilderBase, DateTime nowUtc)
    {
        var localNow = nowUtc.ToLocalTime();
        return upgrades
            .Where(u => u.FinishAt > nowUtc && (showBuilderBase || !u.IsBuilderBase))
            .OrderBy(u => u.FinishAt)
            .Select(u => new RunningTimer(u.Name, u.FinishAt, FormatFinish(u.FinishAt, localNow)))
            .ToList();
    }

    /// <summary>Time alone for today, then with the weekday, then the date once the weekday would be ambiguous.</summary>
    private static string FormatFinish(DateTime finishUtc, DateTime localNow)
    {
        var culture = CultureInfo.CurrentCulture;
        var local = finishUtc.ToLocalTime();
        var days = (local.Date - localNow.Date).Days;
        var time = local.ToString("t", culture);
        return days == 0 ? time
            : days < 7 ? $"{local.ToString("ddd", culture)} {time}"
            : $"{local.ToString("d MMM", culture)} {time}";
    }
}

using System;

namespace CocTimeManager.Models;

public sealed record UpgradeResult(
    TimeSpan BaseTime,
    TimeSpan BoostedTime,
    TimeSpan RealTime,
    TimeSpan PotionUsed,
    DateTime FinishAt,
    double TotalReductionPercent);

public static class UpgradeCalculator
{
    /// <summary>A Builder Potion makes builders work this many times faster while it is active.</summary>
    public const int PotionSpeedMultiplier = 10;

    /// <param name="baseTime">Full upgrade time as shown in game, before any reductions.</param>
    /// <param name="percentReductions">Flat time reductions (Gold Pass, Hammer Jam, ...) in percent, e.g. 20 for 20%.
    /// They stack multiplicatively: 20% and 50% give 1 - 0.8 * 0.5 = 60% total.</param>
    /// <param name="potionDuration">Real time the builder potion stays active (1 potion = 1 hour).</param>
    /// <param name="now">Start time of the upgrade (local time).</param>
    public static UpgradeResult Calculate(TimeSpan baseTime, double[] percentReductions, TimeSpan potionDuration, DateTime now)
    {
        if (baseTime < TimeSpan.Zero) baseTime = TimeSpan.Zero;
        if (potionDuration < TimeSpan.Zero) potionDuration = TimeSpan.Zero;

        double factor = 1.0;
        foreach (var pct in percentReductions)
            factor *= 1.0 - Math.Clamp(pct, 0, 100) / 100.0;

        var boosted = TimeSpan.FromTicks((long)(baseTime.Ticks * factor));

        // While the potion is active, every real second counts as 10 seconds of work.
        var workDuringPotion = potionDuration * PotionSpeedMultiplier;
        TimeSpan real, potionUsed;
        if (boosted <= workDuringPotion)
        {
            real = boosted / PotionSpeedMultiplier;
            potionUsed = real;
        }
        else
        {
            real = boosted - workDuringPotion + potionDuration;
            potionUsed = potionDuration;
        }

        // Round up to the next whole second, the game never finishes early.
        real = TimeSpan.FromSeconds(Math.Ceiling(real.TotalSeconds));

        double totalReduction = baseTime.Ticks == 0 ? 0 : (1.0 - (double)real.Ticks / baseTime.Ticks) * 100.0;

        return new UpgradeResult(baseTime, boosted, real, potionUsed, now + real, totalReduction);
    }

    public static string Format(TimeSpan t)
    {
        if (t <= TimeSpan.Zero) return "0s";
        var parts = new System.Collections.Generic.List<string>(4);
        if (t.Days > 0) parts.Add($"{t.Days}d");
        if (t.Hours > 0) parts.Add($"{t.Hours}h");
        if (t.Minutes > 0) parts.Add($"{t.Minutes}m");
        if (t.Seconds > 0 && t.Days == 0) parts.Add($"{t.Seconds}s");
        return parts.Count == 0 ? "0s" : string.Join(" ", parts);
    }
}

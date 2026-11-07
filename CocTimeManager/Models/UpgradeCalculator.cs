using System;
using System.Collections.Generic;
using System.Linq;

namespace CocTimeManager.Models;

public sealed record UpgradeResult(
    TimeSpan BaseTime,
    TimeSpan BoostedTime,
    TimeSpan RealTime,
    TimeSpan PotionUsed,
    TimeSpan BiteUsed,
    TimeSpan PotionSaved,
    TimeSpan BiteSaved,
    DateTime FinishAt,
    double TotalReductionPercent);

public static class UpgradeCalculator
{
    /// <summary>A Builder Potion makes builders work this many times faster while it is active.</summary>
    public const int PotionSpeedMultiplier = 10;

    /// <summary>A Builder Bite makes builders work this many times faster while it is active.</summary>
    public const int BiteSpeedMultiplier = 2;

    /// <summary>A Research Potion makes the lab work this many times faster while it is active.</summary>
    public const int ResearchPotionSpeedMultiplier = 24;

    /// <summary>
    /// Real time to get through <paramref name="work"/> (time left at normal speed) when boosts, timed from now,
    /// add to the normal 1x speed between Start and End. Rates add up: a 10x potion (+9) and a helper adding 8 give 18x.
    /// </summary>
    public static TimeSpan TimeToFinish(TimeSpan work, IReadOnlyCollection<(TimeSpan Start, TimeSpan End, int ExtraSpeed)> boosts)
    {
        var elapsed = TimeSpan.Zero;
        var remaining = work;
        var changes = boosts.SelectMany(b => new[] { b.Start, b.End }).Where(t => t > TimeSpan.Zero).Distinct().Order();
        foreach (var next in changes)
        {
            var speed = 1 + boosts.Where(b => b.Start <= elapsed && b.End > elapsed).Sum(b => b.ExtraSpeed);
            var used = TimeSpan.FromTicks(Math.Min((next - elapsed).Ticks, remaining.Ticks / speed));
            remaining -= used * speed;
            elapsed += used;
        }
        // Round up to a whole second, the game never finishes early.
        return TimeSpan.FromSeconds(Math.Ceiling((elapsed + remaining).TotalSeconds));
    }

    /// <param name="baseTime">Full upgrade time as shown in game, before any reductions.</param>
    /// <param name="percentReductions">Flat time reductions (Gold Pass, Hammer Jam, ...) in percent, e.g. 20 for 20%.
    /// They stack multiplicatively: 20% and 50% give 1 - 0.8 * 0.5 = 60% total.</param>
    /// <param name="potionDuration">Real time the builder potion stays active (1 potion = 1 hour).</param>
    /// <param name="biteDuration">Real time Builder Bites stay active (1 bite = 1 hour).
    /// Since the Feb 2026 update bites don't stack with a potion (no extra speed while one is active), so
    /// they are assumed to be used right after the potion ends, as the game recommends.</param>
    /// <param name="now">Start time of the upgrade (UTC; FinishAt comes back in the same kind).</param>
    public static UpgradeResult Calculate(TimeSpan baseTime, double[] percentReductions, TimeSpan potionDuration,
                                          TimeSpan biteDuration, DateTime now)
    {
        if (baseTime < TimeSpan.Zero) baseTime = TimeSpan.Zero;
        if (potionDuration < TimeSpan.Zero) potionDuration = TimeSpan.Zero;
        if (biteDuration < TimeSpan.Zero) biteDuration = TimeSpan.Zero;

        // Percentage reductions shrink the amount of work...
        double factor = 1.0;
        foreach (var pct in percentReductions)
            factor *= 1.0 - Math.Clamp(pct, 0, 100) / 100.0;

        var boosted = TimeSpan.FromTicks((long)(baseTime.Ticks * factor));

        // ...then speed boosts change how fast that work gets done, one after the other:
        // potion (10x), then bites (2x), then normal speed.
        var remaining = boosted;
        var potionUsed = Run(ref remaining, potionDuration, PotionSpeedMultiplier);
        var biteUsed = Run(ref remaining, biteDuration, BiteSpeedMultiplier);
        var real = potionUsed + biteUsed + remaining;

        // Round up to the next whole second, the game never finishes early.
        real = TimeSpan.FromSeconds(Math.Ceiling(real.TotalSeconds));

        double totalReduction = baseTime.Ticks == 0 ? 0 : (1.0 - (double)real.Ticks / baseTime.Ticks) * 100.0;

        return new UpgradeResult(baseTime, boosted, real, potionUsed, biteUsed,
            potionUsed * (PotionSpeedMultiplier - 1), biteUsed * (BiteSpeedMultiplier - 1),
            now + real, totalReduction);
    }

    /// <summary>Works at <paramref name="speed"/>x for up to <paramref name="duration"/>;
    /// returns the real time used and takes the work done off <paramref name="remainingWork"/>.</summary>
    private static TimeSpan Run(ref TimeSpan remainingWork, TimeSpan duration, int speed)
    {
        var used = TimeSpan.FromTicks(Math.Min(duration.Ticks, remainingWork.Ticks / speed));
        remainingWork -= used * speed;
        if (remainingWork < TimeSpan.Zero) remainingWork = TimeSpan.Zero;
        return used;
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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CocTimeManager.Models;
using CocTimeManager.Services;

namespace CocTimeManager.Views;

public partial class MainView : UserControl
{
    /// <summary>Below this width the inputs and results stack in one column (phones).</summary>
    private const double NarrowWidth = 760;

    private readonly ObservableCollection<TrackedUpgrade> _tracked = new(UpgradeStore.Load());
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private UpgradeResult? _lastResult;

    public MainView()
    {
        InitializeComponent();

        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);

        // Touch screens have no hover: tapping an "i" badge toggles its tooltip.
        foreach (var info in this.GetLogicalDescendants().OfType<Border>().Where(b => b.Classes.Contains("info")))
            info.Tapped += (_, _) => ToolTip.SetIsOpen(info, !ToolTip.GetIsOpen(info));

        TrackedList.ItemsSource = _tracked;

        foreach (var input in new[] { DaysInput, HoursInput, MinutesInput, PotionHoursInput, PotionMinutesInput,
                                      BiteHoursInput, BiteMinutesInput,
                                      GoldPassPct, JamPct, InactivityPct })
            input.ValueChanged += (_, _) => Recalculate();

        foreach (var check in new[] { PotionCheck, BiteCheck, GoldPassCheck, JamCheck, InactivityCheck })
            check.IsCheckedChanged += (_, _) => Recalculate();

        TrackButton.Click += TrackButton_Click;

        if (Autostart.Current is { } autostart)
        {
            AutostartCard.IsVisible = true;
            AutostartCheck.IsChecked = autostart.IsEnabled;
            AutostartCheck.IsCheckedChanged += (_, _) => Autostart.Set(AutostartCheck.IsChecked == true);
            // Tray menu toggled it, or writing the entry failed: show the real state.
            Autostart.Changed += () => Dispatcher.UIThread.Post(() => AutostartCheck.IsChecked = autostart.IsEnabled);
        }

        _timer.Tick += (_, _) =>
        {
            Recalculate(); // keeps "now" and "done at" current
            foreach (var t in _tracked) t.Refresh();
            CheckFinished();
        };
        _timer.Start();

        Recalculate();
        CheckFinished(); // upgrades that finished while the app was closed
    }

    private bool? _narrow;

    private void ApplyLayout(double width)
    {
        var narrow = width < NarrowWidth;
        if (narrow == _narrow) return;
        _narrow = narrow;

        if (narrow)
        {
            RootGrid.ColumnDefinitions = new ColumnDefinitions("*");
            RootGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
            Grid.SetColumn(ResultsPanel, 0);
            Grid.SetRow(ResultsPanel, 1);
        }
        else
        {
            RootGrid.ColumnDefinitions = new ColumnDefinitions("360,*");
            RootGrid.RowDefinitions = new RowDefinitions("Auto");
            Grid.SetColumn(ResultsPanel, 1);
            Grid.SetRow(ResultsPanel, 0);
        }
    }

    private void CheckFinished()
    {
        var due = _tracked.Where(t => t.IsDone && !t.Notified).ToList();
        if (due.Count == 0) return;

        // On Android a background alarm may already have notified and saved Notified=true.
        var onDisk = UpgradeStore.Load().Where(u => u.Notified).Select(u => u.Id).ToHashSet();
        foreach (var t in due)
        {
            var alreadyNotified = onDisk.Contains(t.Id);
            t.Notified = true;
            if (!alreadyNotified) Notifications.Current.UpgradeFinished(t);
        }
        UpgradeStore.Save(_tracked);
    }

    private static double Val(NumericUpDown n) => (double)(n.Value ?? 0);

    private void Recalculate()
    {
        var culture = CultureInfo.CurrentCulture;
        var now = DateTime.UtcNow;
        var localNow = now.ToLocalTime();

        var baseTime = TimeSpan.FromDays(Val(DaysInput)) + TimeSpan.FromHours(Val(HoursInput)) +
                       TimeSpan.FromMinutes(Val(MinutesInput));

        var reductions = new List<double>();
        var labels = new List<string>();
        void AddIf(CheckBox box, NumericUpDown pct, string name)
        {
            if (box.IsChecked != true) return;
            reductions.Add(Val(pct));
            labels.Add($"{name} -{Val(pct):0.#}%");
        }
        AddIf(GoldPassCheck, GoldPassPct, "Gold Pass");
        AddIf(JamCheck, JamPct, "Hammer/Summer Jam");
        AddIf(InactivityCheck, InactivityPct, "Inactivity Boost");

        var potion = PotionCheck.IsChecked == true
            ? TimeSpan.FromHours(Val(PotionHoursInput)) + TimeSpan.FromMinutes(Val(PotionMinutesInput))
            : TimeSpan.Zero;

        var bites = BiteCheck.IsChecked == true
            ? TimeSpan.FromHours(Val(BiteHoursInput)) + TimeSpan.FromMinutes(Val(BiteMinutesInput))
            : TimeSpan.Zero;

        var r = UpgradeCalculator.Calculate(baseTime, reductions.ToArray(), potion, bites, now);
        _lastResult = r;

        var zone = TimeZoneInfo.Local.IsDaylightSavingTime(localNow) ? TimeZoneInfo.Local.DaylightName : TimeZoneInfo.Local.StandardName;
        NowText.Text = $"{localNow.ToString("F", culture)}  ({zone})";

        if (baseTime == TimeSpan.Zero)
        {
            FinishText.Text = "Enter an upgrade time";
            DurationText.Text = "–";
            ReductionText.Text = "0%";
            ToolTip.SetTip(BreakdownInfo, "Enter an upgrade time");
            TrackButton.IsEnabled = false;
            return;
        }

        TrackButton.IsEnabled = true;
        FinishText.Text = r.FinishAt.ToLocalTime().ToString("f", culture);
        DurationText.Text = UpgradeCalculator.Format(r.RealTime);
        ReductionText.Text = r.TotalReductionPercent.ToString("0.##", culture) + "%";

        var lines = new List<string> { $"Base time: {UpgradeCalculator.Format(r.BaseTime)}" };
        foreach (var l in labels) lines.Add(l);
        if (labels.Count > 0)
            lines.Add($"After reductions: {UpgradeCalculator.Format(r.BoostedTime)}");
        if (r.PotionUsed > TimeSpan.Zero)
        {
            lines.Add($"Potion (10x) active for {UpgradeCalculator.Format(r.PotionUsed)} (saves {UpgradeCalculator.Format(r.PotionSaved)})");
            if (r.PotionUsed < potion)
                lines.Add($"Upgrade finishes before the potion ends: {UpgradeCalculator.Format(potion - r.PotionUsed)} wasted");
        }
        if (bites > TimeSpan.Zero)
        {
            if (r.BiteUsed > TimeSpan.Zero)
                lines.Add($"Bites (2x) active for {UpgradeCalculator.Format(r.BiteUsed)}{(potion > TimeSpan.Zero ? " after the potion" : "")} (saves {UpgradeCalculator.Format(r.BiteSaved)})");
            if (r.BiteUsed < bites)
                lines.Add($"Upgrade finishes before the bites run out: {UpgradeCalculator.Format(bites - r.BiteUsed)} wasted");
        }
        lines.Add($"Real time: {UpgradeCalculator.Format(r.RealTime)}");
        ToolTip.SetTip(BreakdownInfo, string.Join(Environment.NewLine, lines));
    }

    private void TrackButton_Click(object? sender, RoutedEventArgs e)
    {
        Recalculate();
        if (_lastResult is null || _lastResult.BaseTime == TimeSpan.Zero) return;

        var name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Upgrade" : NameBox.Text.Trim();
        var upgrade = new TrackedUpgrade { Name = name, StartedAt = DateTime.UtcNow, FinishAt = _lastResult.FinishAt };
        _tracked.Add(upgrade);
        UpgradeStore.Save(_tracked);
        Notifications.Current.Schedule(upgrade);
        NameBox.Text = "";
    }

    private void RemoveTracked_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TrackedUpgrade item })
        {
            _tracked.Remove(item);
            UpgradeStore.Save(_tracked);
            Notifications.Current.Cancel(item);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
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

        var settings = UpgradeStore.LoadSettings();
        BuilderBaseCheck.IsChecked = settings.ShowBuilderBase;
        BuilderBaseCheck.IsCheckedChanged += (_, _) =>
        {
            var s = UpgradeStore.LoadSettings();
            s.ShowBuilderBase = BuilderBaseCheck.IsChecked == true;
            UpgradeStore.SaveSettings(s);
            UpdateTrackedList();
        };
        LightModeCheck.IsChecked = settings.LightMode;
        LightModeCheck.IsCheckedChanged += (_, _) =>
        {
            var s = UpgradeStore.LoadSettings();
            s.LightMode = LightModeCheck.IsChecked == true;
            UpgradeStore.SaveSettings(s);
            App.ApplyTheme(s.LightMode);
        };
        _tracked.CollectionChanged += (_, _) => UpdateTrackedList();
        VillageImport.ImportedElsewhere += message => Dispatcher.UIThread.Post(() =>
        {
            _tracked.Clear();
            foreach (var u in UpgradeStore.Load()) _tracked.Add(u);
            ShowImportStatus(message);
        });
        UpdateTrackedList();

        foreach (var input in new[] { DaysInput, HoursInput, MinutesInput, PotionHoursInput, PotionMinutesInput,
                                      BiteHoursInput, BiteMinutesInput,
                                      GoldPassPct, JamPct, InactivityPct })
            input.ValueChanged += (_, _) => Recalculate();

        foreach (var check in new[] { PotionCheck, BiteCheck, GoldPassCheck, JamCheck, InactivityCheck })
            check.IsCheckedChanged += (_, _) => Recalculate();

        TrackButton.Click += TrackButton_Click;
        ImportButton.Click += ImportButton_Click;
        UpdateButton.Click += UpdateButton_Click;
        CheckUpdatesButton.Click += (_, _) => _ = CheckForUpdatesAsync(userAsked: true);
        SetupUpdates();
        CopySyncButton.Click += CopySync_Click;
        PasteSyncButton.Click += PasteSync_Click;
        ShowSyncCodeButton.Click += ShowSyncCode_Click;
        EnterSyncCodeButton.Click += EnterSyncCode_Click;
        JoinSyncButton.Click += JoinSync_Click;
        CancelSyncButton.Click += (_, _) => CancelSync();
        CancelJoinButton.Click += (_, _) => CancelSync();
        SyncCodeInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) JoinSync_Click(null, e); };

        if (Autostart.Current is { } autostart)
        {
            AutostartRow.IsVisible = true;
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

    /// <summary>Hidden upgrades stay in <see cref="_tracked"/> so imports and device sync still see them.</summary>
    private void UpdateTrackedList() =>
        TrackedList.ItemsSource = BuilderBaseCheck.IsChecked == true
            ? _tracked
            : _tracked.Where(t => !t.IsBuilderBase).ToList();

    private bool? _narrow;

    private void ApplyLayout(double width)
    {
        var narrow = width < NarrowWidth;
        if (narrow == _narrow) return;
        _narrow = narrow;

        ManualExpander.IsVisible = narrow;
        if (narrow)
        {
            // Phones: the tracked upgrades first, with everything for adding one by hand folded away below them.
            RootGrid.ColumnDefinitions = new ColumnDefinitions("*");
            RootGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
            Grid.SetColumn(ResultsPanel, 0);
            Grid.SetRow(ResultsPanel, 0);
            Grid.SetRow(InputsPanel, 1);

            MoveTo(UpdateBanner, ResultsPanel, 0);
            MoveTo(ManualPanel, ManualHost, 0);
            MoveTo(ResultPanel, ManualHost, 1);
            MoveTo(TrackButton, ManualHost, 2);
            TrackButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            TrackButton.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        }
        else
        {
            RootGrid.ColumnDefinitions = new ColumnDefinitions("360,*");
            RootGrid.RowDefinitions = new RowDefinitions("Auto");
            Grid.SetColumn(ResultsPanel, 1);
            Grid.SetRow(ResultsPanel, 0);
            Grid.SetRow(InputsPanel, 0);

            MoveTo(UpdateBanner, InputsPanel, 0);
            MoveTo(ManualPanel, InputsPanel, InputsPanel.Children.IndexOf(ManualExpander) + 1);
            MoveTo(ResultPanel, ResultsPanel, 0);
            MoveTo(TrackButton, TrackerHeader, TrackerHeader.Children.Count);
            TrackButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        }
    }

    private static void MoveTo(Control control, Panel target, int index)
    {
        if (control.Parent == target) return;
        (control.Parent as Panel)?.Children.Remove(control);
        target.Children.Insert(index, control);
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
            if (!alreadyNotified && UpgradeStore.ShouldNotify(t)) Notifications.Current.UpgradeFinished(t);
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
        var upgrade = new TrackedUpgrade
        {
            Name = name, StartedAt = DateTime.UtcNow, FinishAt = _lastResult.FinishAt, UpdatedAt = DateTime.UtcNow,
        };
        _tracked.Add(upgrade);
        UpgradeStore.Save(_tracked);
        Notifications.Current.Schedule(upgrade);
        NameBox.Text = "";
        ManualExpander.IsExpanded = false; // phones: back to the list, where the new upgrade now is
    }

    private async void ImportButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            var text = clipboard is null ? null : await clipboard.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowImportStatus("The clipboard is empty. In the game use Settings → More Settings → Export village data first.");
                return;
            }
            ShowImportStatus(ApplyImport(VillageImport.Parse(text)));
        }
        catch (FormatException ex)
        {
            ShowImportStatus(ex.Message + " In the game use Settings → More Settings → Export village data, then try again.");
        }
        catch (Exception ex)
        {
            ShowImportStatus("Import failed: " + ex.Message);
        }
    }

    private void ShowImportStatus(string message)
    {
        ImportStatus.Text = message;
        ImportStatus.IsVisible = true;
    }

    private string ApplyImport(VillageExport export) => VillageImport.Apply(_tracked, export);

    private void RemoveTracked_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TrackedUpgrade item })
        {
            _tracked.Remove(item);
            UpgradeStore.Save(_tracked);
            Notifications.Current.Cancel(item);

            // Remember the deletion so syncing with another device doesn't bring it back.
            var deleted = UpgradeStore.LoadDeleted();
            deleted[item.Id] = DateTime.UtcNow;
            UpgradeStore.SaveDeleted(deleted);
        }
    }

    // ---------- device sync (copy/paste code) ----------

    private async void CopySync_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) throw new InvalidOperationException("no clipboard available");
            var code = SyncCode.Create(_tracked, UpgradeStore.LoadDeleted());
            await clipboard.SetTextAsync(code);
            ShowImportStatus($"Sync code copied ({code.Length} characters, {_tracked.Count} upgrade{(_tracked.Count == 1 ? "" : "s")}). " +
                             "Send it to your other device and press Paste code there.");
        }
        catch (Exception ex)
        {
            ShowImportStatus("Couldn't copy the sync code: " + ex.Message);
        }
    }

    private async void PasteSync_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            var text = clipboard is null ? null : await clipboard.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowImportStatus("The clipboard is empty. Press Copy code on your other device and send the code here first.");
                return;
            }
            ShowImportStatus(ApplySync(SyncCode.Parse(text)));
        }
        catch (FormatException ex)
        {
            ShowImportStatus(ex.Message);
        }
        catch (Exception ex)
        {
            ShowImportStatus("Sync failed: " + ex.Message);
        }
    }

    /// <summary>Merges another device's list into ours and returns a status message.</summary>
    private string ApplySync(SyncPayload incoming)
    {
        var r = SyncCode.Merge(_tracked.ToList(), UpgradeStore.LoadDeleted(), incoming, DateTime.UtcNow);

        // Swap in the merged list; re-schedule so Android alarms match it (desktop ignores these).
        foreach (var old in _tracked) Notifications.Current.Cancel(old);
        _tracked.Clear();
        foreach (var u in r.Upgrades.OrderBy(u => u.FinishAt))
        {
            _tracked.Add(u);
            if (!u.Notified) Notifications.Current.Schedule(u);
        }
        UpgradeStore.Save(_tracked);
        UpgradeStore.SaveDeleted(r.Deleted);
        CheckFinished();

        return r.Added + r.Updated + r.Removed == 0
            ? "Already in sync, nothing changed."
            : $"Synced: {r.Added} added, {r.Updated} updated, {r.Removed} removed." +
              (r.ImportsReplaced ? " Used the other device's newer village import." : "");
    }

    // ---------- Wi-Fi sync with a 4-digit code ----------

    private CancellationTokenSource? _syncCts;

    private enum SyncUi { Idle, Hosting, Joining }

    private void SetSyncUi(SyncUi state)
    {
        SyncIdlePanel.IsVisible = state == SyncUi.Idle;
        SyncHostPanel.IsVisible = state == SyncUi.Hosting;
        SyncJoinPanel.IsVisible = state == SyncUi.Joining;
    }

    private void CancelSync()
    {
        _syncCts?.Cancel();
        _syncCts = null;
        SetSyncUi(SyncUi.Idle);
    }

    private async void ShowSyncCode_Click(object? sender, RoutedEventArgs e)
    {
        CancelSync();
        var cts = _syncCts = new CancellationTokenSource();
        var code = LanSync.NewCode();
        SyncCodeText.Text = code;
        SetSyncUi(SyncUi.Hosting);

        // Countdown under the code.
        var until = DateTime.UtcNow + LanSync.HostTimeout;
        var countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void Tick()
        {
            var left = until - DateTime.UtcNow;
            SyncHostHint.Text = $"Enter it on your other device\nSame Wi-Fi · {Math.Max(0, (int)left.TotalMinutes)}:{Math.Max(0, left.Seconds):00} left";
        }
        countdown.Tick += (_, _) => Tick();
        Tick();
        countdown.Start();

        try
        {
            await LanSync.HostAsync(code, theirCode =>
            {
                // Runs on a worker thread: merge on the UI thread, then send back our merged list.
                return Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ShowImportStatus(ApplySync(SyncCode.Parse(theirCode)));
                    return SyncCode.Create(_tracked, UpgradeStore.LoadDeleted());
                }).GetTask();
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user.
        }
        catch (Exception ex) when (ex is LanSyncException or FormatException)
        {
            if (!cts.IsCancellationRequested) ShowImportStatus(ex.Message);
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) ShowImportStatus("Sync failed: " + ex.Message);
        }
        finally
        {
            countdown.Stop();
            if (_syncCts == cts) CancelSync();
        }
    }

    private void EnterSyncCode_Click(object? sender, RoutedEventArgs e)
    {
        CancelSync();
        SyncCodeInput.Text = "";
        SetSyncUi(SyncUi.Joining);
        SyncCodeInput.Focus();
    }

    private async void JoinSync_Click(object? sender, RoutedEventArgs e)
    {
        var code = new string((SyncCodeInput.Text ?? "").Where(char.IsDigit).ToArray());
        if (code.Length != 4)
        {
            ShowImportStatus("Enter the 4-digit code shown on your other device.");
            return;
        }

        _syncCts?.Cancel();
        var cts = _syncCts = new CancellationTokenSource();
        JoinSyncButton.IsEnabled = SyncCodeInput.IsEnabled = false;
        ShowImportStatus($"Looking for the device showing {code}…");
        try
        {
            var ours = SyncCode.Create(_tracked, UpgradeStore.LoadDeleted());
            var theirs = await Task.Run(() => LanSync.JoinAsync(code, ours, cts.Token));
            ShowImportStatus(ApplySync(SyncCode.Parse(theirs)));
            if (_syncCts == cts) CancelSync();
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user.
        }
        catch (Exception ex) when (ex is LanSyncException or FormatException)
        {
            if (!cts.IsCancellationRequested) ShowImportStatus(ex.Message);
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) ShowImportStatus("Sync failed: " + ex.Message);
        }
        finally
        {
            JoinSyncButton.IsEnabled = SyncCodeInput.IsEnabled = true;
        }
    }

    // ---------- in-app updates ----------

    private UpdateInfo? _update;

    private void SetupUpdates()
    {
        var installer = Updater.Installer;
        VersionText.Text = installer is null ? "Version unknown" : $"Version {installer.CurrentVersion}";
        CheckUpdatesButton.IsVisible = installer is not null;
        if (installer is null) return;

        // Quiet check shortly after startup; offline or rate-limited just means no banner.
        DispatcherTimer.RunOnce(() => _ = CheckForUpdatesAsync(userAsked: false), TimeSpan.FromSeconds(3));
    }

    private void ShowCheckStatus(string text)
    {
        UpdateCheckStatus.Text = text;
        UpdateCheckStatus.IsVisible = true;
    }

    private async Task CheckForUpdatesAsync(bool userAsked)
    {
        if (Updater.Installer is not { } installer) return;
        CheckUpdatesButton.IsEnabled = false;
        if (userAsked) ShowCheckStatus("Checking…");
        try
        {
            _update = await Task.Run(() => Updater.CheckAsync(installer.CurrentVersion, installer.AssetSuffix));
            if (_update is null)
            {
                if (userAsked) ShowCheckStatus("You're on the latest version.");
                return;
            }

            UpdateTitle.Text = $"Version {_update.Version} is available";
            var reason = installer.CannotUpdateReason;
            UpdateStatus.Text = reason ?? $"You have {installer.CurrentVersion}. Hover for what's new.";
            UpdateButton.IsVisible = reason is null;
            UpdateButton.IsEnabled = true;
            ToolTip.SetTip(UpdateBanner, Shorten(_update.Notes));
            UpdateBanner.IsVisible = true;
            if (userAsked) ShowCheckStatus($"Version {_update.Version} is available, see the banner at the top.");
        }
        catch (Exception ex)
        {
            if (userAsked) ShowCheckStatus("Couldn't check for updates: " + ex.Message);
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private static string Shorten(string notes)
    {
        var text = notes.Replace("\r", "").Trim();
        var cut = text.IndexOf("## Downloads", StringComparison.Ordinal); // just the "what's new" part
        if (cut > 0) text = text[..cut].Trim();
        return text.Length > 700 ? text[..700] + "…" : text;
    }

    private async void UpdateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_update is null || Updater.Installer is not { } installer) return;
        UpdateButton.IsEnabled = false;
        try
        {
            var progress = new Progress<double>(p => UpdateStatus.Text = $"Downloading… {p:P0}");
            UpdateStatus.Text = "Downloading…";
            var update = _update;
            var file = await Task.Run(() => Updater.DownloadAsync(update, installer.DownloadDirectory, progress));
            UpdateStatus.Text = OperatingSystem.IsAndroid() ? "Opening the installer…" : "Installing and restarting…";
            UpgradeStore.Save(_tracked); // desktop restarts right away
            await installer.InstallAsync(file);
            UpdateButton.IsEnabled = true; // Android: back here if the installer was dismissed
        }
        catch (Exception ex)
        {
            UpdateStatus.Text = ex.Message;
            UpdateButton.IsEnabled = true;
        }
    }
}

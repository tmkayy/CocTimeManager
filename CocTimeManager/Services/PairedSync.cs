using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CocTimeManager.Models;

namespace CocTimeManager.Services;

/// <summary>
/// Keeps two paired devices in sync on the same Wi-Fi (protocol in LanSync.Paired.cs).
/// <list type="bullet">
/// <item>A device that can listen (desktop) answers syncs while the app runs, even hidden in the tray.</item>
/// <item>Any paired device sends its list after each change, if the other one listens, and right away when the app
/// starts or comes back to the front, which is how a phone picks up the PC's changes.</item>
/// <item>Merges use <see cref="SyncCode.Merge"/>, like a code sync. Saving a merge doesn't send it back again.</item>
/// </list>
/// </summary>
public static class PairedSync
{
    /// <summary>Shown to the other device when pairing. Android sets the phone's model.</summary>
    public static string DeviceName { get; set; } = Environment.MachineName;

    /// <summary>Whether this device answers syncs. Phones can't listen while the app is in the background.</summary>
    public static bool CanListen { get; set; } = !OperatingSystem.IsAndroid();

    /// <summary>Merges into the list the app is showing (set by MainView) and returns our sync code.
    /// Null: merge straight into the saved list, e.g. from the Android widget without the app open.</summary>
    public static Func<SyncPayload, Task<string>>? MergeIntoApp { get; set; }

    /// <summary>A short line for the Sync section, from any thread.</summary>
    public static event Action<string>? StatusChanged;

    /// <summary>Raised when the pairing changes (paired, re-paired or forgotten).</summary>
    public static event Action? PairChanged;

    public static string? LastStatus { get; private set; }

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly object Lock = new();
    private static int _applying;
    private static bool _started;
    private static CancellationTokenSource? _debounce, _serverCts;
    private static Task? _server;

    public static PairInfo? Pair => UpgradeStore.LoadSettings().Pair;

    public static string OwnId
    {
        get
        {
            var s = UpgradeStore.LoadSettings();
            if (s.DeviceId.Length > 0) return s.DeviceId;
            s.DeviceId = Guid.NewGuid().ToString("N");
            UpgradeStore.SaveSettings(s);
            return s.DeviceId;
        }
    }

    public static PairingSide Side => new(OwnId, DeviceName, CanListen);

    /// <summary>Call once the app is up: starts listening (if this device can) and syncs once.</summary>
    public static void Start()
    {
        lock (Lock)
        {
            if (_started) return;
            _started = true;
        }
        UpgradeStore.UpgradesSaved += () =>
        {
            if (Volatile.Read(ref _applying) == 0) SyncSoon(TimeSpan.FromSeconds(1.5));
        };
        StartServer();
        SyncSoon(TimeSpan.Zero);
    }

    public static void SavePair(PairInfo pair)
    {
        var s = UpgradeStore.LoadSettings();
        s.Pair = pair;
        UpgradeStore.SaveSettings(s);
        SetStatus($"Paired with {pair.Name}.");
        PairChanged?.Invoke();
        StartServer();
    }

    public static async Task ForgetAsync()
    {
        var s = UpgradeStore.LoadSettings();
        s.Pair = null;
        UpgradeStore.SaveSettings(s);
        await StopServerAsync();
        SetStatus("");
        PairChanged?.Invoke();
    }

    // ---------- listening ----------

    public static void StartServer()
    {
        if (!CanListen || Pair is null) return;
        lock (Lock)
        {
            if (_server is { IsCompleted: false }) return;
            var cts = _serverCts = new CancellationTokenSource();
            _server = Task.Run(() => ServeLoopAsync(cts.Token));
        }
    }

    /// <summary>Stops listening and waits until the port is free (a code sync on this device needs it).</summary>
    public static async Task StopServerAsync()
    {
        Task? server;
        lock (Lock)
        {
            _serverCts?.Cancel();
            server = _server;
            _server = null;
        }
        if (server is not null)
        {
            try { await server; }
            catch (Exception) { /* already reported */ }
        }
    }

    private static async Task ServeLoopAsync(CancellationToken ct)
    {
        // Right after pairing as the host, the code sync may still be letting go of the port: retry quickly,
        // and only report it if it keeps failing.
        var failures = 0;
        while (!ct.IsCancellationRequested && Pair is not null)
        {
            try
            {
                await LanSync.ServePairedAsync(OwnId, () => Pair, ApplyAsync, ct);
                failures = 0;
            }
            catch (LanSyncException ex)
            {
                if (++failures == 3) SetStatus(ex.Message);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                failures++;
            }
            try { await Task.Delay(TimeSpan.FromSeconds(failures < 3 ? 2 : 30), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    // ---------- sending ----------

    /// <summary>Syncs after <paramref name="delay"/>; another call before then restarts the wait.</summary>
    public static void SyncSoon(TimeSpan delay)
    {
        var p = Pair;
        if (p is null || !p.Listens) return;
        CancellationTokenSource cts;
        lock (Lock)
        {
            _debounce?.Cancel();
            cts = _debounce = new CancellationTokenSource();
        }
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(delay, cts.Token); }
            catch (OperationCanceledException) { return; }
            await SyncNowAsync();
        });
    }

    /// <summary>Sends our list to the paired device and merges its reply. False if not paired, the other device
    /// doesn't listen, or it couldn't be reached (the reason goes to <see cref="StatusChanged"/>).</summary>
    public static async Task<bool> SyncNowAsync(CancellationToken ct = default)
    {
        var pair = Pair;
        if (pair is null || !pair.Listens) return false;

        await Gate.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var ours = SyncCode.Create(UpgradeStore.Load(), UpgradeStore.LoadDeleted());
            var (reply, address) = await LanSync.ExchangePairedAsync(pair, OwnId, ours, timeout.Token);
            await ApplyAsync(SyncCode.Parse(reply));

            if (address.ToString() != pair.LastAddress)
            {
                var s = UpgradeStore.LoadSettings();
                if (s.Pair?.Id == pair.Id)
                {
                    s.Pair.LastAddress = address.ToString();
                    UpgradeStore.SaveSettings(s);
                }
            }
            SetStatus($"Synced with {pair.Name} at {DateTime.Now:t}.");
            return true;
        }
        catch (LanSyncException ex)
        {
            SetStatus(ex.Message);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            SetStatus($"Couldn't sync with {pair.Name}: {ex.Message}");
        }
        finally
        {
            Gate.Release();
        }
        return false;
    }

    // ---------- merging ----------

    /// <summary>Merges the other device's list and returns ours. Saves made here don't trigger another sync.</summary>
    private static async Task<string> ApplyAsync(SyncPayload incoming)
    {
        Interlocked.Increment(ref _applying);
        try
        {
            return MergeIntoApp is { } app ? await app(incoming) : MergeIntoStore(incoming);
        }
        finally
        {
            Interlocked.Decrement(ref _applying);
        }
    }

    private static string MergeIntoStore(SyncPayload incoming)
    {
        var local = UpgradeStore.Load();
        var r = SyncCode.Merge(local, UpgradeStore.LoadDeleted(), incoming, DateTime.UtcNow);
        foreach (var old in local) Notifications.Current.Cancel(old);
        foreach (var u in r.Upgrades.Where(u => !u.Notified)) Notifications.Current.Schedule(u);
        UpgradeStore.Save(r.Upgrades.OrderBy(u => u.FinishAt));
        UpgradeStore.SaveDeleted(r.Deleted);
        return SyncCode.Create(r.Upgrades, r.Deleted);
    }

    private static void SetStatus(string text)
    {
        LastStatus = text;
        StatusChanged?.Invoke(text);
    }
}

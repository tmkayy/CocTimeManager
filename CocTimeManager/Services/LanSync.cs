using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CocTimeManager.Services;

/// <summary>
/// Sync two devices on the same Wi-Fi with a 4-digit code, no server involved.
/// <list type="number">
/// <item>The host shows a random code and listens on <see cref="Port"/> (TCP, plus UDP for discovery).</item>
/// <item>The joiner looks for it two ways at once: a UDP broadcast "CTMSYNC?code" that only the host with that
/// code answers, and a quick TCP connect to <see cref="Port"/> on every address in its subnet. The scan only
/// makes outgoing connections, so it still works when the joiner's firewall drops incoming packets
/// (including broadcast replies), e.g. Linux with ufw.</item>
/// <item>Over TCP the joiner sends "CTMSYNC code"; the host answers "OK" only for the right code. Then the
/// joiner sends its sync code (see Models/SyncCode); the host merges it and answers with its merged sync code,
/// which the joiner merges too, so both end up with the same list.</item>
/// </list>
/// The code acts as a password: the host gives up after <see cref="MaxWrongCodes"/> wrong attempts.
/// </summary>
public static class LanSync
{
    public const int Port = 45871;
    public const int MaxWrongCodes = 5;
    public static readonly TimeSpan HostTimeout = TimeSpan.FromMinutes(3);
    private const int MaxLineLength = 1 << 20; // sync codes are ~1 KB; refuse anything absurd

    /// <summary>Android sets this to hold a Wi-Fi multicast lock while hosting; many phones drop
    /// incoming broadcast packets without one.</summary>
    public static Func<IDisposable>? AcquireBroadcastLock { get; set; }

    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 10000).ToString("D4");

    private static string Hello(string code) => "CTMSYNC " + code;

    // ================= host =================

    /// <summary>
    /// Waits for one joiner with <paramref name="code"/>. <paramref name="exchange"/> gets the joiner's sync
    /// code and returns ours (after merging) to send back. Throws <see cref="LanSyncException"/> on failure.
    /// </summary>
    public static async Task HostAsync(string code, Func<string, Task<string>> exchange, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(HostTimeout);
        using var broadcastLock = AcquireBroadcastLock?.Invoke();

        var listener = new TcpListener(IPAddress.Any, Port);
        UdpClient? udp = null;
        try
        {
            listener.Start();
            udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
        }
        catch (SocketException ex)
        {
            listener.Stop();
            udp?.Dispose();
            throw new LanSyncException(ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? $"Port {Port} is already in use (is the app showing a code in another window?)."
                : $"Couldn't start listening on the network ({ex.SocketErrorCode}).");
        }

        using (udp)
        {
            try
            {
                _ = AnswerDiscoveryAsync(udp, code, timeout.Token);
                var wrong = 0;
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                    try
                    {
                        if (await ServeAsync(client, code, exchange, timeout.Token)) return;
                        // Wrong code. (A plain connect-and-close from a scan isn't counted.)
                        if (++wrong >= MaxWrongCodes)
                            throw new LanSyncException("Too many wrong codes, stopped waiting. Show a new code to try again.");
                    }
                    catch (ScanProbe)
                    {
                        // Someone's subnet scan connected and left without sending anything.
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new LanSyncException("No device connected in time. Show a new code to try again.");
            }
            catch (IOException)
            {
                throw new LanSyncException("The connection dropped. Try again.");
            }
            finally
            {
                listener.Stop();
            }
        }
    }

    /// <summary>True after a full exchange, false for a wrong code; throws <see cref="ScanProbe"/> for an empty connection.</summary>
    private static async Task<bool> ServeAsync(TcpClient client, string code, Func<string, Task<string>> exchange, CancellationToken ct)
    {
        using var io = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var both = CancellationTokenSource.CreateLinkedTokenSource(ct, io.Token);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true) { NewLine = "\n" };

        string? hello;
        try
        {
            hello = await ReadLineAsync(reader, both.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new ScanProbe();
        }
        if (hello is null) throw new ScanProbe();
        if (hello != Hello(code)) return false;

        await writer.WriteLineAsync("OK");
        await writer.FlushAsync(both.Token);
        var theirs = await ReadLineAsync(reader, both.Token)
                     ?? throw new LanSyncException("The other device disconnected.");
        var ours = await exchange(theirs);
        await writer.WriteLineAsync(ours);
        await writer.FlushAsync(both.Token);
        return true;
    }

    private sealed class ScanProbe : Exception;

    private static async Task AnswerDiscoveryAsync(UdpClient udp, string code, CancellationToken ct)
    {
        var reply = Encoding.ASCII.GetBytes($"CTMSYNC!{code}:{Port}");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var msg = await udp.ReceiveAsync(ct);
                // Other codes belong to someone else's sync on the same network: stay quiet.
                if (Encoding.ASCII.GetString(msg.Buffer) == "CTMSYNC?" + code)
                    await udp.SendAsync(reply, msg.RemoteEndPoint, ct);
            }
        }
        catch (Exception)
        {
            // Cancelled or socket closed: the host is done.
        }
    }

    // ================= joiner =================

    /// <summary>
    /// Finds the device showing <paramref name="code"/>, sends <paramref name="ourCode"/> and returns its
    /// (merged) sync code. Throws <see cref="LanSyncException"/> on failure.
    /// </summary>
    public static async Task<string> JoinAsync(string code, string ourCode, CancellationToken ct)
    {
        using var finding = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var udpTask = DiscoverAsync(code, TimeSpan.FromSeconds(3), finding.Token);
        var scanTask = ScanAsync(finding.Token);

        // Try whichever finds something first (UDP answers within ms on open networks; the scan takes up to
        // ~1.5 s but gets through firewalls), then the other one's results.
        async IAsyncEnumerable<IPEndPoint> Candidates()
        {
            if (await Task.WhenAny(udpTask, scanTask) == udpTask)
            {
                if (await udpTask is { } viaUdp) yield return viaUdp;
                foreach (var ep in await scanTask) yield return ep;
            }
            else
            {
                foreach (var ep in await scanTask) yield return ep;
                if (await udpTask is { } viaUdp) yield return viaUdp;
            }
        }

        var tried = new HashSet<IPAddress>();
        try
        {
            await foreach (var ep in Candidates())
            {
                if (!tried.Add(ep.Address)) continue;
                var session = await TryHandshakeAsync(ep, code, ct);
                if (session is null) continue;

                using (session)
                {
                    try
                    {
                        using var io = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        io.CancelAfter(TimeSpan.FromSeconds(20));
                        await session.Writer.WriteLineAsync(ourCode);
                        await session.Writer.FlushAsync(io.Token);
                        return await ReadLineAsync(session.Reader, io.Token)
                               ?? throw new LanSyncException("The other device closed the connection.");
                    }
                    catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException && !ct.IsCancellationRequested)
                    {
                        throw new LanSyncException("The connection dropped during the sync. Try again.");
                    }
                }
            }
        }
        finally
        {
            finding.Cancel();
        }

        throw new LanSyncException(
            $"No device showing {code} found. Check the code and that both are on the same Wi-Fi. " +
            $"If this device shows the code instead, its firewall must allow port {Port} (e.g. Linux: sudo ufw allow {Port}).");
    }

    private sealed class Session(TcpClient client, StreamReader reader, StreamWriter writer) : IDisposable
    {
        public StreamReader Reader => reader;
        public StreamWriter Writer => writer;

        public void Dispose()
        {
            writer.Dispose();
            reader.Dispose();
            client.Dispose();
        }
    }

    /// <summary>Connects and sends the code; returns the open session if the host answered "OK".</summary>
    private static async Task<Session?> TryHandshakeAsync(IPEndPoint ep, string code, CancellationToken ct)
    {
        var client = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            using var step = CancellationTokenSource.CreateLinkedTokenSource(ct);
            step.CancelAfter(TimeSpan.FromSeconds(4));
            await client.ConnectAsync(ep, step.Token);
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
            var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true) { NewLine = "\n" };
            await writer.WriteLineAsync(Hello(code));
            await writer.FlushAsync(step.Token);
            if (await reader.ReadLineAsync(step.Token) == "OK") return new Session(client, reader, writer);
            reader.Dispose();
            await writer.DisposeAsync();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Not reachable, wrong code, or not our app: try the next one.
        }
        client.Dispose();
        ct.ThrowIfCancellationRequested();
        return null;
    }

    /// <summary>UDP broadcast discovery; null if nobody answered within <paramref name="wait"/>.</summary>
    private static async Task<IPEndPoint?> DiscoverAsync(string code, TimeSpan wait, CancellationToken ct)
    {
        try
        {
            // Bound to a random port up front: we start listening for the reply before sending anything.
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
            var ask = Encoding.ASCII.GetBytes("CTMSYNC?" + code);
            var expect = $"CTMSYNC!{code}:";
            var targets = BroadcastAddresses().Select(a => new IPEndPoint(a, Port)).ToList();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(wait);
            var receive = udp.ReceiveAsync(timeout.Token).AsTask();
            while (true)
            {
                foreach (var t in targets)
                {
                    try { await udp.SendAsync(ask, t, timeout.Token); }
                    catch (SocketException) { /* interface down / unreachable: try the others */ }
                }

                // Re-send every 500 ms in case a packet got lost.
                var done = await Task.WhenAny(receive, Task.Delay(500, timeout.Token));
                if (done != receive) continue;

                var msg = await receive;
                var text = Encoding.ASCII.GetString(msg.Buffer);
                if (text.StartsWith(expect, StringComparison.Ordinal) && int.TryParse(text[expect.Length..], out var port))
                    return new IPEndPoint(msg.RemoteEndPoint.Address, port);
                receive = udp.ReceiveAsync(timeout.Token).AsTask();
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null; // timed out, or broadcasts not allowed here: the subnet scan still runs
        }
    }

    /// <summary>Every address in our subnets (at most a /22 around each of our addresses) with <see cref="Port"/> open.</summary>
    private static async Task<List<IPEndPoint>> ScanAsync(CancellationToken ct)
    {
        var open = new List<IPEndPoint>();
        using var gate = new SemaphoreSlim(256);
        var tasks = SubnetHosts().Select(async ip =>
        {
            await gate.WaitAsync(ct);
            try
            {
                using var client = new TcpClient(AddressFamily.InterNetwork);
                using var step = CancellationTokenSource.CreateLinkedTokenSource(ct);
                step.CancelAfter(TimeSpan.FromMilliseconds(1500));
                await client.ConnectAsync(ip, Port, step.Token);
                lock (open) open.Add(new IPEndPoint(ip, Port)); // closed again right away; the host ignores that
            }
            catch (Exception)
            {
                // Nothing there.
            }
            finally
            {
                gate.Release();
            }
        });
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Found it another way.
        }
        return open;
    }

    private static IEnumerable<(IPAddress ip, IPAddress mask)> OwnIPv4()
    {
        var result = new List<(IPAddress, IPAddress)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && ua.IPv4Mask is not null)
                        result.Add((ua.Address, ua.IPv4Mask));
            }
        }
        catch (Exception)
        {
            // Not allowed to list interfaces here: broadcast to 255.255.255.255 still works on most networks.
        }
        return result;
    }

    private static uint ToUInt(IPAddress a) => (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(a.GetAddressBytes()));
    private static IPAddress FromUInt(uint v) => new(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)v)));

    private static IEnumerable<IPAddress> SubnetHosts()
    {
        var seen = new HashSet<uint>();
        foreach (var (ip, mask) in OwnIPv4())
        {
            var m = ToUInt(mask);
            if (m < 0xFFFFFC00u) m = 0xFFFFFC00u; // big networks: only the /22 around us (1022 hosts)
            var net = ToUInt(ip) & m;
            var bcast = net | ~m;
            for (var h = net + 1; h < bcast; h++)
                if (seen.Add(h)) yield return FromUInt(h);
        }
    }

    /// <summary>255.255.255.255 plus each interface's subnet broadcast address
    /// (some systems only send the global one out of a single interface).</summary>
    private static IEnumerable<IPAddress> BroadcastAddresses()
    {
        var result = new HashSet<IPAddress> { IPAddress.Broadcast };
        foreach (var (ip, mask) in OwnIPv4())
            result.Add(FromUInt(ToUInt(ip) | ~ToUInt(mask)));
        return result;
    }

    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken ct)
    {
        var line = await reader.ReadLineAsync(ct);
        if (line is { Length: > MaxLineLength }) throw new LanSyncException("The other device sent too much data.");
        return line;
    }
}

public sealed class LanSyncException(string message) : Exception(message);

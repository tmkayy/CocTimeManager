using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CocTimeManager.Models;

namespace CocTimeManager.Services;

/// <summary>Our side of a pairing: who we are and whether we can listen for syncs.</summary>
public sealed record PairingSide(string Id, string Name, bool Listens);

/// <summary>
/// Pairing and paired syncs, on the same port as the code sync.
/// <para>
/// Pairing happens at the end of a code sync, which the 4-digit code already authenticated: the joiner sends
/// "PAIR1 id listens name key" and the host answers "PAIRED1 ..." the same way. The keys are ECDH (P-256) public keys,
/// so the shared secret both derive never crosses the network. Older versions just close the connection instead.
/// </para>
/// <para>
/// A paired sync is one request and one reply over TCP: "CTMPAIR1 id time nonce mac" plus our sync code, answered
/// with "CTMPAIR1-OK mac" plus the listener's merged sync code. Both are HMAC-SHA256 signed with the shared secret,
/// and the listener refuses unknown devices, old timestamps and repeated nonces.
/// </para>
/// </summary>
public static partial class LanSync
{
    private const string PairHello = "PAIR1", PairReply = "PAIRED1", PairedRequest = "CTMPAIR1";
    private static readonly TimeSpan PairedClockSkew = TimeSpan.FromMinutes(10);

    // ================= pairing =================

    /// <summary>Joiner side, after the code sync. Null if the host doesn't pair (older version) or anything fails.</summary>
    private static async Task<PairInfo?> OfferPairingAsync(StreamReader reader, StreamWriter writer, PairingSide me, CancellationToken ct)
    {
        try
        {
            using var io = CancellationTokenSource.CreateLinkedTokenSource(ct);
            io.CancelAfter(TimeSpan.FromSeconds(5));
            using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            await writer.WriteLineAsync(PairLine(PairHello, me, ecdh));
            await writer.FlushAsync(io.Token);
            return ReadPairLine(await ReadLineAsync(reader, io.Token), PairReply, me, ecdh);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Host side, after the code sync. Null if the joiner doesn't pair (older version) or anything fails.</summary>
    private static async Task<PairInfo?> AnswerPairingAsync(StreamReader reader, StreamWriter writer, PairingSide me, CancellationToken ct)
    {
        try
        {
            using var io = CancellationTokenSource.CreateLinkedTokenSource(ct);
            io.CancelAfter(TimeSpan.FromSeconds(5));
            var line = await ReadLineAsync(reader, io.Token);
            using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var pair = ReadPairLine(line, PairHello, me, ecdh);
            if (pair is null) return null;
            await writer.WriteLineAsync(PairLine(PairReply, me, ecdh));
            await writer.FlushAsync(io.Token);
            return pair;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static string PairLine(string kind, PairingSide me, ECDiffieHellman ecdh) =>
        $"{kind} {me.Id} {(me.Listens ? 1 : 0)} {Convert.ToBase64String(Encoding.UTF8.GetBytes(me.Name))} " +
        Convert.ToBase64String(ecdh.ExportSubjectPublicKeyInfo());

    private static PairInfo? ReadPairLine(string? line, string kind, PairingSide me, ECDiffieHellman ecdh)
    {
        var parts = line?.Split(' ');
        if (parts is not { Length: 5 } || parts[0] != kind || parts[1].Length is 0 or > 64 || parts[1] == me.Id) return null;

        using var theirs = ECDiffieHellman.Create();
        theirs.ImportSubjectPublicKeyInfo(Convert.FromBase64String(parts[4]), out _);
        // Both sides hash the same label, with the two ids in a fixed order.
        var ids = string.Join('|', new[] { me.Id, parts[1] }.Order(StringComparer.Ordinal));
        var secret = ecdh.DeriveKeyFromHash(theirs.PublicKey, HashAlgorithmName.SHA256,
                                            Encoding.UTF8.GetBytes("CTM-PAIR1|" + ids + "|"), null);
        var name = Encoding.UTF8.GetString(Convert.FromBase64String(parts[3]));
        return new PairInfo
        {
            Id = parts[1],
            Name = name.Length > 60 ? name[..60] : name,
            Listens = parts[2] == "1",
            Secret = Convert.ToBase64String(secret),
        };
    }

    // ================= paired sync: listener =================

    /// <summary>
    /// Answers syncs from the paired device until cancelled. <paramref name="currentPair"/> is read per request, so
    /// re-pairing or forgetting takes effect at once; <paramref name="merge"/> merges their sync code and returns ours.
    /// Throws <see cref="LanSyncException"/> if the port can't be opened.
    /// </summary>
    public static async Task ServePairedAsync(string ownId, Func<PairInfo?> currentPair,
                                              Func<SyncPayload, Task<string>> merge, CancellationToken ct)
    {
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
                ? $"Port {Port} is in use, so the paired device can't reach this one (is the app open twice?)."
                : $"Couldn't listen for the paired device ({ex.SocketErrorCode}).");
        }

        using (udp)
        {
            _ = AnswerPairedDiscoveryAsync(udp, ownId, ct);
            var seenNonces = new Queue<string>();
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(ct);
                    try
                    {
                        await ServePairedClientAsync(client, ownId, currentPair, merge, seenNonces, ct);
                    }
                    catch (Exception) when (!ct.IsCancellationRequested)
                    {
                        // A scan, a bad request or a dropped connection: wait for the next one.
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped.
            }
            finally
            {
                listener.Stop();
            }
        }
    }

    private static async Task ServePairedClientAsync(TcpClient client, string ownId, Func<PairInfo?> currentPair,
                                                     Func<SyncPayload, Task<string>> merge, Queue<string> seenNonces,
                                                     CancellationToken ct)
    {
        using var io = CancellationTokenSource.CreateLinkedTokenSource(ct);
        io.CancelAfter(TimeSpan.FromSeconds(20));
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true) { NewLine = "\n" };

        var parts = (await ReadLineAsync(reader, io.Token))?.Split(' ');
        if (parts is not { Length: 5 } || parts[0] != PairedRequest) return; // scan, or a code sync looking for a host
        var payload = await ReadLineAsync(reader, io.Token);
        if (payload is null) return;
        var (fromId, time, nonce, mac) = (parts[1], parts[2], parts[3], parts[4]);

        var pair = currentPair();
        if (pair is null || pair.Id != fromId)
        {
            await writer.WriteLineAsync(PairedRequest + "-UNKNOWN");
            await writer.FlushAsync(io.Token);
            return;
        }
        var secret = Convert.FromBase64String(pair.Secret);
        if (!long.TryParse(time, out var unix) ||
            (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unix)).Duration() > PairedClockSkew ||
            seenNonces.Contains(nonce) ||
            !Verify(secret, mac, "req", fromId, time, nonce, payload))
            return;
        seenNonces.Enqueue(nonce);
        while (seenNonces.Count > 500) seenNonces.Dequeue();

        var ours = await merge(SyncCode.Parse(payload));
        await writer.WriteLineAsync($"{PairedRequest}-OK {Sign(secret, "resp", ownId, nonce, ours)}");
        await writer.WriteLineAsync(ours);
        await writer.FlushAsync(io.Token);
    }

    private static async Task AnswerPairedDiscoveryAsync(UdpClient udp, string ownId, CancellationToken ct)
    {
        var reply = Encoding.ASCII.GetBytes($"CTMPAIR!{ownId}:{Port}");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var msg = await udp.ReceiveAsync(ct);
                if (Encoding.ASCII.GetString(msg.Buffer) == "CTMPAIR?" + ownId)
                    await udp.SendAsync(reply, msg.RemoteEndPoint, ct);
            }
        }
        catch (Exception)
        {
            // Stopped.
        }
    }

    // ================= paired sync: caller =================

    /// <summary>
    /// Finds the paired device (last address first, then broadcast, then a subnet scan), sends
    /// <paramref name="ourCode"/> and returns its signed reply and where it was. Throws <see cref="LanSyncException"/>.
    /// </summary>
    public static async Task<(string Reply, IPAddress Address)> ExchangePairedAsync(PairInfo pair, string ownId,
                                                                                     string ourCode, CancellationToken ct)
    {
        var secret = Convert.FromBase64String(pair.Secret);
        var tried = new HashSet<IPAddress>();

        async Task<(string, IPAddress)?> TryAt(IPAddress address)
        {
            if (!tried.Add(address)) return null;
            using var client = new TcpClient(AddressFamily.InterNetwork);
            try
            {
                using var step = CancellationTokenSource.CreateLinkedTokenSource(ct);
                step.CancelAfter(TimeSpan.FromSeconds(3));
                await client.ConnectAsync(address, Port, step.Token);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return null;
            }

            using var io = CancellationTokenSource.CreateLinkedTokenSource(ct);
            io.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true) { NewLine = "\n" };

                var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                await writer.WriteLineAsync($"{PairedRequest} {ownId} {time} {nonce} {Sign(secret, "req", ownId, time, nonce, ourCode)}");
                await writer.WriteLineAsync(ourCode);
                await writer.FlushAsync(io.Token);

                var status = await ReadLineAsync(reader, io.Token);
                if (status == PairedRequest + "-UNKNOWN")
                    throw new LanSyncException($"{pair.Name} doesn't recognise this device any more. Sync with a code again to pair them.");
                var head = status?.Split(' ');
                if (head is not { Length: 2 } || head[0] != PairedRequest + "-OK") return null; // something else on that port
                var reply = await ReadLineAsync(reader, io.Token);
                if (reply is null || !Verify(secret, head[1], "resp", pair.Id, nonce, reply)) return null;
                return (reply, address);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                return null;
            }
        }

        if (IPAddress.TryParse(pair.LastAddress, out var last) && await TryAt(last) is { } quick) return quick;

        if (await DiscoverAsync("CTMPAIR?" + pair.Id, $"CTMPAIR!{pair.Id}:", TimeSpan.FromSeconds(2), ct) is { } found &&
            await TryAt(found.Address) is { } viaUdp)
            return viaUdp;

        foreach (var ep in await ScanAsync(ct))
            if (await TryAt(ep.Address) is { } viaScan)
                return viaScan;

        throw new LanSyncException(
            $"Couldn't reach {pair.Name}. Is it on the same Wi-Fi with the app running? " +
            $"Its firewall must allow port {Port} (e.g. Linux: sudo ufw allow {Port}).");
    }

    private static string Sign(byte[] secret, params string[] fields) =>
        Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(string.Join('\n', fields))));

    private static bool Verify(byte[] secret, string mac, params string[] fields)
    {
        byte[] given;
        try
        {
            given = Convert.FromHexString(mac);
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(given,
            HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(string.Join('\n', fields))));
    }
}

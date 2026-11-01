using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CocTimeManager.Models;

/// <summary>Everything one device knows: its tracked upgrades plus recent hand deletions.</summary>
public sealed class SyncPayload
{
    public List<TrackedUpgrade> Upgrades { get; set; } = [];
    public Dictionary<Guid, DateTime> Deleted { get; set; } = [];
}

/// <param name="Upgrades">The merged list to show.</param>
/// <param name="Deleted">The merged deletion markers to save.</param>
public sealed record SyncResult(List<TrackedUpgrade> Upgrades, Dictionary<Guid, DateTime> Deleted,
                                int Added, int Updated, int Removed, bool ImportsReplaced);

/// <summary>
/// Copy/paste sync between devices without a server: the tracked list is packed into a text code
/// ("CTM1." + base64url of Brotli-compressed JSON) that you send to the other device yourself.
/// Pasting merges, it never just overwrites:
/// <list type="bullet">
/// <item>Hand-added upgrades: newer <see cref="TrackedUpgrade.UpdatedAt"/> wins; deletions are remembered
/// for a while so a deleted upgrade isn't brought back by the other device.</item>
/// <item>Imported upgrades: the set from the newest village export wins as a whole (both devices may
/// have imported separately, with different ids), keeping local ids for items still running.</item>
/// <item>"Notified" is combined, so an upgrade only notifies if neither device did yet.</item>
/// </list>
/// </summary>
public static class SyncCode
{
    private const string Prefix = "CTM1.";

    public static string Create(IEnumerable<TrackedUpgrade> upgrades, Dictionary<Guid, DateTime> deleted)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new SyncPayload { Upgrades = upgrades.ToList(), Deleted = deleted });
        using var ms = new MemoryStream();
        using (var brotli = new BrotliStream(ms, CompressionLevel.SmallestSize))
            brotli.Write(json);
        return Prefix + Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Throws <see cref="FormatException"/> if <paramref name="text"/> isn't a sync code.</summary>
    public static SyncPayload Parse(string text)
    {
        // Messaging apps may wrap long text or add spaces; ignore all whitespace.
        var code = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var start = code.IndexOf(Prefix, StringComparison.Ordinal);
        if (start < 0) throw new FormatException("The clipboard doesn't contain a sync code.");
        var b64 = code[(start + Prefix.Length)..].Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');

        try
        {
            using var input = new BrotliStream(new MemoryStream(Convert.FromBase64String(b64)), CompressionMode.Decompress);
            using var output = new MemoryStream();
            input.CopyTo(output);
            var payload = JsonSerializer.Deserialize<SyncPayload>(Encoding.UTF8.GetString(output.ToArray()));
            if (payload is not null) return payload;
        }
        catch (Exception)
        {
            // Bad base64, truncated Brotli data or invalid JSON: all reported the same way below.
        }
        throw new FormatException("The sync code is incomplete or damaged. Copy it again.");
    }

    public static SyncResult Merge(IReadOnlyList<TrackedUpgrade> local, Dictionary<Guid, DateTime> localDeleted,
                                   SyncPayload incoming, DateTime nowUtc)
    {
        // Deletions: keep the latest time per id, drop very old ones.
        var cutoff = nowUtc - UpgradeStore.DeletedRetention;
        var deleted = new Dictionary<Guid, DateTime>();
        foreach (var (id, at) in localDeleted.Concat(incoming.Deleted))
        {
            var utc = at.ToUniversalTime();
            if (utc > cutoff && (!deleted.TryGetValue(id, out var cur) || utc > cur)) deleted[id] = utc;
        }
        bool IsDeleted(TrackedUpgrade u) => deleted.TryGetValue(u.Id, out var at) && at >= u.UpdatedAt;

        // ---- hand-added: per id, newer wins ----
        var result = new List<TrackedUpgrade>();
        int added = 0, updated = 0;
        var incomingManual = incoming.Upgrades.Where(u => u.ImportKey is null).ToDictionary(u => u.Id);
        foreach (var mine in local.Where(u => u.ImportKey is null))
        {
            if (IsDeleted(mine)) continue;
            if (incomingManual.Remove(mine.Id, out var theirs) && theirs.UpdatedAt > mine.UpdatedAt)
            {
                theirs.Notified |= mine.Notified;
                result.Add(theirs);
                updated++;
            }
            else
            {
                if (theirs is not null) mine.Notified |= theirs.Notified;
                result.Add(mine);
            }
        }
        foreach (var theirs in incomingManual.Values.Where(u => !IsDeleted(u)))
        {
            result.Add(theirs);
            added++;
        }

        // ---- imported: the newest export's set wins ----
        var localImported = local.Where(u => u.ImportKey is not null).ToList();
        var theirImported = incoming.Upgrades.Where(u => u.ImportKey is not null).ToList();
        var localExport = localImported.Count == 0 ? DateTime.MinValue : localImported.Max(u => u.ImportedAt);
        var theirExport = theirImported.Count == 0 ? DateTime.MinValue : theirImported.Max(u => u.ImportedAt);
        var replaceImports = theirExport > localExport;

        var matched = new HashSet<TrackedUpgrade>();
        // Same item on both devices: same id, or same import key with the closest finish time.
        TrackedUpgrade? Counterpart(TrackedUpgrade u, List<TrackedUpgrade> among) =>
            among.FirstOrDefault(m => m.Id == u.Id && !matched.Contains(m))
            ?? among.Where(m => !matched.Contains(m) && m.ImportKey == u.ImportKey)
                .OrderBy(m => Math.Abs((m.FinishAt - u.FinishAt).Ticks)).FirstOrDefault();

        if (replaceImports)
        {
            foreach (var theirs in theirImported.Where(u => !IsDeleted(u)))
            {
                // Keep the local id for the same item so notifications/alarms carry over instead of doubling.
                var mine = Counterpart(theirs, localImported);
                if (mine is not null)
                {
                    matched.Add(mine);
                    theirs.Id = mine.Id;
                    theirs.StartedAt = mine.StartedAt < theirs.StartedAt ? mine.StartedAt : theirs.StartedAt;
                    theirs.Notified |= mine.Notified;
                    updated++;
                }
                else added++;
                result.Add(theirs);
            }
        }
        else
        {
            foreach (var mine in localImported.Where(u => !IsDeleted(u)))
            {
                // Same or older export over there: keep ours, but don't notify again if they already did.
                if (theirExport == localExport && Counterpart(mine, theirImported) is { } theirs)
                {
                    matched.Add(theirs);
                    mine.Notified |= theirs.Notified;
                }
                result.Add(mine);
            }
        }

        var removed = local.Count(l => result.All(r => r.Id != l.Id));
        return new SyncResult(result, deleted, added, updated, removed, replaceImports);
    }
}

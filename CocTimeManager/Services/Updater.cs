using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CocTimeManager.Services;

/// <summary>Installs a downloaded release on one platform. Set <see cref="Updater.Installer"/> at startup.</summary>
public interface IUpdateInstaller
{
    /// <summary>Version of the running app.</summary>
    Version CurrentVersion { get; }

    /// <summary>End of this platform's release file name, e.g. "-linux-x64.tar.gz".</summary>
    string AssetSuffix { get; }

    /// <summary>Folder to download the update into.</summary>
    string DownloadDirectory { get; }

    /// <summary>Null if updating in place is possible, else why not (e.g. running from source).</summary>
    string? CannotUpdateReason { get; }

    /// <summary>Installs the verified file. Desktop replaces itself and restarts; Android opens the installer.</summary>
    Task InstallAsync(string downloadedFile);
}

/// <param name="AssetUrl">The release file's normal download link.</param>
/// <param name="AssetApiUrl">The same file through GitHub's API, used when the normal link fails.</param>
public sealed record UpdateInfo(Version Version, string Tag, string Notes, string AssetName, string AssetUrl, string AssetApiUrl,
                                string? ChecksumsUrl, string? ChecksumsApiUrl);

/// <summary>
/// In-app updates from this project's GitHub releases (via the API, the user never opens the website).
/// Downloads are checked against the release's SHA256SUMS.txt before they're installed.
/// </summary>
public static class Updater
{
    public const string Repo = "tmkayy/CocTimeManager";

    public static IUpdateInstaller? Installer { get; set; }

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CocUpgradeTimer-updater");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>"v1.2.1" / "1.2.1" -> 1.2.1 (3 parts, so 1.2.1.0 from the assembly compares equal).</summary>
    public static Version? ParseVersion(string text)
    {
        var t = text.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(t, out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    /// <summary>The latest release if it's newer than <paramref name="current"/> and has a file for this platform.</summary>
    public static async Task<UpdateInfo?> CheckAsync(Version current, string assetSuffix, CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var latest = ParseVersion(tag);
        if (latest is null || latest <= Normalize(current)) return null;

        var assets = root.GetProperty("assets").EnumerateArray()
            .Select(a => (name: a.GetProperty("name").GetString() ?? "", url: a.GetProperty("browser_download_url").GetString() ?? "",
                          api: a.GetProperty("url").GetString() ?? ""))
            .ToList();
        var asset = assets.FirstOrDefault(a => a.name.EndsWith(assetSuffix, StringComparison.OrdinalIgnoreCase));
        if (asset.name.Length == 0) return null;
        var sums = assets.FirstOrDefault(a => a.name == "SHA256SUMS.txt");

        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        return new UpdateInfo(latest, tag, notes, asset.name, asset.url, asset.api,
                              sums.name is null ? null : sums.url, sums.name is null ? null : sums.api);
    }

    /// <summary>Downloads the update into <paramref name="dir"/> and verifies its SHA-256. Returns the file path.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, string dir, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (update.ChecksumsUrl is null)
            throw new InvalidOperationException("The release has no SHA256SUMS.txt, so the download can't be verified.");

        string sumsText;
        using (var sumsResp = await GetFileAsync(update.ChecksumsUrl, update.ChecksumsApiUrl, ct))
            sumsText = await sumsResp.Content.ReadAsStringAsync(ct);
        var expected = ParseChecksums(sumsText)
            .GetValueOrDefault(update.AssetName)
            ?? throw new InvalidOperationException($"SHA256SUMS.txt has no entry for {update.AssetName}.");

        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, update.AssetName);
        using (var resp = await GetFileAsync(update.AssetUrl, update.AssetApiUrl, ct))
        {
            var total = resp.Content.Headers.ContentLength;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(path);
            var buffer = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total.Value);
            }
        }

        string actual;
        await using (var f = File.OpenRead(path))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(f, ct)).ToLowerInvariant();
        if (actual != expected)
        {
            File.Delete(path);
            throw new InvalidOperationException("The download doesn't match its checksum, so it wasn't installed. Try again.");
        }
        return path;
    }

    /// <summary>
    /// Starts downloading a release file: the normal link first, then the same file through GitHub's API.
    /// The normal links sometimes time out on GitHub's side (504) while the API still serves the file.
    /// Returns a successful response with only the headers read.
    /// </summary>
    private static async Task<HttpResponseMessage> GetFileAsync(string url, string? apiUrl, CancellationToken ct)
    {
        Exception? firstError = null;
        try
        {
            var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.IsSuccessStatusCode || string.IsNullOrEmpty(apiUrl)) return resp.EnsureSuccessStatusCode();
            firstError = new HttpRequestException($"{(int)resp.StatusCode} ({resp.ReasonPhrase})");
            resp.Dispose();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested &&
                                   !string.IsNullOrEmpty(apiUrl))
        {
            firstError = ex;
        }

        // The API sends the file itself (via a redirect) when asked for application/octet-stream.
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("application/octet-stream");
        var fallback = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (fallback.IsSuccessStatusCode) return fallback;
        var code = $"{(int)fallback.StatusCode} ({fallback.ReasonPhrase})";
        fallback.Dispose();
        throw new HttpRequestException($"GitHub couldn't send the download right now ({firstError?.Message}, then {code}). Try again in a few minutes.");
    }

    /// <summary>"hash  filename" lines (sha256sum format) -> filename: lowercase hash.</summary>
    public static Dictionary<string, string> ParseChecksums(string text)
    {
        var result = new Dictionary<string, string>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2) result[parts[1].TrimStart('*').Trim()] = parts[0].ToLowerInvariant();
        }
        return result;
    }
}

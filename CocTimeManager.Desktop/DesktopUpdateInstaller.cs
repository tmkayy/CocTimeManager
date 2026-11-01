using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CocTimeManager.Services;

namespace CocTimeManager.Desktop;

/// <summary>
/// Replaces the running single-file app with the downloaded release and restarts it.
/// Linux: extracts the binary from the .tar.gz and renames it over the running one (allowed on Linux).
/// Windows: a running .exe can't be overwritten but can be renamed, so it's moved to ".old" first
/// (deleted on the next start).
/// </summary>
public sealed class DesktopUpdateInstaller : IUpdateInstaller
{
    /// <summary>Passed to the restarted app with the old process id, so it waits for the old one to exit
    /// (otherwise single-instance would hand it straight back to the old process).</summary>
    public const string AfterUpdateArg = "--after-update";

    public Version CurrentVersion => Updater.Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    public string AssetSuffix =>
        OperatingSystem.IsWindows() ? "-windows-x64.exe" :
        OperatingSystem.IsLinux() ? "-linux-x64.tar.gz" : "(none)";

    public string DownloadDirectory => Path.Combine(Path.GetTempPath(), "CocTimeManager", "update");

    public string? CannotUpdateReason
    {
        get
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
                return "In-app updates are only available on Windows and Linux.";
            // Release builds are single-file; from source (dotnet run / an IDE) the assembly has a location.
            if (!string.IsNullOrEmpty(typeof(DesktopUpdateInstaller).Assembly.Location))
                return "Running from source: update with git pull instead.";
            if (Environment.ProcessPath is null) return "Can't tell where the app is installed.";
            return null;
        }
    }

    public Task InstallAsync(string downloadedFile) => Task.Run(() =>
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Can't tell where the app is installed.");
        ReplaceBinary(downloadedFile, exe);
        Restart(exe);
    });

    /// <summary>Swaps <paramref name="targetExe"/> for the new version. Separate from the restart for testing.</summary>
    public static void ReplaceBinary(string downloadedFile, string targetExe)
    {
        var dir = Path.GetDirectoryName(targetExe)!;
        var staged = Path.Combine(dir, "." + Path.GetFileName(targetExe) + ".new");
        try
        {
            if (downloadedFile.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                ExtractBinary(downloadedFile, staged);
            else
                File.Copy(downloadedFile, staged, overwrite: true);

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(staged, File.GetUnixFileMode(targetExe) | UnixFileMode.UserExecute);

            if (OperatingSystem.IsWindows())
            {
                var old = targetExe + ".old";
                File.Delete(old);
                File.Move(targetExe, old);
            }
            File.Move(staged, targetExe, overwrite: true);
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(staged);
            throw new InvalidOperationException(
                $"No permission to replace the app in {dir}. If it was installed for all users, download the new version and run install.sh again.");
        }
        catch
        {
            TryDelete(staged);
            throw;
        }
    }

    /// <summary>The release tarball holds CocUpgradeTimer-x.y.z-linux-x64/coc-upgrade-timer.</summary>
    private static void ExtractBinary(string tarGz, string dest)
    {
        using var file = File.OpenRead(tarGz);
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gz);
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile &&
                Path.GetFileName(entry.Name) == "coc-upgrade-timer")
            {
                entry.ExtractToFile(dest, overwrite: true);
                return;
            }
        }
        throw new InvalidOperationException("The download doesn't contain the app binary.");
    }

    private static void Restart(string exe)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        psi.ArgumentList.Add(AfterUpdateArg);
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(psi);
        Environment.Exit(0);
    }

    /// <summary>Called first thing at startup: waits for the old version to exit and cleans up after an update.</summary>
    public static string[] HandleStartup(string[] args)
    {
        var i = Array.IndexOf(args, AfterUpdateArg);
        if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var oldPid))
        {
            try { Process.GetProcessById(oldPid).WaitForExit(15_000); }
            catch (ArgumentException) { /* already gone */ }
            args = args.Where((_, k) => k != i && k != i + 1).ToArray();
        }

        if (OperatingSystem.IsWindows() && Environment.ProcessPath is { } exe)
            TryDelete(exe + ".old");
        return args;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception) { /* best effort */ }
    }
}

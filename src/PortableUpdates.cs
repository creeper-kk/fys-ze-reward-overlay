using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FysZeRewardOverlay;

// Published packages stay side by side. The original executable continues to serve existing shortcuts.
internal sealed class PortableUpdates
{
    private const string Executable = "FysZeRewardOverlay.exe";
    private const string OriginOption = "--update-origin";
    private const int DownloadLimit = 32 * 1024 * 1024;
    private const int ExtractLimit = 64 * 1024 * 1024;
    private static readonly string[] RequiredFiles =
    {
        Executable, "FysZeRewardOverlay.dll", "FysZeRewardOverlay.deps.json", "FysZeRewardOverlay.runtimeconfig.json"
    };
    private static readonly string[] PackageFiles = [.. RequiredFiles, "使用说明.md", "ACKNOWLEDGMENTS.md"];
    private static readonly HttpClient Downloads = new();
    private readonly string root;
    private readonly string processPath;
    private readonly string origin;
    private readonly string[] args;
    private string PendingFile => Path.Combine(root, "pending.json");
    private string ActiveFile => Path.Combine(root, "active.json");

    internal sealed record Package(string Id, string Tag, string Notes, bool Manual);

    private PortableUpdates(string profile, string processPath, string origin, string[] args)
    {
        this.processPath = Path.GetFullPath(processPath);
        this.origin = Path.GetFullPath(origin);
        root = PackageRoot(profile, this.origin);
        this.args = args;
    }

    internal static PortableUpdates? ForCurrentProcess(string profile, string[] args) =>
        ForProcess(profile, Environment.ProcessPath, args);

    internal static PortableUpdates? ForProcess(string profile, string? processPath, string[] args)
    {
        if (!string.Equals(Path.GetFileName(processPath), Executable, StringComparison.OrdinalIgnoreCase)) return null;
        int originIndex = Array.IndexOf(args, OriginOption);
        if (originIndex >= 0 && originIndex + 1 < args.Length && Path.IsPathFullyQualified(args[originIndex + 1]))
        {
            string origin = args[originIndex + 1];
            var candidate = new PortableUpdates(profile, processPath!, origin,
                args.Where((_, index) => index != originIndex && index != originIndex + 1).ToArray());
            string id = Path.GetFileName(Path.GetDirectoryName(candidate.processPath)) ?? "";
            if (Guid.TryParseExact(id, "N", out _) && candidate.IsRunning(id)) return candidate;
        }
        return new(profile, processPath!, processPath!, args);
    }

    private static string PackageRoot(string profile, string origin)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(origin.ToUpperInvariant())));
        return Path.GetFullPath(Path.Combine(profile, "updates", hash[..24]));
    }

    internal async Task StageAsync(ReleaseCheck release, bool manual, HttpClient? client = null)
    {
        if (release.Package is not { Size: > 0 and <= DownloadLimit } asset)
            throw new InvalidDataException("此 Release 没有可用的 Windows x64 更新包。");
        var previous = Read(PendingFile);
        if (previous is not null && IsVersion(previous.Tag, release.Tag) && HasPackage(previous))
        {
            if (manual && !previous.Manual) Write(PendingFile, previous with { Manual = true });
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var response = await (client ?? Downloads).GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var zip = new MemoryStream((int)asset.Size);
        byte[] buffer = new byte[81920];
        int count;
        while ((count = await body.ReadAsync(buffer, timeout.Token)) > 0)
        {
            if (zip.Length + count > asset.Size) throw new InvalidDataException("更新包大小与 Release 不符。");
            zip.Write(buffer, 0, count);
        }
        if (zip.Length != asset.Size) throw new InvalidDataException("更新包下载不完整。");
        if (asset.Digest is { Length: > 0 } expected &&
            !string.Equals(expected, "sha256:" + Convert.ToHexString(SHA256.HashData(zip.GetBuffer().AsSpan(0, (int)zip.Length))),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新包 SHA-256 校验失败。");

        var package = new Package(Guid.NewGuid().ToString("N"), release.Tag, release.Notes, manual);
        var directory = PackageDirectory(package.Id);
        Directory.CreateDirectory(directory);
        try
        {
            await Task.Run(() => Extract(zip, directory));
            if (!HasPackage(package)) throw new InvalidDataException("更新包中的程序版本与 Release 不符。");
            Write(PendingFile, package);
        }
        catch
        {
            Remove(package);
            throw;
        }
        if (previous is not null && Read(ActiveFile)?.Id != previous.Id && !IsRunning(previous.Id)) Remove(previous);
    }

    private static void Extract(Stream zip, string directory)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        long extracted = 0;
        foreach (string name in PackageFiles)
        {
            // Select fixed root entries only. An archive path never becomes a destination path.
            var entries = archive.Entries.Where(entry => string.Equals(entry.FullName, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (entries.Length == 0 && !RequiredFiles.Contains(name)) continue;
            if (entries.Length != 1 || entries[0].Length <= 0 || entries[0].Length > ExtractLimit - extracted)
                throw new InvalidDataException("Windows x64 更新包缺少文件或格式无效。");
            var entry = entries[0];
            using var output = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write);
            using var input = entry.Open();
            byte[] buffer = new byte[81920];
            int count;
            long written = 0;
            while ((count = input.Read(buffer)) > 0)
            {
                if (written + count > entry.Length) throw new InvalidDataException("更新包中的文件大小不符。");
                output.Write(buffer, 0, count);
                written += count;
            }
            if (written != entry.Length) throw new InvalidDataException("更新包中的文件不完整。");
            extracted += written;
        }
    }

    internal ProcessStartInfo? GetNextLaunch(string localVersion, bool autoEnabled, string? declined = null)
    {
        var active = Read(ActiveFile);
        var pending = Read(PendingFile);
        var package = pending is not null && (autoEnabled || pending.Manual) && (declined is null || !IsVersion(pending.Tag, declined)) &&
            IsNewer(pending.Tag, localVersion) && (active is null || IsNewer(pending.Tag, active.Tag)) && HasPackage(pending)
            ? pending : active;
        if (package is null || !IsNewer(package.Tag, localVersion) || !HasPackage(package)) return null;

        var start = new ProcessStartInfo(Path.Combine(PackageDirectory(package.Id), Executable)) { WorkingDirectory = Environment.CurrentDirectory };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        start.ArgumentList.Add(OriginOption);
        start.ArgumentList.Add(origin);
        return start;
    }

    internal bool TryLaunchNewer(string localVersion, bool autoEnabled, string? declined)
    {
        var start = GetNextLaunch(localVersion, autoEnabled, declined);
        if (start is null) return false;
        if (TryStart(start)) return true;
        var pending = Read(PendingFile);
        if (pending is null || !string.Equals(start.FileName, Path.Combine(PackageDirectory(pending.Id), Executable),
            StringComparison.OrdinalIgnoreCase)) return false;
        ClearPending();
        var fallback = GetNextLaunch(localVersion, autoEnabled: false, declined: pending.Tag);
        return fallback is not null && !string.Equals(start.FileName, fallback.FileName, StringComparison.OrdinalIgnoreCase) && TryStart(fallback);
    }

    private static bool TryStart(ProcessStartInfo start)
    {
        try
        {
            using var launched = Process.Start(start);
            return launched is not null;
        }
        catch (Win32Exception) { return false; }
    }

    internal void RecoverFailedStartup()
    {
        var pending = Read(PendingFile);
        if (pending is not null && IsRunning(pending.Id)) ClearPending();
    }

    internal void ConfirmRunning(string localVersion)
    {
        var pending = Read(PendingFile);
        if (pending is null || !IsRunning(pending.Id) || !IsVersion(pending.Tag, localVersion)) return;
        var previous = Read(ActiveFile);
        Write(ActiveFile, pending);
        ClearPending();
        if (previous is not null && previous.Id != pending.Id) Remove(previous);
    }

    private void ClearPending()
    {
        try { File.Delete(PendingFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal Package? GetInstalledRelease(string localVersion)
    {
        var active = Read(ActiveFile);
        return active is not null && IsRunning(active.Id) && IsVersion(active.Tag, localVersion) ? active : null;
    }

    private bool HasPackage(Package package)
    {
        string directory = PackageDirectory(package.Id);
        try
        {
            if (!RequiredFiles.All(name => File.Exists(Path.Combine(directory, name)))) return false;
            var product = FileVersionInfo.GetVersionInfo(Path.Combine(directory, Executable)).ProductVersion;
            var managed = FileVersionInfo.GetVersionInfo(Path.Combine(directory, "FysZeRewardOverlay.dll")).ProductVersion;
            return product is not null && managed is not null &&
                IsVersion(product.Split('+')[0], package.Tag) && IsVersion(managed.Split('+')[0], package.Tag);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception) { return false; }
    }

    private bool IsRunning(string id) => string.Equals(processPath, Path.Combine(PackageDirectory(id), Executable), StringComparison.OrdinalIgnoreCase);
    private string PackageDirectory(string id) => Path.Combine(root, Guid.ParseExact(id, "N").ToString("N"));

    private static Package? Read(string path)
    {
        try
        {
            var package = JsonSerializer.Deserialize<Package>(File.ReadAllText(path));
            return package is not null && Guid.TryParseExact(package.Id, "N", out _) &&
                package.Tag is not null && ReleaseCheck.TryCompare(package.Tag, package.Tag, out _) ? package : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static void Write(string path, Package package) => AtomicFile.Write(path, JsonSerializer.Serialize(package));

    private void Remove(Package package)
    {
        try { Directory.Delete(PackageDirectory(package.Id), recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool IsNewer(string tag, string version) => ReleaseCheck.TryCompare(tag, version, out int comparison) && comparison > 0;
    private static bool IsVersion(string tag, string version) => ReleaseCheck.TryCompare(tag, version, out int comparison) && comparison == 0;
}

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace FysZeRewardOverlay;

internal sealed record ReleaseAsset(Uri Url, long Size, string? Digest);

internal sealed record ReleaseCheck(string Tag, string PageUrl, int Comparison, string Notes, ReleaseAsset? Package)
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Assembly Assembly = typeof(ReleaseCheck).Assembly;
    internal static string LocalVersion { get; } = Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion.Split('+')[0] ?? Assembly.GetName().Version!.ToString();
    internal static string Repository { get; } = Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(metadata => metadata.Key == "RepositoryUrl")?.Value ?? "https://github.com/creeper-kk/fys-ze-reward-overlay";

    static ReleaseCheck()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("FysZeRewardOverlay");
        Client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    internal static async Task<ReleaseCheck> GetLatestAsync(string repository, string localVersion, HttpClient? client = null)
        => (await GetAsync(repository, "latest", localVersion, client, allowMissing: false))!;

    internal static Task<ReleaseCheck?> GetInstalledAsync(string repository, string version, HttpClient? client = null) =>
        GetAsync(repository, "tags/" + Uri.EscapeDataString("v" + version), version, client, allowMissing: true);

    internal static ReleaseCheck? GetBundledRelease(string version)
    {
        using var stream = Assembly.GetManifestResourceStream($"release-notes-{version}.md");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return new("v" + version, Repository.TrimEnd('/') + "/releases", 0, reader.ReadToEnd(), null);
    }

    private static async Task<ReleaseCheck?> GetAsync(string repository, string endpoint, string localVersion, HttpClient? client, bool allowMissing)
    {
        var repo = new Uri(repository);
        var repoPath = repo.AbsolutePath.Trim('/');
        using var response = await (client ?? Client).GetAsync($"https://api.github.com/repos/{repoPath}/releases/{endpoint}");
        if (allowMissing && response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var release = json.RootElement;
        if (release.ValueKind != JsonValueKind.Object || ReadString(release, "tag_name") is not { } tag ||
            !TryCompare(tag, localVersion, out int comparison))
            throw new InvalidDataException("GitHub Release 的版本号无效。");
        if (allowMissing && comparison != 0) return null;
        var page = repository.TrimEnd('/') + "/releases/tag/" + Uri.EscapeDataString(tag);
        var notes = ReadString(release, "body") ?? "";
        return new(tag, page, comparison, notes, ReadPackage(release, repo, tag));
    }

    private static ReleaseAsset? ReadPackage(JsonElement release, Uri repo, string tag)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 || repo.Scheme != "https" ||
            repo.Host != "github.com" || !release.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array) return null;
        string name = $"FysZeRewardOverlay-{tag}-win-x64.zip";
        var url = new Uri(repo.AbsoluteUri.TrimEnd('/') + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(name));
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind == JsonValueKind.Object && ReadString(asset, "name") == name &&
                ReadString(asset, "browser_download_url") == url.AbsoluteUri && asset.TryGetProperty("size", out var size) &&
                size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out long bytes) && bytes > 0)
                return new(url, bytes, ReadString(asset, "digest"));
        }
        return null;
    }

    private static string? ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static bool TryCompare(string candidate, string installed, out int comparison)
    {
        comparison = 0;
        if (!TryParseVersion(candidate, out var remote) || !TryParseVersion(installed, out var local)) return false;
        comparison = remote.Number.CompareTo(local.Number);
        if (comparison == 0) comparison = remote.Suffix.CompareTo(local.Suffix);
        return true;
    }

    private static bool TryParseVersion(string text, out (Version Number, char Suffix) version)
    {
        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        char suffix = char.MaxValue;
        if (value.Length > 0 && char.IsAsciiLetter(value[^1]))
        {
            suffix = char.ToLowerInvariant(value[^1]);
            value = value[..^1];
        }
        if (!Version.TryParse(value, out var parsed))
        {
            version = default;
            return false;
        }
        version = (new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0)), suffix);
        return true;
    }
}

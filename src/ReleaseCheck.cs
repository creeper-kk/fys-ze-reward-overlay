using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace FysZeRewardOverlay;

internal sealed record ReleaseCheck(string Tag, string PageUrl, int Comparison)
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    static ReleaseCheck()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("FysZeRewardOverlay");
        Client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    internal static async Task<ReleaseCheck> GetLatestAsync(string repository, string localVersion, HttpClient? client = null)
    {
        var repoPath = new Uri(repository).AbsolutePath.Trim('/');
        using var response = await (client ?? Client).GetAsync($"https://api.github.com/repos/{repoPath}/releases/latest");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (json.RootElement.ValueKind != JsonValueKind.Object ||
            !json.RootElement.TryGetProperty("tag_name", out var tagName) ||
            tagName.ValueKind != JsonValueKind.String ||
            tagName.GetString() is not { } tag ||
            !TryParseVersion(tag, out var remote))
            throw new InvalidDataException("GitHub Release 的版本号无效。");
        if (!TryParseVersion(localVersion, out var local))
            throw new InvalidDataException("本地程序的版本号无效。");
        return new(tag, repository.TrimEnd('/') + "/releases/tag/" + Uri.EscapeDataString(tag), remote.CompareTo(local));
    }

    private static bool TryParseVersion(string text, out Version version)
    {
        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        if (!Version.TryParse(value, out var parsed))
        {
            version = null!;
            return false;
        }
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
        return true;
    }
}

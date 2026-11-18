using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace FysZeRewardOverlay;

public enum KzMode { Auto, Ckz, Vnl }

internal sealed record KzTiers(string? Nub, string? Pro)
{
    internal const int MaxTier = 10;
    private static readonly string[] TierNames =
        { "very-easy", "easy", "medium", "advanced", "hard", "very-hard", "extreme", "death", "unfeasible", "impossible" };

    internal static string Format(string? tier)
    {
        if (Number(tier) is not int number) return "—";
        return $"T{number} {char.ToUpperInvariant(tier![0])}{tier[1..].Replace('-', ' ')}";
    }

    internal static int? Number(string? tier)
    {
        int index = Array.IndexOf(TierNames, tier);
        return index < 0 ? null : index + 1;
    }
}

internal sealed record KzCourse(string Name, KzTiers? Ckz, KzTiers? Vnl);
internal sealed record KzMap(string Name, string State, IReadOnlyList<KzCourse> Courses);
internal sealed record KzMapResult(KzMap? Map, bool Failed = false);

internal sealed record KzOverlayInfo(KzMode Mode, int? VnlTier, int? CkzTier, string DifficultyStatus = "")
{
    internal static KzOverlayInfo Resolve(KzMapResult? result, KzMode mode, KzMode tierMode)
    {
        if (result is null) return new(mode, null, null, "查询中");
        if (result.Failed) return new(mode, null, null, "暂不可用");
        if (result.Map is null) return new(mode, null, null, "非全球图");

        var course = result.Map.Courses.FirstOrDefault(course => course.Name.Equals("Main", StringComparison.OrdinalIgnoreCase))
            ?? result.Map.Courses.FirstOrDefault();
        int? vnlTier = tierMode == KzMode.Ckz ? null : KzTiers.Number(course?.Vnl?.Nub);
        int? ckzTier = tierMode == KzMode.Vnl ? null : KzTiers.Number(course?.Ckz?.Nub);
        return new(mode, vnlTier, ckzTier, vnlTier is null && ckzTier is null ? "暂无难度" : "");
    }
}

internal sealed class KzMaps(HttpClient? client = null, TimeProvider? clock = null)
{
    private static readonly HttpClient DefaultClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly Dictionary<string, Task<(KzMapResult Result, DateTimeOffset Expires)>> cache = new(StringComparer.Ordinal);

    internal static bool IsKzMap(string name) => name.StartsWith("kz_", StringComparison.Ordinal) ||
        name.StartsWith("bkz_", StringComparison.Ordinal) || name.StartsWith("xc_", StringComparison.Ordinal) ||
        name.StartsWith("kzpro_", StringComparison.Ordinal) || name.StartsWith("skz_", StringComparison.Ordinal);

    // Called from the dispatcher. Keep one request per map and return only the result for the current map.
    internal KzMapResult? Lookup(string name)
    {
        if (cache.TryGetValue(name, out var request))
        {
            if (!request.IsCompleted) return null;
            var (result, expires) = request.GetAwaiter().GetResult();
            if (expires > clock.GetUtcNow()) return result;
        }
        cache[name] = FetchAsync(name);
        return null;
    }

    private async Task<(KzMapResult, DateTimeOffset)> FetchAsync(string name)
    {
        KzMapResult result;
        try
        {
            using var response = await (client ?? DefaultClient).GetAsync(
                "https://api.cs2kz.org/maps/" + Uri.EscapeDataString(name)).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) result = new(null);
            else
            {
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                result = new(Parse(json.RootElement, name));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            result = new(null, Failed: true);
        }

        var interval = result.Failed ? TimeSpan.FromSeconds(30) :
            result.Map is null ? TimeSpan.FromMinutes(10) : TimeSpan.FromHours(1);
        return (result, clock.GetUtcNow() + interval);
    }

    private static KzMap? Parse(JsonElement root, string expectedName)
    {
        string name = String(root, "name") ?? throw new JsonException("Missing CS2KZ map name.");
        if (!name.Equals(expectedName, StringComparison.OrdinalIgnoreCase)) return null;
        if (!root.TryGetProperty("courses", out var courses) || courses.ValueKind != JsonValueKind.Array)
            throw new JsonException("Missing CS2KZ courses.");

        var list = new List<KzCourse>();
        foreach (var course in courses.EnumerateArray())
        {
            if (String(course, "name") is not { Length: > 0 } label) continue;
            KzTiers? ckz = null, vnl = null;
            if (course.TryGetProperty("filters", out var filters))
            {
                ckz = ReadTiers(filters, "classic");
                vnl = ReadTiers(filters, "vanilla");
            }
            list.Add(new(label, ckz, vnl));
        }
        return new(name, String(root, "state") ?? "", list);
    }

    private static KzTiers? ReadTiers(JsonElement filters, string mode) =>
        filters.ValueKind == JsonValueKind.Object && filters.TryGetProperty(mode, out var filter) && filter.ValueKind == JsonValueKind.Object
            ? new(String(filter, "nub_tier"), String(filter, "pro_tier")) : null;

    private static string? String(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
}

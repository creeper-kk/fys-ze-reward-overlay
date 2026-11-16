using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FysZeRewardOverlay;

public record Broadcast(string Map, string Title, int? Stage, string StageText, string Error, bool FileUnavailable = false)
{
    internal int? ServerNumber { get; init; }
    internal bool IsGsiStageFallback { get; init; }
    public int? CtScore { get; init; }
    public int? Kills { get; init; }
    public int? RoundDamage { get; init; }
    public bool IsZe => Regex.IsMatch(Map, @"^ze_[a-z0-9_-]{1,120}$", RegexOptions.IgnoreCase);
    internal bool CanFetchRewards => IsZe && (IsGsiStageFallback || (Stage.HasValue && Error.Length == 0));
    public string Identity => Map + "|" + Title + "|" + Stage + "|" + StageText + "|" + Error + "|" + FileUnavailable + "|" + ServerNumber + "|" + CtScore;

    internal int? GetSingleRewardStage(RewardData? data) =>
        IsGsiStageFallback && data?.Rewards is { Count: 1 } rewards ? rewards.Keys.Single() : null;
}

internal static class FysServer
{
    private static readonly Regex ZeServer = new(@"僵[尸屍]逃[跑脫]\s*(?:([①-⑳])|([0-9]+)\s*[#服])", RegexOptions.CultureInvariant);

    internal static int? ParseNumber(string name)
    {
        var match = ZeServer.Match(name);
        if (match.Groups[1].Value is { Length: 1 } circled) return circled[0] - '①' + 1;
        return int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number > 0
            ? number : null;
    }
}

public static class BroadcastReader
{
    public static Broadcast Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 65536) return Invalid(BroadcastFiles.DisplayName + "内容过长", true);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
            return Parse(reader.ReadToEnd());
        }
        catch (FileNotFoundException) { return Invalid(BroadcastFiles.DisplayName + "尚未生成", true); }
        catch (DirectoryNotFoundException) { return Invalid(BroadcastFiles.DisplayName + "路径不存在", true); }
        catch (IOException) { return Invalid(BroadcastFiles.DisplayName + "暂时无法读取", true); }
        catch (UnauthorizedAccessException) { return Invalid("没有" + BroadcastFiles.DisplayName + "的读取权限", true); }
        catch (DecoderFallbackException) { return Invalid("无法识别" + BroadcastFiles.DisplayName + "的编码", true); }
        catch (ArgumentException) { return Invalid(BroadcastFiles.DisplayName + "路径无效", true); }
    }

    public static Broadcast Invalid(string reason, bool fileUnavailable = false) => new("", "", null, "", reason, fileUnavailable);
    public static Broadcast Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Invalid("等待" + BroadcastFiles.DisplayName + "更新");
        string map = "", title = "", stageText = "";
        int? stage = null;
        int? serverNumber = null;
        int mapLines = 0, stageLines = 0;
        foreach (var line in raw.TrimStart('\uFEFF').Replace("\r", "").Split('\n'))
        {
            var match = Regex.Match(line, @"^\s*(?<key>区服|地图(?:名称)?|Map|译名|当前关卡|关卡|当前阶段|Stage|Level)\s*[:：]\s*(?<value>.*?)\s*$", RegexOptions.IgnoreCase);
            if (!match.Success) continue;
            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Value.Trim();
            if (key.StartsWith("地图") || key.Equals("Map", StringComparison.OrdinalIgnoreCase))
            {
                mapLines++;
                map = value.ToLowerInvariant();
            }
            else if (key == "译名") title = value;
            else if (key == "区服") serverNumber = FysServer.ParseNumber(value);
            else
            {
                stageLines++;
                stageText = value;
                // Only absolute stage numbers; EX1 / Normal1 are not assumed to be stage 1.
                var number = Regex.Match(value, @"^(?:第\s*)?(?<n>[0-9]{1,3})(?=\s|/|／|关|$)");
                if (number.Success && int.TryParse(number.Groups["n"].Value, out int n) && n > 0) stage = n;
                else if (Regex.IsMatch(value, @"^(?:单关|单关卡|单关地图)$")) stage = 1;
            }
        }
        if (mapLines == 0) return Invalid("当前未连接服务器或未提供地图信息");
        if (mapLines != 1 || stageLines > 1) return Invalid(BroadcastFiles.DisplayName + "正在更新或字段重复");
        if (!Regex.IsMatch(map, @"^[a-z0-9_-]{1,124}$")) return Invalid("未读取到有效地图名称");
        return new(map, title, stage, stageText, "") { ServerNumber = serverNumber };
    }
}

public record Reward(int RankPasses, int RankDamage, int EconPasses, int EconDamage)
{
    public long GetRankPasses(bool isSvip, long damagePoints = 0) =>
        isSvip ? RankPasses + (long)Math.Floor((RankPasses + (decimal)damagePoints) * 0.2m) : RankPasses;
}

public readonly record struct DamageProgress(long Bonus, decimal RemainingThousands, double Fraction)
{
    public static DamageProgress? Calculate(int? kills, int damagePerReward) =>
        FromDamage(kills * 1000L, damagePerReward);

    internal static DamageProgress? FromDamage(long? damage, int damagePerReward)
    {
        if (damage is not long total || total < 0 || damagePerReward <= 0) return null;
        long remainder = total % damagePerReward;
        return new DamageProgress(total / damagePerReward, (damagePerReward - remainder) / 1000m, (double)remainder / damagePerReward);
    }
}
public static class RewardParser
{
    public static Dictionary<int, Reward> Parse(string text)
    {
        using var doc = ReadDocument(text);
        return Parse(doc.RootElement);
    }

    internal static JsonDocument ReadDocument(string text) => JsonDocument.Parse(text.TrimStart('\uFEFF'),
        new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

    internal static Dictionary<int, Reward> Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("奖励配置不是对象");
        var result = new Dictionary<int, Reward>();
        foreach (var entry in root.EnumerateObject())
        {
            if (!int.TryParse(entry.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var stage) || stage < 1 || stage > 999)
                throw new FormatException("奖励关卡编号无效");
            int Read(string name)
            {
                if (!entry.Value.TryGetProperty(name, out var value) || !value.TryGetInt32(out var n) || n < 0) throw new FormatException("奖励字段缺失或无效: " + name);
                return n;
            }
            if (!result.TryAdd(stage, new(Read("rankPasses"), Read("rankDamage"), Read("econPasses"), Read("econDamage")))) throw new FormatException("奖励关卡重复");
        }
        if (result.Count == 0) throw new FormatException("奖励配置为空");
        return result;
    }
}

public record RewardData(Dictionary<int, Reward>? Rewards, DateTimeOffset? FetchedAt, bool Offline, string Error)
{
    public bool Bundled { get; init; }
}
public record DisplayInfo(bool Available, string Map, string Title, int? Stage, Reward? Reward, string Source, string Detail)
{
    public const string UnavailableText = "当前无法获取该地图信息";
    public const string InferredStageUnavailableText = "当前关卡暂无地图奖励,或请手动核对关卡数据";
    public bool StageInferred { get; init; }
    public bool InferredStageMissingReward { get; init; }
    public int? Kills { get; init; }
    public int? RoundDamage { get; init; }
    public static DisplayInfo Resolve(Broadcast broadcast, RewardData? data)
    {
        if (broadcast.IsGsiStageFallback && data is null)
            return new(false, broadcast.Map, broadcast.Title, null, null, "", "正在获取该地图奖励…");

        int? configuredStage = broadcast.GetSingleRewardStage(data);
        int? stage = configuredStage ?? broadcast.Stage;
        bool inferred = configuredStage is null && broadcast.CtScore.HasValue;
        DisplayInfo Fail(string reason) => new(false, broadcast.Map, broadcast.Title, stage, null, "", reason)
            { StageInferred = inferred };
        if (configuredStage is null && broadcast.Error.Length > 0) return Fail(broadcast.Error);
        if (!broadcast.IsZe) return Fail("当前不是 ZE 地图");
        if (stage is null) return Fail(broadcast.StageText.Length == 0 ? BroadcastFiles.DisplayName + "未提供当前关卡" : "无法识别关卡：" + broadcast.StageText);
        if (data is null) return Fail("正在获取该地图奖励…");
        if (data.Rewards is null) return Fail(data.Error);
        if (!data.Rewards.TryGetValue(stage.Value, out var reward))
            return Fail("未找到第 " + stage + " 关的奖励配置") with { InferredStageMissingReward = inferred };
        var source = data.Bundled ? "离线配置 · 随版本附带" : data.Offline
            ? "离线缓存 · " + data.FetchedAt?.ToLocalTime().ToString("MM-dd HH:mm") : "官方配置 · 已同步";
        var detail = data.Bundled ? "使用发布时打包的奖励配置" : data.Offline
            ? "使用上次成功获取的配置" : "伤害转化比 = 伤害 : 1 点货币";
        return new(true, broadcast.Map, broadcast.Title, stage, reward, source, detail)
            { StageInferred = inferred, Kills = broadcast.Kills, RoundDamage = broadcast.RoundDamage };
    }
    public static string Ratio(int number) => number == 0 ? "未启用" : number.ToString("N0", CultureInfo.InvariantCulture) + " : 1";
}

public sealed class RewardRepository : IDisposable
{
    private static readonly Lazy<JsonDocument> bundledRewards = new(() =>
    {
        using var stream = typeof(RewardRepository).Assembly.GetManifestResourceStream("rewards.json")!;
        return JsonDocument.Parse(stream);
    });
    private readonly HttpClient http;
    private readonly string cacheDirectory;
    private readonly TimeProvider clock;
    private TimeSpan refreshInterval = TimeSpan.FromMinutes(10);
    public TimeSpan RefreshInterval
    {
        get => refreshInterval;
        set
        {
            if (value < TimeSpan.FromMinutes(1) || value > TimeSpan.FromMinutes(Settings.MaxRewardRefreshMinutes))
                throw new ArgumentOutOfRangeException(nameof(value));
            refreshInterval = value;
        }
    }
    private readonly Dictionary<string, (RewardData Data, DateTimeOffset RetryAt)> memory = new(StringComparer.OrdinalIgnoreCase);
    public RewardRepository(string cacheDirectory, HttpClient? http = null, TimeProvider? clock = null)
    {
        this.cacheDirectory = cacheDirectory;
        this.clock = clock ?? TimeProvider.System;
        this.http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        this.http.DefaultRequestHeaders.UserAgent.ParseAdd("FysZeRewardOverlay/1.0");
    }
    public void Invalidate() => memory.Clear();
    internal RewardData? GetLastResult(string map) => memory.TryGetValue(map, out var existing) ? existing.Data : null;

    private static bool IsValidMap(string map) => Regex.IsMatch(map, @"^ze_[a-z0-9_-]{1,120}\z");

    internal RewardData? GetLocalFallback(string map)
    {
        if (!IsValidMap(map)) return null;
        var path = Path.Combine(cacheDirectory, map + ".json");
        try
        {
            if (File.Exists(path))
            {
                var cache = JsonSerializer.Deserialize<CacheEnvelope>(File.ReadAllText(path));
                if (cache?.Map == map && cache.FetchedAt <= clock.GetUtcNow().AddMinutes(5) && !string.IsNullOrEmpty(cache.Body))
                    return new(RewardParser.Parse(cache.Body), cache.FetchedAt, true, "");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidOperationException or ArgumentException) { }

        return bundledRewards.Value.RootElement.TryGetProperty(map, out var bundled)
            ? new(RewardParser.Parse(bundled), null, true, "") { Bundled = true }
            : null;
    }

    public async Task<RewardData> GetAsync(string map)
    {
        if (!IsValidMap(map)) return new(null, null, false, "地图名称无效");
        var now = clock.GetUtcNow();
        if (memory.TryGetValue(map, out var existing))
        {
            var expires = existing.Data.Rewards is not null && !existing.Data.Offline && existing.Data.FetchedAt.HasValue
                ? existing.Data.FetchedAt.Value + RefreshInterval : existing.RetryAt;
            if (expires > now) return existing.Data;
        }
        var path = Path.Combine(cacheDirectory, map + ".json");
        RewardData result;
        try
        {
            using var response = await http.GetAsync("https://raw.githubusercontent.com/fyscs/cs2/master/2001/sharp/configs/rewards/" + map + ".jsonc");
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                result = new(null, null, false, "官方配置中未找到该地图奖励");
            }
            else
            {
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync();
                using var parsed = RewardParser.ReadDocument(body);
                var rewards = RewardParser.Parse(parsed.RootElement);
                result = new(rewards, now, false, "");
                try
                {
                    Directory.CreateDirectory(cacheDirectory);
                    var compact = JsonSerializer.Serialize(parsed.RootElement);
                    AtomicFile.Write(path, JsonSerializer.Serialize(new CacheEnvelope(map, now, compact)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Online display still works without disk cache. */ }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException or InvalidOperationException)
        {
            result = GetLocalFallback(map) ?? new(null, null, false, "奖励获取失败，请检查网络后重试");
        }
        memory[map] = (result, clock.GetUtcNow().AddSeconds(30));
        return result;
    }
    public void Dispose() => http.Dispose();
    private record CacheEnvelope(string Map, DateTimeOffset FetchedAt, string Body);
}

public static class AtomicFile
{
    public static void Write(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }
}

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

public record Broadcast(string Map, string Title, int? Stage, string StageText, string Error)
{
    public bool IsZe => Regex.IsMatch(Map, @"^ze_[a-z0-9_-]{1,120}$", RegexOptions.IgnoreCase);
    public string Identity => Map + "|" + Title + "|" + Stage + "|" + StageText + "|" + Error;
}

public static class BroadcastReader
{
    public static Broadcast Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 65536) return Invalid("实时文件内容过长");
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
            return Parse(reader.ReadToEnd());
        }
        catch (FileNotFoundException) { return Invalid("实时文件尚未生成"); }
        catch (DirectoryNotFoundException) { return Invalid("实时文件路径不存在"); }
        catch (IOException) { return Invalid("实时文件暂时无法读取"); }
        catch (UnauthorizedAccessException) { return Invalid("没有实时文件的读取权限"); }
        catch (DecoderFallbackException) { return Invalid("无法识别实时文件编码"); }
        catch (ArgumentException) { return Invalid("实时文件路径无效"); }
    }

    public static Broadcast Invalid(string reason) => new("", "", null, "", reason);
    public static Broadcast Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Invalid("等待实时文件更新");
        string map = "", title = "", stageText = "";
        int? stage = null;
        int mapLines = 0, stageLines = 0;
        foreach (var line in raw.TrimStart('\uFEFF').Replace("\r", "").Split('\n'))
        {
            var match = Regex.Match(line, @"^\s*(?<key>地图(?:名称)?|Map|译名|当前关卡|关卡|当前阶段|Stage|Level)\s*[:：]\s*(?<value>.*?)\s*$", RegexOptions.IgnoreCase);
            if (!match.Success) continue;
            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Value.Trim();
            if (key.StartsWith("地图") || key.Equals("Map", StringComparison.OrdinalIgnoreCase))
            {
                mapLines++;
                map = value.ToLowerInvariant();
            }
            else if (key == "译名") title = value;
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
        if (mapLines != 1 || stageLines > 1) return Invalid("实时文件正在更新或字段重复");
        if (!Regex.IsMatch(map, @"^[a-z0-9_-]{1,124}$")) return Invalid("未读取到有效地图名称");
        return new(map, title, stage, stageText, "");
    }
}

public record Reward(int RankPasses, int RankDamage, int EconPasses, int EconDamage);
public static class RewardParser
{
    public static Dictionary<int, Reward> Parse(string text)
    {
        using var doc = JsonDocument.Parse(text.TrimStart('\uFEFF'), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("奖励配置不是对象");
        var result = new Dictionary<int, Reward>();
        foreach (var entry in doc.RootElement.EnumerateObject())
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

public record RewardData(Dictionary<int, Reward>? Rewards, DateTimeOffset? FetchedAt, bool Offline, string Error);
public record DisplayInfo(bool Available, string Map, string Title, int? Stage, Reward? Reward, string Source, string Detail)
{
    public const string UnavailableText = "当前无法获取该地图信息";
    public static DisplayInfo Resolve(Broadcast broadcast, RewardData? data)
    {
        DisplayInfo Fail(string reason) => new(false, broadcast.Map, broadcast.Title, broadcast.Stage, null, "", reason);
        if (broadcast.Error.Length > 0) return Fail(broadcast.Error);
        if (!broadcast.IsZe) return Fail("当前不是 ZE 地图");
        if (broadcast.Stage is null) return Fail(broadcast.StageText.Length == 0 ? "实时文件未提供当前关卡" : "无法识别关卡：" + broadcast.StageText);
        if (data is null) return Fail("正在获取该地图奖励…");
        if (data.Rewards is null) return Fail(data.Error);
        if (!data.Rewards.TryGetValue(broadcast.Stage.Value, out var reward)) return Fail("未找到第 " + broadcast.Stage + " 关的奖励配置");
        var source = data.Offline ? "离线缓存 · " + data.FetchedAt?.ToLocalTime().ToString("MM-dd HH:mm") : "官方配置 · 已同步";
        return new(true, broadcast.Map, broadcast.Title, broadcast.Stage, reward, source, data.Offline ? "联网失败，使用上次成功获取的配置" : "伤害转化比 = 伤害 : 1 点货币");
    }
    public static string Ratio(int number) => number == 0 ? "未启用" : number.ToString("N0", CultureInfo.InvariantCulture) + " : 1";
}

public sealed class RewardRepository : IDisposable
{
    private readonly HttpClient http;
    private readonly string cacheDirectory;
    private readonly Dictionary<string, (RewardData Data, DateTimeOffset RetryAt)> memory = new(StringComparer.OrdinalIgnoreCase);
    public RewardRepository(string cacheDirectory, HttpClient? http = null)
    {
        this.cacheDirectory = cacheDirectory;
        this.http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        this.http.DefaultRequestHeaders.UserAgent.ParseAdd("FysZeRewardOverlay/1.0");
    }
    public void Invalidate() => memory.Clear();
    public async Task<RewardData> GetAsync(string map)
    {
        if (!Regex.IsMatch(map, @"^ze_[a-z0-9_-]{1,120}$")) return new(null, null, false, "地图名称无效");
        var now = DateTimeOffset.UtcNow;
        if (memory.TryGetValue(map, out var existing) && existing.RetryAt > now) return existing.Data;
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
                var rewards = RewardParser.Parse(body);
                result = new(rewards, now, false, "");
                try
                {
                    Directory.CreateDirectory(cacheDirectory);
                    AtomicFile.Write(path, JsonSerializer.Serialize(new CacheEnvelope(map, now, body)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Online display still works without disk cache. */ }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException or InvalidOperationException)
        {
            result = new(null, null, false, "奖励获取失败，请检查网络后重试");
            try
            {
                if (File.Exists(path))
                {
                    var cache = JsonSerializer.Deserialize<CacheEnvelope>(File.ReadAllText(path));
                    if (cache?.Map == map && cache.FetchedAt <= DateTimeOffset.UtcNow.AddMinutes(5))
                        result = new(RewardParser.Parse(cache.Body), cache.FetchedAt, true, "");
                }
            }
            catch (Exception cacheError) when (cacheError is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidOperationException or ArgumentException) { }
        }
        memory[map] = (result, now.Add(result.Rewards is not null && !result.Offline ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(30)));
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

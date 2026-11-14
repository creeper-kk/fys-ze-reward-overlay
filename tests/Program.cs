using System.Net;
using System.Text;
using FysZeRewardOverlay;

int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); Console.WriteLine("PASS " + name); checks++; }
const string json = """
// official reward field names
{ "1": { "rankPasses":30,"rankDamage":35000,"econPasses":25,"econDamage":38000 },
  "4": { "rankPasses":18,"rankDamage":20000,"econPasses":12,"econDamage":24000, }, }
""";
var parsed = RewardParser.Parse(json);
var broadcast = BroadcastReader.Parse("区服: [风云社] 僵尸逃跑①\n地图: ze_ffxii_westersand_v7_t\n译名: 西沙\n关卡: 4/4");
Check(broadcast.Map == "ze_ffxii_westersand_v7_t" && broadcast.Stage == 4 && broadcast.Title == "西沙", "actual-format map and numeric stage");
Check(BroadcastReader.Parse("地图：ZE_LEGO\r\n当前关卡：第 1 关").Stage == 1, "CRLF, fullwidth punctuation, stage label");
Check(BroadcastReader.Parse("地图: ze_lego\n关卡: EX1").Stage is null, "do not guess extreme stage offset");
Check(BroadcastReader.Parse("地图: ze_lego\n关卡: 0").Stage is null, "reject zero stage");
Check(BroadcastReader.Parse("地图: ze_lego\n关卡: -1").Stage is null, "reject negative stage");
Check(!BroadcastReader.Parse("地图: ze_../secrets\n关卡: 1").IsZe, "reject path traversal");
Check(BroadcastReader.Parse("地图: ze_lego\n地图: ze_new\n关卡: 1").Error.Length > 0, "reject incomplete mixed snapshot");
Check(BroadcastReader.Parse("\uFEFF地图: ze_lego\n关卡: 单关").Stage == 1, "BOM and explicit single-stage text");
var data = new RewardData(parsed, DateTimeOffset.UtcNow, false, "");
var result = DisplayInfo.Resolve(broadcast, data);
Check(result.Available && result.Reward!.RankPasses == 18 && result.Reward.RankDamage == 20000 && result.Reward.EconPasses == 12 && result.Reward.EconDamage == 24000, "exact stage and all four rewards");
Check(!DisplayInfo.Resolve(broadcast with { Stage = 3 }, data).Available, "missing stage never falls back to stage one");
Check(!DisplayInfo.Resolve(broadcast with { Stage = null }, data).Available, "missing broadcast stage is unavailable");
Check(!DisplayInfo.Resolve(BroadcastReader.Parse("地图: fys_lobby_liyue"), data).Available, "non-ZE unavailable");
Check(!DisplayInfo.Resolve(BroadcastReader.Parse(""), data).Available, "empty input unavailable");
Check(!DisplayInfo.Resolve(BroadcastReader.Parse("还没有在<风云社>服务器内QAQ\n官方Q群: 654277366"), data).Available, "real disconnected broadcast unavailable");
Check(DisplayInfo.Resolve(broadcast, data with { Offline = true }).Source.StartsWith("离线缓存"), "offline cache visibly marked");
Check(DisplayInfo.Ratio(35000) == "35,000 : 1" && DisplayInfo.Ratio(0) == "未启用", "ratio formatting and zero");
bool rejected = false;
try { RewardParser.Parse("{\"1\":{\"rankPasses\":2}}"); } catch (FormatException) { rejected = true; }
Check(rejected, "missing fields are never silently zero");
Check(Shortcut.TryParse("Ctrl+Alt+R", out var one) && one.Modifiers == 3 && one.VirtualKey == 82, "hotkey modifiers and virtual key");
Check(!Shortcut.TryParse("R", out _) && !Shortcut.TryParse("Ctrl+F12", out _) && !Shortcut.TryParse("Alt+R+M", out _), "reject unsafe or invalid shortcuts");

var temporary = Path.Combine(Path.GetTempPath(), "FysOverlayTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    var input = Path.Combine(temporary, "broadcast.txt");
    File.WriteAllText(input, "地图: ze_lego\n关卡: 1", new UTF8Encoding(false));
    using (var writer = new FileStream(input, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        Check(BroadcastReader.Read(input).Stage == 1, "read while producer holds shared file handle");
    File.WriteAllText(input, "");
    Check(!DisplayInfo.Resolve(BroadcastReader.Read(input), data).Available, "truncated file clears previous data");
    File.WriteAllText(input, "地图: ze_lego\n关卡: 4", Encoding.Unicode);
    Check(BroadcastReader.Read(input).Stage == 4, "UTF-16 input autodetection");
    File.Delete(input);
    Check(BroadcastReader.Read(input).Error.Length > 0, "deleted input handled");

    var handler = new FakeHandler { Body = json };
    using var repo = new RewardRepository(Path.Combine(temporary, "cache"), new HttpClient(handler));
    var online = await repo.GetAsync("ze_test");
    Check(online.Rewards![4].EconPasses == 12 && !online.Offline, "online fetch");
    await repo.GetAsync("ze_test");
    Check(handler.Requests == 1, "throttle repeated requests");
    handler.Fail = true; repo.Invalidate();
    var cached = await repo.GetAsync("ze_test");
    Check(cached.Offline && cached.Rewards![1].RankPasses == 30, "network failure falls back to disk cache");
    Check((await repo.GetAsync("ze_missing")).Rewards is null, "offline map without cache unavailable");
    handler.Fail = false; handler.Status = HttpStatusCode.NotFound; repo.Invalidate();
    Check((await repo.GetAsync("ze_test")).Rewards is null, "authoritative 404 does not display removed cached rewards");
    handler.Status = HttpStatusCode.OK; handler.Body = "html error"; repo.Invalidate();
    Check((await repo.GetAsync("ze_test")).Offline, "malformed response cannot overwrite valid cache");
    var cachePath = Path.Combine(temporary, "cache", "ze_test.json");
    File.WriteAllText(cachePath, "broken"); handler.Fail = true; repo.Invalidate();
    Check((await repo.GetAsync("ze_test")).Rewards is null, "corrupted cache safely unavailable");
    var settings = new Settings { Scale = 1.25, ToggleHotkey = "Ctrl+Shift+F8", Left = -100 };
    settings.Save(temporary);
    var loaded = Settings.Load(temporary);
    Check(loaded.Scale == 1.25 && loaded.ToggleHotkey == settings.ToggleHotkey && loaded.Left == -100, "settings persist including negative monitor coordinates");
}
finally { Directory.Delete(temporary, true); }
Console.WriteLine($"All {checks} checks passed.");

sealed class FakeHandler : HttpMessageHandler
{
    public string Body = "";
    public bool Fail;
    public int Requests;
    public HttpStatusCode Status = HttpStatusCode.OK;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        if (Fail) throw new HttpRequestException("simulated offline");
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
    }
}

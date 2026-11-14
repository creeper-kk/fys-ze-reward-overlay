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
Check(OverlayOptions.TryIntervals("0.25", "3", out var poll, out var refresh, out _) && poll == 250 && refresh == 3, "custom update intervals parsed with correct units");
Check(!OverlayOptions.TryIntervals("0.1", "10", out _, out _, out _) && !OverlayOptions.TryIntervals("61", "10", out _, out _, out _), "poll interval bounds enforced");
Check(!OverlayOptions.TryIntervals("NaN", "10", out _, out _, out _) && !OverlayOptions.TryIntervals("1", "0", out _, out _, out _) && !OverlayOptions.TryIntervals("1", "1441", out _, out _, out _), "invalid and excessive update intervals rejected");
Check(BroadcastFiles.TryNormalize("\"%LOCALAPPDATA%\\fys\\fys.broadcast.assistant.txt\"", out var expanded) && expanded == BroadcastFiles.DefaultPath, "manual path supports environment variables and quotes");
Check(!BroadcastFiles.TryNormalize("relative.txt", out _) && !BroadcastFiles.TryNormalize(Path.GetTempPath(), out _), "manual path rejects relative paths and directories");

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
    Check(BroadcastReader.Read(input).FileUnavailable, "deleted input offers file selection");
    Check(!BroadcastReader.Parse("还没有在<风云社>服务器内QAQ").FileUnavailable, "disconnected content does not prompt for another file");
    Check(BroadcastFiles.TryNormalize(input, out var pending) && BroadcastReader.Read(pending).FileUnavailable, "manual missing path can be saved awaiting file creation");
    File.WriteAllText(pending, "地图: ze_lego\n关卡: 1");
    Check(!BroadcastReader.Read(pending).FileUnavailable && BroadcastReader.Read(pending).Stage == 1, "manual source recovers when file appears");

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
    settings.PollIntervalMilliseconds = 250; settings.RewardRefreshMinutes = 3;
    settings.OverlayFontFamily = "Consolas";
    settings.HiddenFields = OverlayField.MapName | OverlayField.RankDamage | OverlayField.Source;
    settings.Save(temporary); loaded = Settings.Load(temporary);
    Check(loaded.PollIntervalMilliseconds == 250 && loaded.RewardRefreshMinutes == 3 && loaded.OverlayFontFamily == "Consolas", "font and update settings persist");
    Check(loaded.Hides(OverlayField.RankDamage) && loaded.Hides(OverlayField.MapName) && !loaded.Hides(OverlayField.RankPasses), "hidden fields persist independently");
    var copy = loaded.Copy(); copy.HiddenFields = OverlayField.None;
    Check(loaded.Hides(OverlayField.MapName), "cancelling edited visibility does not mutate saved settings");
    File.WriteAllText(Path.Combine(temporary, "settings.json"), """
    { "ToggleHotkey":"Alt+P", "MoveHotkey":"Alt+O", "BackgroundOpacity":0.15,"Scale":0.9,"Left":2057,"Top":0,"Locked":true }
    """);
    loaded = Settings.Load(temporary);
    Check(loaded.ToggleHotkey == "Alt+P" && loaded.MoveHotkey == "Alt+O" && loaded.BackgroundOpacity == 0.15 && loaded.Left == 2057, "upgrade preserves existing personal settings");
    Check(loaded.PollIntervalMilliseconds == 600 && loaded.RewardRefreshMinutes == 10 && loaded.HiddenFields == OverlayField.None && loaded.OverlayFontFamily == FontCatalog.DefaultName, "old settings receive safe defaults for new fields");
    loaded.PollIntervalMilliseconds = 0; loaded.RewardRefreshMinutes = 99999; loaded.Save(temporary);
    loaded = Settings.Load(temporary);
    Check(loaded.PollIntervalMilliseconds == 200 && loaded.RewardRefreshMinutes == 1440, "out of range stored intervals are clamped");

    var clock = new FakeTimeProvider();
    var timedHandler = new FakeHandler { Body = json };
    using var timedRepo = new RewardRepository(Path.Combine(temporary, "timed-cache"), new HttpClient(timedHandler), clock);
    await timedRepo.GetAsync("ze_timer"); clock.Advance(TimeSpan.FromMinutes(2)); await timedRepo.GetAsync("ze_timer");
    Check(timedHandler.Requests == 1, "reward refresh obeys existing interval");
    timedRepo.RefreshInterval = TimeSpan.FromMinutes(1); await timedRepo.GetAsync("ze_timer");
    Check(timedHandler.Requests == 2, "shorter interval takes effect on existing cached data");
    timedRepo.RefreshInterval = TimeSpan.FromHours(1); clock.Advance(TimeSpan.FromMinutes(2)); await timedRepo.GetAsync("ze_timer");
    Check(timedHandler.Requests == 2, "longer interval avoids premature refresh");
    clock.Advance(TimeSpan.FromMinutes(58)); await timedRepo.GetAsync("ze_timer");
    Check(timedHandler.Requests == 3, "configured interval expires at requested boundary");
    timedHandler.Fail = true; timedRepo.Invalidate(); await timedRepo.GetAsync("ze_timer");
    clock.Advance(TimeSpan.FromSeconds(29)); await timedRepo.GetAsync("ze_timer");
    Check(timedHandler.Requests == 4, "offline retry remains throttled independently");
    clock.Advance(TimeSpan.FromSeconds(1)); await timedRepo.GetAsync("ze_timer");
    Check(timedHandler.Requests == 5, "offline retry still occurs after 30 seconds");
}
finally { Directory.Delete(temporary, true); }
Console.WriteLine($"All {checks} checks passed.");

sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan elapsed) => now += elapsed;
}

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

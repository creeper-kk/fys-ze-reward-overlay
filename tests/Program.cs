using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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
var svipReward = new Reward(5, 20000, 12, 24000);
Check(svipReward.GetRankPasses(false) == 5 && svipReward.GetRankPasses(true) == 6 &&
    (svipReward with { RankPasses = 2 }).GetRankPasses(true) == 2 &&
    (svipReward with { RankPasses = 3 }).GetRankPasses(true) == 3 &&
    (svipReward with { RankPasses = 4 }).GetRankPasses(true) == 4 &&
    (svipReward with { RankPasses = int.MaxValue }).GetRankPasses(true) == 2_576_980_376,
    "SVIP floors the base cloud points and supports the full reward range");
Check(!DisplayInfo.Resolve(broadcast with { Stage = 3 }, data).Available, "missing stage never falls back to stage one");
Check(!DisplayInfo.Resolve(broadcast with { Stage = null }, data).Available, "missing broadcast stage is unavailable");
Check(!DisplayInfo.Resolve(BroadcastReader.Parse("地图: fys_lobby_liyue"), data).Available, "non-ZE unavailable");
Check(!DisplayInfo.Resolve(BroadcastReader.Parse(""), data).Available, "empty input unavailable");
Check(!DisplayInfo.Resolve(BroadcastReader.Parse("还没有在<风云社>服务器内QAQ\n官方Q群: 654277366"), data).Available, "real disconnected broadcast unavailable");
Check(DisplayInfo.Resolve(broadcast, data with { Offline = true }).Source.StartsWith("离线缓存"), "offline cache visibly marked");
Check(DisplayInfo.Ratio(35000) == "35,000 : 1" && DisplayInfo.Ratio(0) == "未启用", "ratio formatting and zero");
Check(DamageProgress.Calculate(20, 20000) == new DamageProgress(1, 20, 0) &&
    DamageProgress.Calculate(20, 24000) == new DamageProgress(0, 4, 20d / 24), "20 kills earn one cloud point, reset its bar and fill 20/24 of a dragon crystal");
Check(DamageProgress.Calculate(60, 20000) == new DamageProgress(3, 20, 0) &&
    DamageProgress.Calculate(60, 24000) == new DamageProgress(2, 12, 0.5), "60 kills reach three cloud points and two and a half dragon crystals");
Check(DamageProgress.Calculate(19, 20000) == new DamageProgress(0, 1, 0.95) &&
    DamageProgress.Calculate(21, 20000) == new DamageProgress(1, 19, 0.05) &&
    DamageProgress.Calculate(0, 20000) == new DamageProgress(0, 20, 0), "progress starts empty and wraps after an exact threshold");
Check(DamageProgress.Calculate(null, 20000) is null && DamageProgress.Calculate(20, 0) is null, "no progress when kills are absent or conversion is disabled");
bool rejected = false;
try { RewardParser.Parse("{\"1\":{\"rankPasses\":2}}"); } catch (FormatException) { rejected = true; }
Check(rejected, "missing fields are never silently zero");
Check(Shortcut.TryParse("Ctrl+Alt+R", out var one) && one.Modifiers == 3 && one.VirtualKey == 82, "hotkey modifiers and virtual key");
Check(Shortcut.TryParse("Shift+=", out var up) && up == new Shortcut(4, 0xbb) &&
    Shortcut.TryParse("Shift+-", out var down) && down == new Shortcut(4, 0xbd), "stage hotkeys use the equals and minus keys");
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
    var steam = Path.Combine(temporary, "Steam");
    var steamApps = Directory.CreateDirectory(Path.Combine(steam, "steamapps")).FullName;
    var library = Path.Combine(temporary, "Steam 库");
    var libraryApps = Directory.CreateDirectory(Path.Combine(library, "steamapps")).FullName;
    var escapedLibrary = library.Replace(@"\", @"\\");
    var libraryList = Path.Combine(steamApps, "libraryfolders.vdf");
    File.WriteAllText(libraryList, $$"""
        "libraryfolders"
        {
            "1"
            {
                "path" "{{escapedLibrary}}"
                "apps"
                {
                    "730" "100"
                }
            }
        }
        """);
    File.WriteAllText(Path.Combine(libraryApps, "appmanifest_730.acf"), """
        "AppState"
        {
            "appid" "730"
            "installdir" "Custom CS2 directory"
        }
        """);
    var game = Path.Combine(libraryApps, "common", "Custom CS2 directory", "game");
    var cfg = Directory.CreateDirectory(Path.Combine(game, "csgo", "cfg")).FullName;
    Check(SteamCs2.FindConfigDirectory(new[] { steam }) is null, "incomplete CS2 install has no automatic destination");
    var exeDir = Directory.CreateDirectory(Path.Combine(game, "bin", "win64")).FullName;
    File.WriteAllText(Path.Combine(exeDir, "cs2.exe"), "");
    Check(SteamCs2.FindConfigDirectory(new[] { steam }) == cfg, "locate CS2 in a secondary Steam library using the install manifest");
    File.WriteAllText(libraryList, $$"""
        "LibraryFolders"
        {
            "1" "{{escapedLibrary}}"
        }
        """);
    Check(SteamCs2.FindConfigDirectory(new[] { steam }) == cfg, "locate CS2 from the older Steam library format");

    const string repository = "https://github.com/creeper-kk/fys-ze-reward-overlay";
    var releaseHandler = new FakeHandler { Body = """{"tag_name":"v1.2.10"}""" };
    using var releaseClient = new HttpClient(releaseHandler);
    var latestRelease = await ReleaseCheck.GetLatestAsync(repository, "1.2.9", releaseClient);
    Check(latestRelease.Comparison > 0 && latestRelease.PageUrl == repository + "/releases/tag/v1.2.10",
        "release check compares numeric version components and links to the new release");
    Check((await ReleaseCheck.GetLatestAsync(repository, "1.2.10.0", releaseClient)).Comparison == 0 &&
        (await ReleaseCheck.GetLatestAsync(repository, "1.3.0", releaseClient)).Comparison < 0,
        "release check recognizes matching and newer local versions");
    releaseHandler.Body = """{"tag_name":"latest"}""";
    bool invalidRelease = false;
    try { await ReleaseCheck.GetLatestAsync(repository, "1.2.9", releaseClient); }
    catch (InvalidDataException) { invalidRelease = true; }
    Check(invalidRelease, "release check reports an invalid tag");

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
    var cachePath = Path.Combine(temporary, "cache", "ze_test.json");
    using (var cache = JsonDocument.Parse(File.ReadAllText(cachePath)))
    using (var compact = JsonDocument.Parse(cache.RootElement.GetProperty("Body").GetString()!))
        Check(RewardParser.Parse(compact.RootElement).SequenceEqual(parsed), "downloaded JSONC is cached as compact valid JSON with identical rewards");
    await repo.GetAsync("ze_test");
    Check(handler.Requests == 1, "throttle repeated requests");
    handler.Fail = true; repo.Invalidate();
    var cached = await repo.GetAsync("ze_test");
    Check(cached.Offline && cached.Rewards![1].RankPasses == 30, "network failure falls back to disk cache");
    var bundled = repo.GetLocalFallback(broadcast.Map);
    Check(bundled is { Offline: true, Bundled: true } && bundled.Rewards![4] == parsed[4] && handler.Requests == 2,
        "a fresh install can resolve bundled rewards before making a network request");
    Check(DisplayInfo.Resolve(broadcast, await repo.GetAsync(broadcast.Map)).Source.StartsWith("离线配置"),
        "bundled rewards are visibly marked when the first fetch fails");
    var bundledCache = Path.Combine(temporary, "cache", broadcast.Map + ".json");
    File.WriteAllText(bundledCache, "broken");
    Check(repo.GetLocalFallback(broadcast.Map) is { Bundled: true }, "a damaged cache falls back to the snapshot");
    File.WriteAllText(bundledCache, JsonSerializer.Serialize(new { broadcast.Map, FetchedAt = DateTimeOffset.UtcNow, Body = json }));
    Check(repo.GetLocalFallback(broadcast.Map) is { Bundled: false, Rewards.Count: 2 },
        "the legacy JSONC cache takes precedence over the snapshot");
    Check((await repo.GetAsync("ze_missing")).Rewards is null, "offline map missing from both cache and snapshot is unavailable");
    handler.Fail = false; handler.Status = HttpStatusCode.NotFound; repo.Invalidate();
    Check((await repo.GetAsync(broadcast.Map)).Rewards is null, "authoritative 404 takes precedence over local rewards");
    handler.Status = HttpStatusCode.OK; handler.Body = "html error"; repo.Invalidate();
    Check((await repo.GetAsync("ze_test")).Offline, "malformed response cannot overwrite valid cache");
    File.WriteAllText(cachePath, "broken"); handler.Fail = true; repo.Invalidate();
    Check((await repo.GetAsync("ze_test")).Rewards is null, "corrupted cache safely unavailable");
    using (var snapshot = typeof(RewardRepository).Assembly.GetManifestResourceStream("rewards.json")!)
    using (var snapshotJson = JsonDocument.Parse(snapshot))
        Check(snapshotJson.RootElement.EnumerateObject().All(map => RewardParser.Parse(map.Value).Count > 0),
            "the entire bundled snapshot is readable by the reward parser");
    var settings = new Settings { Scale = 1.25, ToggleHotkey = "Ctrl+Shift+F8", Left = -100, IsSvip = true };
    settings.Save(temporary);
    var loaded = Settings.Load(temporary);
    Check(loaded.Scale == 1.25 && loaded.ToggleHotkey == settings.ToggleHotkey && loaded.Left == -100, "settings persist including negative monitor coordinates");
    Check(loaded.IsSvip && Settings.Import(Path.Combine(temporary, "settings.json")).IsSvip && !Settings.CreateDefault().IsSvip,
        "SVIP persists in the profile and is off by default");
    settings.PollIntervalMilliseconds = 250; settings.RewardRefreshMinutes = 3;
    settings.OverlayFontFamily = "Consolas";
    settings.RowSpacing = 14;
    settings.ColumnSpacing = -16;
    settings.ValueAlignment = RewardValueAlignment.Center;
    settings.HiddenFields = OverlayField.MapName | OverlayField.RankDamage | OverlayField.Source;
    settings.Save(temporary); loaded = Settings.Load(temporary);
    Check(loaded.PollIntervalMilliseconds == 250 && loaded.RewardRefreshMinutes == 3 && loaded.OverlayFontFamily == "Consolas", "font and update settings persist");
    Check(loaded.RowSpacing == 14 && loaded.ColumnSpacing == -16, "row spacing and tighter columns persist");
    Check(loaded.ValueAlignment == RewardValueAlignment.Center, "reward value alignment persists");
    Check(loaded.Hides(OverlayField.RankDamage) && loaded.Hides(OverlayField.MapName) && !loaded.Hides(OverlayField.RankPasses), "hidden fields persist independently");
    settings.DisplayOrder = new[] { OverlayField.EconPasses, OverlayField.MapName, OverlayField.EconPasses, OverlayField.Source, (OverlayField)9999 };
    settings.Save(temporary); loaded = Settings.Load(temporary);
    var expectedOrder = new[] { OverlayField.EconPasses, OverlayField.MapName }.Union(OverlayOptions.ReorderableFields);
    Check(loaded.DisplayOrder.SequenceEqual(expectedOrder) && Settings.Import(Path.Combine(temporary, "settings.json")).DisplayOrder.SequenceEqual(expectedOrder),
        "display order round-trips, ignores unknown or repeated rows, and fills in missing rows");
    var copy = loaded.Copy(); copy.HiddenFields = OverlayField.None;
    copy.DisplayOrder[0] = OverlayField.Stage;
    Check(loaded.Hides(OverlayField.MapName) && loaded.DisplayOrder[0] == OverlayField.EconPasses,
        "cancelling edited visibility and order does not mutate saved settings");
    settings.GlobalTextStyle = new(21, "#FEDCBA", 500);
    settings.FieldTextStyles[OverlayField.Stage] = new(Size: 36, Weight: 800);
    settings.FieldTextStyles[OverlayField.Unavailable] = new(Color: "#abcdef");
    settings.Save(temporary);
    loaded = Settings.Load(temporary);
    Check(loaded.TextStyleFor(OverlayField.Stage) == new OverlayTextStyle(36, "#FEDCBA", 800) &&
        loaded.TextStyleFor(OverlayField.RankDamage) == loaded.GlobalTextStyle, "typography persists with per-property inheritance");
    Check(loaded.TextStyleFor(OverlayField.Unavailable).Color == "#ABCDEF", "always-visible messages support typography overrides");
    var typographyCopy = loaded.Copy();
    typographyCopy.FieldTextStyles.Remove(OverlayField.Stage);
    Check(loaded.FieldTextStyles.ContainsKey(OverlayField.Stage) && typographyCopy.TextStyleFor(OverlayField.Stage) == loaded.GlobalTextStyle,
        "resetting or cancelling one field does not mutate persisted typography");
    settings.FieldTextStyles[OverlayField.Stage] = new(Size: 99, Color: "invalid", Weight: 5);
    settings.Save(temporary);
    Check(Settings.Load(temporary).TextStyleFor(OverlayField.Stage) == settings.GlobalTextStyle, "invalid stored overrides fall back to the global style");
    settings.MapSource = MapSource.Gsi;
    settings.BroadcastPath = "";
    settings.StageUpHotkey = "Ctrl+Shift+F6";
    settings.StageDownHotkey = "Ctrl+Shift+F7";
    settings.Save(temporary);
    loaded = Settings.Load(temporary);
    Check(loaded.MapSource == MapSource.Gsi && loaded.BroadcastPath == "", "GSI mode persists with no stage TXT");
    Check(loaded.StageUpHotkey == "Ctrl+Shift+F6" && loaded.StageDownHotkey == "Ctrl+Shift+F7", "custom stage hotkeys persist");
    File.WriteAllText(Path.Combine(temporary, "settings.json"), """
    { "ToggleHotkey":"Alt+P", "MoveHotkey":"Alt+O", "BackgroundOpacity":0.15,"Scale":0.9,"Left":2057,"Top":0,"Locked":true }
    """);
    loaded = Settings.Load(temporary);
    Check(loaded.ToggleHotkey == "Alt+P" && loaded.MoveHotkey == "Alt+O" && loaded.BackgroundOpacity == 0.15 && loaded.Left == 2057, "upgrade preserves existing personal settings");
    Check(!loaded.IsSvip, "existing profiles default to non-SVIP");
    Check(loaded.DisplayOrder.SequenceEqual(OverlayOptions.ReorderableFields), "existing profiles retain the original row order");
    Check(loaded.MapSource == MapSource.Broadcast && loaded.BroadcastPath == BroadcastFiles.DefaultPath, "existing settings default to the original TXT source");
    Check(loaded.RowSpacing == 0 && loaded.ColumnSpacing == 0, "old settings retain the default spacing");
    Check(loaded.ValueAlignment == RewardValueAlignment.Right, "old settings retain right-aligned values");
    Check(loaded.StageUpHotkey == "Shift+=" && loaded.StageDownHotkey == "Shift+-", "old settings receive the default stage hotkeys");
    Check(loaded.GlobalTextStyle == new OverlayTextStyle() && loaded.FieldTextStyles.Count == 0, "existing settings keep the original per-field typography");
    Check(loaded.PollIntervalMilliseconds == 600 && loaded.RewardRefreshMinutes == 10 &&
        loaded.HiddenFields == new Settings().HiddenFields && loaded.OverlayFontFamily == FontCatalog.DefaultName && !loaded.ObsEnabled,
        "old settings receive current defaults for absent fields");
    loaded.PollIntervalMilliseconds = 0; loaded.RewardRefreshMinutes = 99999; loaded.Save(temporary);
    loaded = Settings.Load(temporary);
    Check(loaded.PollIntervalMilliseconds == 200 && loaded.RewardRefreshMinutes == 1440, "out of range stored intervals are clamped");

    var exported = Path.Combine(temporary, "backup.json");
    settings.Locked = false;
    settings.ObsEnabled = true;
    settings.Export(exported);
    var imported = Settings.Import(exported);
    imported.Export(exported);
    Check(imported.MapSource == MapSource.Gsi && imported.BroadcastPath == "" && imported.Left == -100 &&
        !imported.Locked && imported.ObsEnabled && imported.ValueAlignment == RewardValueAlignment.Center &&
        imported.TextStyleFor(OverlayField.Unavailable).Color == "#ABCDEF",
        "configuration round trip preserves source, window placement, lock and appearance");
    var defaultSettings = Settings.Load(Path.Combine(temporary, "fresh"));
    Check(defaultSettings.ToggleHotkey == "Alt+P" && defaultSettings.MoveHotkey == "Alt+O" &&
        defaultSettings.Scale == 0.85 && defaultSettings.RowSpacing == 6 && defaultSettings.ColumnSpacing == -24 &&
        defaultSettings.ValueAlignment == RewardValueAlignment.Left && defaultSettings.GlobalTextStyle.Size == 28 &&
        defaultSettings.PollIntervalMilliseconds == 10000 && defaultSettings.RewardRefreshMinutes == 1440 &&
        defaultSettings.Left == 2234 && defaultSettings.Top == 250 && defaultSettings.Locked && !defaultSettings.ObsEnabled &&
        defaultSettings.Hides(OverlayField.ErrorDetail) && !defaultSettings.Hides(OverlayField.MapName),
        "fresh install uses the same layout and shortcuts as restoring defaults");
    var persistedBeforeImport = File.ReadAllText(Path.Combine(temporary, "settings.json"));
    foreach (string invalid in new[] { "{", "null", "{}", """{"unrelated":true}""", """{"Scale":"invalid"}""", """{"BroadcastPath":"relative.txt"}""" })
    {
        File.WriteAllText(exported, invalid);
        bool importRejected = false;
        try { Settings.Import(exported); }
        catch (System.Text.Json.JsonException) { importRejected = true; }
        Check(importRejected && File.ReadAllText(Path.Combine(temporary, "settings.json")) == persistedBeforeImport,
            "invalid import is rejected without overwriting saved settings: " + invalid);
    }

    var clock = new FakeTimeProvider();
    int port;
    using (var socket = new TcpListener(IPAddress.Loopback, 0))
    {
        socket.Start();
        port = ((IPEndPoint)socket.LocalEndpoint).Port;
    }
    using (var gsi = new GsiListener(port, clock))
    using (var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) })
    {
        var endpoint = $"http://127.0.0.1:{port}/";
        async Task<HttpStatusCode> Push(string body)
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(endpoint, content);
            return response.StatusCode;
        }
        Check(gsi.Read(input).Map == "", "GSI ignores TXT before the first game update");
        Check(await Push("""{"map":{"name":"ZE_LEGO 僵尸逃跑①","round":4,"team_ct":{"score":2}}}""") == HttpStatusCode.OK, "loopback receiver accepts a CS2 GSI POST");
        var mapOnly = gsi.Read("");
        Check(mapOnly is { Map: "ze_lego", Stage: 3, CtScore: 2, FileUnavailable: false }, "GSI uses CT score plus one independently of the round number");
        var singleStage = data with { Rewards = new() { [4] = parsed[4] } };
        Check(DisplayInfo.Resolve(mapOnly with { Stage = 5 }, singleStage) is { Available: true, Stage: 4, StageInferred: false, InferredStageMissingReward: false } single &&
            single.Reward == parsed[4], "GSI uses the only configured stage regardless of CT score and offset");
        Check(DisplayInfo.Resolve(mapOnly, null) is { Stage: null, StageInferred: false }, "GSI waits for the reward config before displaying an inferred stage");
        var missing = DisplayInfo.Resolve(mapOnly, data);
        Check(!missing.Available && missing.InferredStageMissingReward, "a missing inferred stage is flagged for manual correction");
        Check(!DisplayInfo.Resolve(mapOnly, data with { Rewards = null }).InferredStageMissingReward, "a retrieval failure is not a missing stage");
        File.WriteAllText(input, "地图: ze_lego\n译名: 乐高\n关卡: 4/4");
        var matched = gsi.Read(input);
        Check(matched.Title == "乐高" && matched.CtScore is null && DisplayInfo.Resolve(matched, data).Reward == parsed[4], "same-map TXT takes precedence over a score");
        Check(!DisplayInfo.Resolve(matched with { Stage = 1 }, singleStage).Available, "an explicit TXT stage still requires an exact reward match");
        Check(await Push("""{"map":{"name":"ze_lego 僵尸逃跑①"},"provider":{"steamid":"101"},"player":{"steamid":"101","match_stats":{"kills":20}}}""") == HttpStatusCode.OK &&
            DisplayInfo.Resolve(gsi.Read(input), data).Kills == 20, "local kills supplement a recognized TXT stage");
        var noScore = gsi.Read("");
        Check(DisplayInfo.Resolve(noScore, singleStage with { Offline = true }) is { Available: true, Stage: 4, StageInferred: false, Kills: 20 } cachedSingle &&
            cachedSingle.Source.StartsWith("离线缓存"), "one cached stage is available without a CT score and preserves kill progress");
        Check(!DisplayInfo.Resolve(noScore, data).Available, "multiple reward stages still require a TXT stage or CT score");
        Check(await Push("""{"map":{"name":"ze_lego 僵尸逃跑①"},"provider":{"steamid":"101"},"player":{"steamid":"102","match_stats":{"kills":60}}}""") == HttpStatusCode.OK &&
            gsi.Read(input).Kills is null, "spectating another player clears the local kill progress");
        Check(await Push("""{"map":{"name":"ZE_LEGO 僵尸逃跑①","team_ct":{"score":2}}}""") == HttpStatusCode.OK, "restore score for stage tests");
        File.WriteAllText(input, "区服: [风云社] 僵尸逃跑②\n地图: ze_lego\n关卡: 4/4");
        Check(gsi.Read(input) is { Map: "ze_lego", Stage: 3, CtScore: 2 }, "same map on a different FYS server falls back to the CT score");
        File.WriteAllText(input, "区服: [风云社] 僵尸逃跑1服\n地图: ze_lego\n关卡: 2/4");
        Check(gsi.Read(input).Stage == 2, "circled and numeric server labels match");
        File.WriteAllText(input, "地图: ze_lego\n译名: 乐高\n关卡: EX1");
        Check(gsi.Read(input) is { Title: "乐高", Stage: 3 }, "unrecognized TXT stage falls back to CT score and retains the title");
        Check(await Push("""{"map":{"name":"ze_lego","team_ct":{"score":0},"team_t":{"score":7}}}""") == HttpStatusCode.OK &&
            gsi.Read("") is { Stage: 1, CtScore: 0 }, "a zero CT score is stage one irrespective of T score");
        Check(await Push("""{"map":{"name":"ze_lego","team_ct":{"score":1}}}""") == HttpStatusCode.OK &&
            gsi.Read("") is { Stage: 2, CtScore: 1 }, "one CT point is stage two");
        Check(await Push("""{"map":{"name":"ze_lego","team_ct":{"score":"bad"}}}""") == HttpStatusCode.OK &&
            gsi.Read("").Stage is null, "an invalid CT score cannot infer a stage");
        Check(await Push("""{"map":{"name":"workshop/123456/ze_other"}}""") == HttpStatusCode.OK, "GSI accepts a workshop map path");
        Check(gsi.Read(input) is { Map: "ze_other", Stage: null }, "map change blocks a different map's TXT stage");
        Check(await Push("{") == HttpStatusCode.BadRequest && gsi.Read(input).Map == "ze_other", "malformed payload leaves the latest good map intact");
        Check(await Push("""{"provider":{"appid":730}}""") == HttpStatusCode.OK && gsi.Read(input).Map == "", "a payload without a map clears the old map");
        File.WriteAllText(input, "地图: ze_lego\n关卡: 4/4");
        Check(await Push("""{"map":{"name":"ze_lego"}}""") == HttpStatusCode.OK && gsi.Read(input).Stage == 4, "GSI recovers on reconnection");
        clock.Advance(TimeSpan.FromSeconds(15));
        Check(gsi.Read(input) is { Map: "", Stage: null }, "expired GSI snapshot cannot use the old TXT stage");
    }
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
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => now.UtcTicks;
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

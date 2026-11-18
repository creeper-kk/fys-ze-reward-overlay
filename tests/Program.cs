using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
    "SVIP floors the bonus with no damage points and supports the full reward range");
var rankReward = svipReward with { RankPasses = 4 };
Check(rankReward.GetRankPasses(true, 1) == 5 && rankReward.GetRankPasses(false, 1) == 4,
    "with one damage point, 4+1 becomes 5+1 for SVIP");
Check(rankReward.GetRankPasses(true, 5) == 5 && rankReward.GetRankPasses(true, 6) == 6 &&
    (rankReward with { RankPasses = 0 }).GetRankPasses(true, 5) == 1,
    "damage points count toward each SVIP bonus threshold while remaining a separate display value");
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
Check(DamageProgress.FromDamage(23501, 24000) == new DamageProgress(0, 0.499m, 23501d / 24000) &&
    DamageProgress.FromDamage(24000, 24000) == new DamageProgress(1, 24, 0) &&
    DamageProgress.FromDamage(0, 24000) == new DamageProgress(0, 24, 0),
    "native damage uses exact amounts and resets the bar at round and reward boundaries");
bool rejected = false;
try { RewardParser.Parse("{\"1\":{\"rankPasses\":2}}"); } catch (FormatException) { rejected = true; }
Check(rejected, "missing fields are never silently zero");
Check(Shortcut.TryParse("Ctrl+Alt+R", out var one) && one.Modifiers == 3 && one.VirtualKey == 82, "hotkey modifiers and virtual key");
Check(Shortcut.TryParse("Shift+=", out var up) && up == new Shortcut(4, 0xbb) &&
    Shortcut.TryParse("Shift+-", out var down) && down == new Shortcut(4, 0xbd), "stage hotkeys use the equals and minus keys");
Check(!Shortcut.TryParse("R", out _) && !Shortcut.TryParse("Ctrl+F12", out _) && !Shortcut.TryParse("Alt+R+M", out _), "reject unsafe or invalid shortcuts");
Check(OverlayOptions.TryIntervals("0.25", "3", out var poll, out var refresh, out _) && poll == 250 && refresh == 3, "custom update intervals parsed with correct units");
Check(!OverlayOptions.TryIntervals("0.1", "10", out _, out _, out _) && !OverlayOptions.TryIntervals("61", "10", out _, out _, out _), "poll interval bounds enforced");
Check(OverlayOptions.TryIntervals("1", "4320", out _, out var maxRefresh, out _) && maxRefresh == 4320, "the full three-day reward refresh interval is accepted");
Check(!OverlayOptions.TryIntervals("NaN", "10", out _, out _, out _) && !OverlayOptions.TryIntervals("1", "0", out _, out _, out _) && !OverlayOptions.TryIntervals("1", "4321", out _, out _, out _), "invalid and excessive update intervals rejected");
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
    releaseHandler.Body = """{"tag_name":"v1.2.5.1"}""";
    Check((await ReleaseCheck.GetLatestAsync(repository, "1.2.5", releaseClient)).Comparison > 0 &&
        (await ReleaseCheck.GetLatestAsync(repository, "1.2.5.1", releaseClient)).Comparison == 0,
        "release check recognizes four-part patch versions");
    releaseHandler.Body = """{"tag_name":"v1.2.5.2a"}""";
    Check((await ReleaseCheck.GetLatestAsync(repository, "1.2.5.2", releaseClient)).Comparison < 0 &&
        (await ReleaseCheck.GetLatestAsync(repository, "1.2.5.2a", releaseClient)).Comparison == 0 &&
        (await ReleaseCheck.GetLatestAsync(repository, "1.2.5.3", releaseClient)).Comparison < 0,
        "lettered builds sort before the final version with the same version number");
    releaseHandler.Body = """{"tag_name":"v1.2.5.2","body":"## Release notes\nFixed updater","assets":[{"name":"FysZeRewardOverlay-v1.2.5.2-win-x64.zip","size":50,"digest":"sha256:1234","browser_download_url":"https://github.com/creeper-kk/fys-ze-reward-overlay/releases/download/v1.2.5.2/FysZeRewardOverlay-v1.2.5.2-win-x64.zip"}]}""";
    var finalRelease = await ReleaseCheck.GetLatestAsync(repository, "1.2.5.2b", releaseClient);
    Check(finalRelease.Comparison > 0 && finalRelease.Notes == "## Release notes\nFixed updater" &&
        (RuntimeInformation.ProcessArchitecture != Architecture.X64 || finalRelease.Package is { Size: 50, Digest: "sha256:1234" }),
        "the final release is newer than b and includes its notes and Windows x64 package");
    Check((await ReleaseCheck.GetInstalledAsync(repository, "1.2.5.2", releaseClient))?.Notes == finalRelease.Notes &&
        releaseHandler.LastPath == "/repos/creeper-kk/fys-ze-reward-overlay/releases/tags/v1.2.5.2",
        "startup looks up the installed tag instead of the latest release");
    Check(await ReleaseCheck.GetInstalledAsync(repository, "1.2.5.1", releaseClient) is null, "startup never uses another version's notes");
    releaseHandler.Status = HttpStatusCode.NotFound;
    Check(await ReleaseCheck.GetInstalledAsync(repository, "1.2.5.2", releaseClient) is null, "unpublished versions have no online release notes");
    Check(ReleaseCheck.TryCompare("v1.3.0.1", "1.3.0.1a", out int kzReleaseOrder) && kzReleaseOrder > 0 &&
        ReleaseCheck.GetBundledRelease("1.3.0.1") is { Tag: "v1.3.0.1" } bundledKz &&
        bundledKz.Notes.Contains("KZ 地图探测") && bundledKz.Notes.Contains("非全球图") &&
        ReleaseCheck.GetBundledRelease("1.3.0.1a") is null,
        "the final KZ release contains the merged offline announcement and supersedes the preview build");
    releaseHandler.Status = HttpStatusCode.OK;
    releaseHandler.Body = """{"tag_name":"latest"}""";
    bool invalidRelease = false;
    try { await ReleaseCheck.GetLatestAsync(repository, "1.2.9", releaseClient); }
    catch (InvalidDataException) { invalidRelease = true; }
    Check(invalidRelease, "release check reports an invalid tag");

    var now = DateTimeOffset.UtcNow;
    var updateState = new UpdateState(now);
    Check(Settings.CreateDefault().AutoUpdateDays == 3 && new UpdateState().IsDue(3, now), "automatic updates default to checking every three days");
    Check(!updateState.IsDue(3, now.AddDays(3).AddSeconds(-1)) && updateState.IsDue(3, now.AddDays(3)) &&
        updateState.IsDue(7, now.AddDays(7)) && !updateState.IsDue(0, now.AddYears(1)),
        "automatic check schedule honors custom days and never mode");
    var updateProfile = Path.Combine(temporary, "update-profile");
    new Settings { AutoUpdateDays = 0 }.Save(updateProfile);
    Check(Settings.Load(updateProfile).AutoUpdateDays == 0, "never automatically update persists across restarts");

    var installed = ReleaseCheck.LocalVersion;
    var origin = Path.Combine(AppContext.BaseDirectory, "FysZeRewardOverlay.exe");
    var updater = PortableUpdates.ForProcess(updateProfile, origin, ["--data-dir", updateProfile])!;
    using var archiveStream = new MemoryStream();
    using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (string file in new[] { "FysZeRewardOverlay.exe", "FysZeRewardOverlay.dll", "FysZeRewardOverlay.runtimeconfig.json", "FysZeRewardOverlay.deps.json" })
            archive.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, file), file);
        using var unrelated = new StreamWriter(archive.CreateEntry("../outside.txt").Open());
        unrelated.Write("not an update file");
    }
    var zipBytes = archiveStream.ToArray();
    var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(zipBytes));
    var zipHandler = new FakeHandler { Binary = zipBytes };
    using var zipClient = new HttpClient(zipHandler);
    var package = finalRelease with { Tag = "v" + installed, Package = new ReleaseAsset(new Uri(repository), zipBytes.Length, digest) };
    bool invalidPackage = false;
    try { await updater.StageAsync(package with { Package = package.Package! with { Digest = "sha256:bad" } }, false, zipClient); }
    catch (InvalidDataException) { invalidPackage = true; }
    Check(invalidPackage && updater.GetNextLaunch("1.0", true) is null, "a corrupt download is never staged");
    await updater.StageAsync(package, false, zipClient);
    var launch = updater.GetNextLaunch("1.0", true);
    Check(launch is not null && File.Exists(launch.FileName) && !File.Exists(Path.Combine(Path.GetDirectoryName(launch.FileName)!, "..", "outside.txt")) &&
        updater.GetNextLaunch("1.0", false) is null && updater.GetNextLaunch("1.0", true, package.Tag) is null,
        "automatic staging selects only validated files and respects disabling and declining updates");
    await updater.StageAsync(package, true, zipClient);
    Check(zipHandler.Requests == 2 && updater.GetNextLaunch("1.0", false)?.FileName == launch!.FileName, "manual acceptance reuses an already downloaded package");
    var forwarded = PortableUpdates.ForProcess(updateProfile, launch!.FileName, launch.ArgumentList.ToArray())!;
    forwarded.ConfirmRunning(installed);
    Check(forwarded.GetInstalledRelease(installed)?.Notes == package.Notes &&
        updater.GetNextLaunch("1.0", false)?.FileName == launch.FileName && forwarded.GetNextLaunch(installed, true) is null,
        "the installed version is confirmed, caches its notes, and remains active after disabling automatic updates");

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
    var settings = new Settings { Scale = 1.25, ToggleHotkey = "Ctrl+Shift+F8", Left = -100, IsSvip = true, UseNativeDamage = true };
    settings.Save(temporary);
    var loaded = Settings.Load(temporary);
    Check(loaded.Scale == 1.25 && loaded.ToggleHotkey == settings.ToggleHotkey && loaded.Left == -100, "settings persist including negative monitor coordinates");
    Check(loaded.IsSvip && Settings.Import(Path.Combine(temporary, "settings.json")).IsSvip && !Settings.CreateDefault().IsSvip,
        "SVIP persists in the profile and is off by default");
    string legacyDamageSettings = Path.Combine(temporary, "legacy-damage.json");
    File.WriteAllText(legacyDamageSettings, """{"UseNativeDamage":true}""");
    Check(!loaded.UseNativeDamage && !Settings.Import(legacyDamageSettings).UseNativeDamage &&
        !Settings.CreateDefault().UseNativeDamage, "kill estimates remain active for saved, imported and default settings");
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
    Check(!loaded.IsSvip && !loaded.UseNativeDamage, "existing profiles default to non-SVIP and kill estimates");
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
    Check(loaded.PollIntervalMilliseconds == 200 && loaded.RewardRefreshMinutes == 4320, "out of range stored intervals are clamped");

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
        Check(await Push("""{"map":{"name":"ze_lego 僵尸逃跑①"},"provider":{"steamid":"101"},"player":{"steamid":"101","match_stats":{"kills":20},"state":{"round_totaldmg":23501}}}""") == HttpStatusCode.OK &&
            DisplayInfo.Resolve(gsi.Read(input), data) is { Kills: 20, RoundDamage: 23501 } &&
            gsi.CurrentRoundDamage == 23501, "local combat stats supplement a recognized TXT stage and expose the raw reading");
        Check(gsi.AddCombatStats(BroadcastReader.Read(input)) is { Kills: 20, RoundDamage: 23501 } &&
            gsi.AddCombatStats(BroadcastReader.Parse("地图: ze_other\n关卡: 1")).RoundDamage is null,
            "native damage supplements TXT map mode only on the matching map");
        var noScore = gsi.Read("");
        Check(DisplayInfo.Resolve(noScore, singleStage with { Offline = true }) is { Available: true, Stage: 4, StageInferred: false, Kills: 20 } cachedSingle &&
            cachedSingle.Source.StartsWith("离线缓存"), "one cached stage is available without a CT score and preserves kill progress");
        Check(!DisplayInfo.Resolve(noScore, data).Available, "multiple reward stages still require a TXT stage or CT score");
        Check(await Push("""{"map":{"name":"ze_lego 僵尸逃跑①"},"provider":{"steamid":"101"},"player":{"steamid":"101","match_stats":{"kills":20},"state":{"round_totaldmg":0}}}""") == HttpStatusCode.OK &&
            gsi.Read(input) is { Kills: 20, RoundDamage: 0 }, "a round reset preserves match kills and reports zero damage");
        Check(await Push("""{"map":{"name":"ze_lego 僵尸逃跑①"},"provider":{"steamid":"101"},"player":{"steamid":"101","match_stats":{"kills":20}}}""") == HttpStatusCode.OK &&
            gsi.Read(input) is { Kills: 20, RoundDamage: null }, "a missing damage field stays distinct from zero");
        Check(await Push("""{"map":{"name":"ze_lego 僵尸逃跑①"},"provider":{"steamid":"101"},"player":{"steamid":"102","match_stats":{"kills":60},"state":{"round_totaldmg":50000}}}""") == HttpStatusCode.OK &&
            gsi.Read(input) is { Kills: null, RoundDamage: null } && gsi.CurrentRoundDamage is null,
            "spectating another player clears local kills and native damage");
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
        Check(await Push("""{"map":{"name":"workshop/3121168339/KZ_GROTTO","mode":"custom"}}""") == HttpStatusCode.OK &&
            gsi.CurrentMap == "kz_grotto" && KzMaps.IsKzMap(gsi.CurrentMap), "KZ reads the normalized workshop map from GSI independently of ZE data");
        Check(await Push("""{"provider":{"appid":730}}""") == HttpStatusCode.OK && gsi.Read(input).Map == "", "a payload without a map clears the old map");
        File.WriteAllText(input, "地图: ze_lego\n关卡: 4/4");
        Check(await Push("""{"map":{"name":"ze_lego"},"player":{"state":{"round_totaldmg":30000}}}""") == HttpStatusCode.OK &&
            gsi.Read(input) is { Stage: 4, RoundDamage: 30000 }, "GSI recovers on reconnection");
        clock.Advance(TimeSpan.FromSeconds(15));
        Check(gsi.Read(input) is { Map: "", Stage: null, RoundDamage: null } && gsi.CurrentRoundDamage is null && gsi.CurrentMap == "",
            "expired GSI snapshot clears both the old TXT stage and native damage");
    }

    var kzHandler = new FakeHandler { Body = """
        {"name":"kz_grotto","state":"approved","courses":[
          {"name":"Main","filters":{"classic":{"nub_tier":"easy","pro_tier":"medium"},
                                    "vanilla":{"nub_tier":"medium","pro_tier":"advanced"}}},
          {"name":"Garden","filters":{}}
        ]}
        """ };
    using var kzClient = new HttpClient(kzHandler);
    var kzMaps = new KzMaps(kzClient, clock);
    Check(kzMaps.Lookup("kz_grotto") is null && kzHandler.LastPath == "/maps/kz_grotto", "KZ fetches the official map endpoint once");
    var kzMap = kzMaps.Lookup("kz_grotto")?.Map;
    Check(kzMap is { Courses.Count: 2 } && kzMap.Courses[0] is { Ckz: { Nub: "easy", Pro: "medium" }, Vnl: { Nub: "medium", Pro: "advanced" } } &&
        KzTiers.Format(kzMap.Courses[0].Ckz?.Nub) == "T2 Easy" && kzMap.Courses[1].Ckz is null && kzHandler.Requests == 1,
        "KZ caches both mode filters, preserves bonus courses, and formats official tier numbers");
    var reorderedCourses = new KzMapResult(kzMap! with { Courses = kzMap!.Courses.Reverse().ToArray() });
    Check(KzOverlayInfo.Resolve(reorderedCourses, KzMode.Vnl, KzMode.Vnl) is { Mode: KzMode.Vnl, VnlTier: 3, CkzTier: null } &&
          KzOverlayInfo.Resolve(reorderedCourses, KzMode.Vnl, KzMode.Ckz) is { Mode: KzMode.Vnl, VnlTier: null, CkzTier: 2 } &&
          KzOverlayInfo.Resolve(reorderedCourses, KzMode.Auto, KzMode.Auto) is { VnlTier: 3, CkzTier: 2 },
        "KZ overlay uses the main course NUB tier and keeps actual mode separate from manual tier selection");
    Check(KzOverlayInfo.Resolve(new(null), KzMode.Ckz, KzMode.Ckz) is { Mode: KzMode.Ckz, DifficultyStatus: "非全球图" } &&
          KzOverlayInfo.Resolve(new(null, Failed: true), KzMode.Ckz, KzMode.Ckz).DifficultyStatus == "暂不可用",
        "KZ overlay preserves mode and distinguishes non-global difficulty from API failure");
    kzMaps.Lookup("kz_gro");
    Check(kzMaps.Lookup("kz_gro") is { Failed: false, Map: null }, "KZ ignores a fuzzy API match for another map");
    kzHandler.Status = HttpStatusCode.NotFound;
    kzMaps.Lookup("kz_unlisted");
    Check(kzMaps.Lookup("kz_unlisted") is { Failed: false, Map: null } && !KzMaps.IsKzMap("ze_lego"), "KZ separates unlisted maps from connection errors");
    kzHandler.Status = HttpStatusCode.ServiceUnavailable;
    kzMaps.Lookup("kz_retry");
    int kzRequests = kzHandler.Requests;
    clock.Advance(TimeSpan.FromSeconds(29));
    Check(kzMaps.Lookup("kz_retry") is { Failed: true } && kzHandler.Requests == kzRequests, "KZ throttles failed lookups");
    clock.Advance(TimeSpan.FromSeconds(1));
    Check(kzMaps.Lookup("kz_retry") is null && kzHandler.Requests == kzRequests + 1, "KZ retries an unavailable API");

    var consoleLog = Path.Combine(temporary, "console.log");
    File.WriteAllText(consoleLog, "Possible modes: (Current mode is Vanilla)\n");
    var modeLog = new KzModeLog(consoleLog);
    Check(modeLog.Read("kz_grotto") is null, "KZ starts at the live console tail");
    File.AppendAllText(consoleLog, "[KZ] \u0001你已切换到 \u0003Classic");
    Check(modeLog.Read("kz_grotto") is null, "KZ retains partial log lines");
    File.AppendAllText(consoleLog, " \u0001模式.\r\n");
    Check(modeLog.Read("kz_grotto") == KzMode.Ckz, "KZ detects a Chinese server mode change with chat colors");
    File.AppendAllText(consoleLog, "[ALL] Other player: You have switched to the Vanilla mode.\n");
    Check(modeLog.Read("kz_grotto") == KzMode.Ckz, "KZ ignores ordinary player chat");
    File.AppendAllText(consoleLog, "[Client] CL: Disconnecting from server: NETWORK_DISCONNECT_LOOPDEACTIVATE\n");
    Check(modeLog.Read("kz_grotto") is null, "KZ clears mode on disconnect even before GSI changes");
    File.AppendAllText(consoleLog, "[Client] Map: \"kz_next\"\nPossible modes: (Current mode is Vanilla)\n");
    Check(modeLog.Read("kz_grotto") is null && modeLog.Read("kz_next") == KzMode.Vnl, "KZ scopes the console mode to the map even when the console updates before GSI");
    Check(modeLog.Read("kz_third") is null, "KZ cannot reuse a previous map's mode");
    var kzSettingsPath = Path.Combine(temporary, "kz-settings.json");
    new Settings
    {
        KzEnabled = false, KzMode = KzMode.Vnl, HiddenFields = OverlayField.KzMode,
        KzTextColors = new() { ["T0"] = "#bbccdd", ["VNL"] = "#123456", ["CKZ"] = "invalid", ["T11"] = "#111111" }
    }.Export(kzSettingsPath);
    var kzProfile = Settings.Import(kzSettingsPath);
    Check(kzProfile is { KzEnabled: false, KzMode: KzMode.Vnl } && Settings.CreateDefault() is { KzEnabled: true, KzMode: KzMode.Auto },
        "KZ settings round trip and default to automatic detection");
    kzProfile.Copy().KzTextColors["T0"] = "#ABCDEF";
    Check(kzProfile.Hides(OverlayField.KzMode) && !kzProfile.Hides(OverlayField.KzDifficulty) &&
          kzProfile.KzTextColors is { Count: 2 } && kzProfile.KzTextColors["T0"] == "#BBCCDD" && kzProfile.KzTextColors["VNL"] == "#123456",
        "KZ display options and per-token colors persist independently and reject invalid colors");

    var timedHandler = new FakeHandler { Body = json };
    using var timedRepo = new RewardRepository(Path.Combine(temporary, "timed-cache"), new HttpClient(timedHandler), clock);
    await timedRepo.GetAsync("ze_timer"); clock.Advance(TimeSpan.FromMinutes(2)); await timedRepo.GetAsync("ze_timer");
    Check(timedHandler.Requests == 1, "reward refresh obeys existing interval");
    timedRepo.RefreshInterval = TimeSpan.FromMinutes(1); await timedRepo.GetAsync("ze_timer");
    Check(timedHandler.Requests == 2, "shorter interval takes effect on existing cached data");
    timedRepo.RefreshInterval = TimeSpan.FromMinutes(4320); clock.Advance(TimeSpan.FromMinutes(2)); await timedRepo.GetAsync("ze_timer");
    Check(timedHandler.Requests == 2, "longer interval avoids premature refresh");
    clock.Advance(TimeSpan.FromMinutes(4318)); await timedRepo.GetAsync("ze_timer");
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
    public byte[]? Binary;
    public string? LastPath;
    public bool Fail;
    public int Requests;
    public HttpStatusCode Status = HttpStatusCode.OK;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        LastPath = request.RequestUri?.AbsolutePath;
        if (Fail) throw new HttpRequestException("simulated offline");
        return Task.FromResult(new HttpResponseMessage(Status) { Content = Binary is { } bytes ? new ByteArrayContent(bytes) : new StringContent(Body) });
    }
}

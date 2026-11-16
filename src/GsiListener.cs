using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FysZeRewardOverlay;

public sealed class GsiListener : IDisposable
{
    public const int Port = 31338;
    public const string ConfigFileName = "gamestate_integration_fys_reward_overlay.cfg";
    private const int MaxPayloadBytes = 64 * 1024;
    private static readonly TimeSpan Expiry = TimeSpan.FromSeconds(15);
    private static readonly Regex MapName = new(@"^(?:workshop/[0-9]+/)?(?<map>[a-z0-9_-]{1,124})(?=\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly HttpListener listener = new();
    private readonly TimeProvider clock;
    private readonly TaskCompletionSource firstUpdate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private MapUpdate? latest;
    internal event Action<bool, bool>? CombatStatsChanged;
    internal enum ConnectionState { Waiting, Receiving, TimedOut }
    internal ConnectionState State
    {
        get
        {
            var update = Volatile.Read(ref latest);
            if (update is null) return ConnectionState.Waiting;
            return IsExpired(update) ? ConnectionState.TimedOut : ConnectionState.Receiving;
        }
    }

    internal int? CurrentRoundDamage
    {
        get
        {
            var update = Volatile.Read(ref latest);
            return update is not null && !IsExpired(update) ? update.RoundDamage : null;
        }
    }

    private bool IsExpired(MapUpdate update) => clock.GetElapsedTime(update.ReceivedAt) >= Expiry;

    public static string Config => $$"""
        "FYS ZE Reward Overlay"
        {
            "uri"       "http://127.0.0.1:{{Port}}/"
            "timeout"   "5.0"
            "buffer"    "0.1"
            "throttle"  "0.1"
            "heartbeat" "5.0"
            "data"
            {
                "map" "1"
                "provider" "1"
                "player_id" "1"
                "player_match_stats" "1"
                "player_state" "1"
            }
        }
        """;

    public GsiListener(int port = Port, TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        try
        {
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
        }
        catch
        {
            listener.Close();
            throw;
        }
        _ = Task.Run(ReceiveAsync);
    }

    internal static GsiListener? Start(out string error)
    {
        try
        {
            error = "";
            return new GsiListener();
        }
        catch (HttpListenerException ex)
        {
            error = $"GSI 监听启动失败（127.0.0.1:{Port}）：{ex.Message}";
            return null;
        }
    }

    internal void WaitForUpdate() => firstUpdate.Task.Wait(Expiry);

    public Broadcast Read(string broadcastPath)
    {
        var update = Volatile.Read(ref latest);
        if (update is null) return BroadcastReader.Invalid("等待 CS2 GSI 推送地图信息");
        if (IsExpired(update)) return BroadcastReader.Invalid("CS2 GSI 推送已超时");
        if (update.Map.Length == 0) return BroadcastReader.Invalid("CS2 GSI 当前未提供地图名称");

        var game = new Broadcast(update.Map, "", null, "", "")
            { ServerNumber = update.ServerNumber, Kills = update.Kills, RoundDamage = update.RoundDamage };
        if (!game.IsZe) return game;
        if (broadcastPath.Length > 0)
        {
            var broadcast = BroadcastReader.Read(broadcastPath);
            if (game.Map == broadcast.Map && (update.ServerNumber is null ||
                broadcast.ServerNumber is null || update.ServerNumber == broadcast.ServerNumber))
            {
                if (broadcast.Stage.HasValue)
                    return broadcast with { ServerNumber = update.ServerNumber, Kills = update.Kills, RoundDamage = update.RoundDamage };
                game = game with { Title = broadcast.Title, StageText = broadcast.StageText };
            }
        }
        game = game with { IsGsiStageFallback = true };
        return update.CtScore is int score
            ? game with { Stage = score + 1, CtScore = score }
            : game with { Error = game.StageText.Length > 0
                ? "无法识别关卡：" + game.StageText + "。等待 GSI 提供 CT 比分"
                : "等待" + BroadcastFiles.DisplayName + "提供当前关卡，或等待 GSI 提供 CT 比分" };
    }

    internal Broadcast AddCombatStats(Broadcast broadcast)
    {
        var update = Volatile.Read(ref latest);
        if (update is null || IsExpired(update) || update.Map != broadcast.Map ||
            (update.ServerNumber.HasValue && broadcast.ServerNumber.HasValue && update.ServerNumber != broadcast.ServerNumber))
            return broadcast;
        return broadcast with { Kills = update.Kills, RoundDamage = update.RoundDamage };
    }

    private static string? SteamId(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty("steamid", out var id) &&
        id.ValueKind == JsonValueKind.String ? id.GetString() : null;

    private static (int? Kills, int? RoundDamage) ReadCombatStats(JsonElement root)
    {
        if (!root.TryGetProperty("player", out var player) || player.ValueKind != JsonValueKind.Object) return default;
        if (root.TryGetProperty("provider", out var provider) && SteamId(provider) is string localId &&
            SteamId(player) != localId) return default;
        return (ReadStat(player, "match_stats", "kills"), ReadStat(player, "state", "round_totaldmg"));
    }

    private static int? ReadStat(JsonElement player, string group, string field) =>
        player.TryGetProperty(group, out var stats) && stats.ValueKind == JsonValueKind.Object &&
        stats.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out int count) && count >= 0 ? count : null;

    private async Task ReceiveAsync()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }

            try
            {
                var status = await HandleAsync(context.Request);
                context.Response.StatusCode = (int)status;
                context.Response.ContentLength64 = 0;
                context.Response.Close();
                if (status == HttpStatusCode.OK) firstUpdate.TrySetResult();
            }
            catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException or TimeoutException)
            {
                context.Response.Abort();
            }
        }
    }

    private async Task<HttpStatusCode> HandleAsync(HttpListenerRequest request)
    {
        if (request.RemoteEndPoint is not { Address: var address } || !IPAddress.IsLoopback(address)) return HttpStatusCode.Forbidden;
        if (request.Url?.AbsolutePath != "/") return HttpStatusCode.NotFound;
        if (request.HttpMethod != "POST") return HttpStatusCode.MethodNotAllowed;
        if (request.ContentLength64 <= 0) return HttpStatusCode.BadRequest;
        if (request.ContentLength64 > MaxPayloadBytes) return HttpStatusCode.RequestEntityTooLarge;

        var body = new byte[(int)request.ContentLength64];
        await request.InputStream.ReadExactlyAsync(body).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return HttpStatusCode.BadRequest;
            string name = "", raw = "";
            int? ctScore = null;
            if (root.TryGetProperty("map", out var map) && map.ValueKind == JsonValueKind.Object &&
                map.TryGetProperty("name", out var rawName) && rawName.ValueKind == JsonValueKind.String)
            {
                raw = rawName.GetString()!.Trim();
                name = MapName.Match(raw).Groups["map"].Value.ToLowerInvariant();
                if (map.TryGetProperty("team_ct", out var team) && team.ValueKind == JsonValueKind.Object &&
                    team.TryGetProperty("score", out var score) && score.ValueKind == JsonValueKind.Number &&
                    score.TryGetInt32(out int points) && points is >= 0 and < int.MaxValue)
                    ctScore = points;
            }
            var (kills, roundDamage) = ReadCombatStats(root);
            var update = new MapUpdate(name, FysServer.ParseNumber(raw), ctScore, kills, roundDamage, clock.GetTimestamp());
            var previous = Interlocked.Exchange(ref latest, update);
            bool scopeChanged = previous is null || previous.Map != update.Map || previous.ServerNumber != update.ServerNumber ||
                IsExpired(previous);
            bool killsChanged = previous?.Kills != kills || (kills.HasValue && scopeChanged);
            bool damageChanged = previous?.RoundDamage != roundDamage || (roundDamage.HasValue && scopeChanged);
            if (killsChanged || damageChanged) CombatStatsChanged?.Invoke(killsChanged, damageChanged);
            return HttpStatusCode.OK;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return HttpStatusCode.BadRequest; }
    }

    public void Dispose() => listener.Close();

    private sealed record MapUpdate(string Map, int? ServerNumber, int? CtScore, int? Kills, int? RoundDamage, long ReceivedAt);
}

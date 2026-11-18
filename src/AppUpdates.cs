using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace FysZeRewardOverlay;

internal sealed record UpdateState(DateTimeOffset? LastCheckUtc = null, string? LastShownVersion = null, string? DeclinedTag = null)
{
    internal bool IsDue(int intervalDays, DateTimeOffset now) => intervalDays > 0 &&
        (LastCheckUtc is not { } last || last > now || now - last >= TimeSpan.FromDays(intervalDays));
}

internal sealed class AppUpdates : IDisposable
{
    private readonly PortableUpdates? packages;
    private readonly string statePath;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private UpdateState state;
    private OverlayWindow? owner;
    private bool busy;
    private bool disposed;
    private DateTimeOffset retryAfter;

    internal bool RestartRequested { get; private set; }

    internal AppUpdates(string profile, PortableUpdates? packages)
    {
        this.packages = packages;
        statePath = Path.Combine(profile, "update-state.json");
        try { state = JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(statePath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { state = new(); }
        timer.Tick += async (_, _) => await CheckIfDueAsync();
    }

    internal bool TryLaunch(int days) => packages?.TryLaunchNewer(ReleaseCheck.LocalVersion, days > 0, state.DeclinedTag) == true;

    internal async Task StartAsync(OverlayWindow window)
    {
        owner = window;
        busy = true;
        try
        {
            try { packages?.ConfirmRunning(ReleaseCheck.LocalVersion); }
            catch (Exception ex) when (IsUpdateError(ex)) { }
            await ShowInstalledNotesAsync();
        }
        finally { busy = false; }
        if (disposed) return;
        timer.Start();
        await CheckIfDueAsync();
    }

    private async Task ShowInstalledNotesAsync()
    {
        string current = ReleaseCheck.LocalVersion;
        if (state.LastShownVersion is { } shown && ReleaseCheck.TryCompare(current, shown, out int comparison) && comparison <= 0) return;
        ReleaseCheck? release = null;
        try { release = await ReleaseCheck.GetInstalledAsync(ReleaseCheck.Repository, current); }
        catch (Exception ex) when (IsUpdateError(ex)) { }
        if (release is null && packages?.GetInstalledRelease(current) is { } cached)
            release = new(cached.Tag, ReleaseCheck.Repository + "/releases/tag/" + Uri.EscapeDataString(cached.Tag), 0, cached.Notes ?? "", null);
        if (string.IsNullOrWhiteSpace(release?.Notes))
            release = ReleaseCheck.GetBundledRelease(current) ?? release;
        if (release is null || disposed) return;
        new ReleaseDialog(release, installed: true, canUpdate: false, owner?.UpdateDialogOwner).ShowDialog();
        Store(state with { LastShownVersion = current });
    }

    private async Task CheckIfDueAsync()
    {
        var now = DateTimeOffset.UtcNow;
        if (busy || disposed || packages is null || owner is null || now < retryAfter || !state.IsDue(owner.AutoUpdateDays, now)) return;
        await CheckAsync(manual: false, (_, _) => { });
    }

    internal Task CheckManuallyAsync(Action<string, bool> status) => CheckAsync(manual: true, status);

    private async Task CheckAsync(bool manual, Action<string, bool> status)
    {
        if (disposed) return;
        if (busy) { status("更新任务正在进行，请稍后再试。", false); return; }
        busy = true;
        status("正在检查更新…", false);
        try
        {
            var release = await ReleaseCheck.GetLatestAsync(ReleaseCheck.Repository, ReleaseCheck.LocalVersion);
            if (disposed) return;
            var checkedAt = DateTimeOffset.UtcNow;
            if (release.Comparison <= 0)
            {
                Store(state with { LastCheckUtc = checkedAt });
                status(release.Comparison == 0 ? $"已是最新版本 {release.Tag}。" : $"当前版本高于已发布版本 {release.Tag}。", false);
                return;
            }

            bool canUpdate = packages is not null && release.Package is not null;
            if (manual)
            {
                Store(state with { LastCheckUtc = checkedAt });
                status($"发现新版本 {release.Tag}。", false);
                var dialog = new ReleaseDialog(release, installed: false, canUpdate, owner?.UpdateDialogOwner);
                if (dialog.ShowDialog() != true)
                {
                    if (canUpdate) Store(state with { DeclinedTag = release.Tag });
                    return;
                }
            }
            else if (!canUpdate || owner?.AutoUpdateDays is not > 0 ||
                (state.DeclinedTag is { } tag && ReleaseCheck.TryCompare(release.Tag, tag, out int comparison) && comparison == 0))
            {
                Store(state with { LastCheckUtc = checkedAt });
                return;
            }

            status($"正在下载 {release.Tag}…", false);
            await packages!.StageAsync(release, manual);
            Store(state with { LastCheckUtc = checkedAt, DeclinedTag = null });
            if (!manual || disposed || owner is null) return;
            status("更新已下载，正在重启…", false);
            RestartRequested = true;
            owner.Quit();
        }
        catch (Exception ex) when (IsUpdateError(ex))
        {
            retryAfter = DateTimeOffset.UtcNow.AddHours(1);
            status(ex is TaskCanceledException ? "更新请求超时，请重试。" : "更新失败：" + ex.Message, true);
        }
        finally { busy = false; }
    }

    private void Store(UpdateState value)
    {
        state = value;
        try { AtomicFile.Write(statePath, JsonSerializer.Serialize(state)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool IsUpdateError(Exception ex) => ex is HttpRequestException or IOException or InvalidDataException or
        UnauthorizedAccessException or JsonException or TaskCanceledException;

    public void Dispose()
    {
        disposed = true;
        timer.Stop();
    }
}

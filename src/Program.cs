using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;

namespace FysZeRewardOverlay;

internal static class Program
{
    internal static Uri AppIconUri { get; } = new("Assets/App.ico", UriKind.Relative);

    [STAThread]
    public static int Main(string[] args)
    {
        string? Option(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        var directory = Option("--data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FysZeRewardOverlay");
        Directory.CreateDirectory(directory);
        var settings = Settings.Load(directory);
        if (Option("--source") is string source)
        {
            settings.MapSource = MapSource.Broadcast;
            settings.BroadcastPath = source;
        }
        if (args.Contains("--diagnose"))
        {
            Broadcast broadcast;
            if (settings.MapSource == MapSource.Gsi)
            {
                using var gsi = GsiListener.Start(out var error);
                gsi?.WaitForUpdate();
                broadcast = gsi?.Read(settings.BroadcastPath) ?? BroadcastReader.Invalid(error);
            }
            else broadcast = BroadcastReader.Read(settings.BroadcastPath);
            using var rewards = new RewardRepository(Path.Combine(directory, "cache"));
            rewards.RefreshInterval = TimeSpan.FromMinutes(settings.RewardRefreshMinutes);
            var result = DisplayInfo.Resolve(broadcast, broadcast.CanFetchRewards ? rewards.GetAsync(broadcast.Map).GetAwaiter().GetResult() : null);
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            return result.Available ? 0 : 2;
        }
        PortableUpdates? packages = null;
        try
        {
            bool interactive = !args.Contains("--demo") && !args.Contains("--smoke-report") && !args.Contains("--render-preview");
            packages = interactive ? PortableUpdates.ForCurrentProcess(directory, args) : null;
            using var updates = interactive ? new AppUpdates(directory, packages) : null;
            if (updates?.TryLaunch(settings.AutoUpdateDays) == true) return 0;
            using var mutex = new Mutex(true, @"Local\FysZeRewardOverlay-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(directory.ToLowerInvariant())))[..16], out bool first);
            if (!first)
            {
                MessageBox.Show("工具已在运行。\n请使用系统托盘图标，或按 " + settings.SettingsHotkey + " 打开设置。", "ZE 奖励悬浮窗");
                return 0;
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (_, e) =>
            {
                packages?.RecoverFailedStartup();
                try { File.AppendAllText(Path.Combine(directory, "error.log"), DateTimeOffset.Now + " " + e.Exception + Environment.NewLine); } catch { }
                MessageBox.Show("工具遇到错误，详细信息已保存到应用目录的 error.log。\n" + e.Exception.Message, "ZE 奖励悬浮窗");
                e.Handled = true;
                app.Shutdown(1);
            };
            var overlay = new OverlayWindow(settings, directory, args.Contains("--demo"), !args.Contains("--smoke-report") && !args.Contains("--render-preview"))
            { Updates = updates };
            app.MainWindow = overlay;
            if (Option("--render-preview") is string preview)
            {
                overlay.RenderPreviews(preview);
                return 0;
            }
            overlay.Show();
            if (Option("--smoke-report") is string report) overlay.Dispatcher.BeginInvoke(async () => await overlay.SmokeTestAsync(report));
            if (args.Contains("--settings")) overlay.Dispatcher.BeginInvoke(overlay.OpenSettings);
            if (updates is not null) overlay.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,
                async () => await updates.StartAsync(overlay));
            int exitCode = app.Run();
            if (updates?.RestartRequested != true) return exitCode;
            mutex.ReleaseMutex();
            mutex.Dispose();
            if (updates.TryLaunch(overlay.AutoUpdateDays)) return exitCode;
            MessageBox.Show("新版已下载，请重新打开工具。", "无法重启悬浮窗");
            return 1;
        }
        catch (Exception ex)
        {
            packages?.RecoverFailedStartup();
            File.AppendAllText(Path.Combine(directory, "error.log"), ex + Environment.NewLine);
            MessageBox.Show(ex.Message, "ZE 奖励悬浮窗启动失败");
            return 1;
        }
    }
}

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
        using var mutex = new Mutex(true, @"Local\FysZeRewardOverlay-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(directory.ToLowerInvariant())))[..16], out bool first);
        if (!first)
        {
            MessageBox.Show("工具已在运行。\n请使用系统托盘图标，或按 " + settings.SettingsHotkey + " 打开设置。", "ZE 奖励悬浮窗");
            return 0;
        }
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (_, e) =>
            {
                try { File.AppendAllText(Path.Combine(directory, "error.log"), DateTimeOffset.Now + " " + e.Exception + Environment.NewLine); } catch { }
                MessageBox.Show("工具遇到错误，详细信息已保存到应用目录的 error.log。\n" + e.Exception.Message, "ZE 奖励悬浮窗");
                e.Handled = true;
                app.Shutdown(1);
            };
            var overlay = new OverlayWindow(settings, directory, args.Contains("--demo"), !args.Contains("--smoke-report") && !args.Contains("--render-preview"));
            app.MainWindow = overlay;
            if (Option("--render-preview") is string preview)
            {
                overlay.RenderPreviews(preview);
                return 0;
            }
            overlay.Show();
            if (Option("--smoke-report") is string report) overlay.Dispatcher.BeginInvoke(async () => await overlay.SmokeTestAsync(report));
            if (args.Contains("--settings")) overlay.Dispatcher.BeginInvoke(overlay.OpenSettings);
            return app.Run();
        }
        catch (Exception ex)
        {
            File.AppendAllText(Path.Combine(directory, "error.log"), ex + Environment.NewLine);
            MessageBox.Show(ex.Message, "ZE 奖励悬浮窗启动失败");
            return 1;
        }
    }
}

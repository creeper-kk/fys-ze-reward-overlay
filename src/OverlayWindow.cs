using System;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace FysZeRewardOverlay;

public sealed class OverlayWindow : Window
{
    private Settings settings;
    private readonly string directory;
    private readonly bool demo;
    private readonly bool interactiveSetup;
    private readonly RewardRepository rewards;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly DispatcherTimer gsiStatusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private HotkeyManager? hotkeys;
    private Forms.NotifyIcon? tray;
    private TrayMenu? trayMenu;
    private System.Drawing.Icon? trayIcon;
    private SettingsWindow? settingsWindow;
    private GsiListener? gsi;
    private string gsiError = "";
    private ObsBrowserSource? obs;
    private RewardCard? obsCard;
    private string obsError = "";
    private (string Map, int? Server)? stageOffsetScope;
    private int stageOffset;
    private readonly RewardCard card;
    private bool loading;
    private bool quitting;
    private string identity = "";
    private int generation;
    private DisplayInfo? lastDisplay;
    private bool inputUnavailable;
    private string notifiedMissingPath = "";
    private (Point Cursor, Point Window)? dragStart;

    public OverlayWindow(Settings settings, string directory, bool demo, bool interactiveSetup = true)
    {
        this.settings = settings;
        this.directory = directory;
        this.demo = demo;
        this.interactiveSetup = interactiveSetup;
        rewards = new RewardRepository(Path.Combine(directory, "cache"));
        ApplyUpdateIntervals();
        Title = demo ? "ZE 奖励悬浮窗 · 演示" : "ZE 奖励悬浮窗";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        UseLayoutRounding = true;
        card = new RewardCard(OpenSettings, ToggleLock, SelectBroadcastFile);
        Content = card.Element;
        SourceInitialized += (_, _) =>
        {
            hotkeys = new HotkeyManager(new WindowInteropHelper(this).Handle, HandleHotkey);
            CreateTray();
            if (!hotkeys.Apply(settings.Hotkeys, settings.ActiveHotkeyCount, out var error))
            {
                if (settings.MapSource == MapSource.Gsi) hotkeys.Apply(settings.Hotkeys[..3], out _);
                Dispatcher.BeginInvoke(() => { OpenSettings(); settingsWindow?.SetError(error); });
            }
            ApplyAppearance();
        };
        Loaded += (_, _) =>
        {
            ApplyMapSource();
            ApplyObsSettings();
            Tick();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, PlaceWindow);
            timer.Start();
            if (!demo && interactiveSetup && inputUnavailable) OpenSettings();
        };
        timer.Tick += (_, _) => Tick();
        gsiStatusTimer.Tick += (_, _) => UpdateGsiStatus();
        Closing += (_, e) => { if (!quitting) { e.Cancel = true; Hide(); } };
        Closed += (_, _) =>
        {
            timer.Stop();
            gsiStatusTimer.Stop();
            hotkeys?.Dispose();
            tray?.Dispose();
            trayMenu?.Dispose();
            trayIcon?.Dispose();
            gsi?.Dispose();
            obs?.Dispose();
            rewards.Dispose();
        };
        MouseLeftButtonDown += StartDrag;
        MouseMove += MoveDrag;
        MouseLeftButtonUp += (_, _) => { if (dragStart is not null) ReleaseMouseCapture(); };
        LostMouseCapture += OnLostDragCapture;
        MouseRightButtonUp += (_, _) => { if (!settings.Locked) OpenSettings(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape && !settings.Locked) ToggleLock(); };
    }

    private void StartDrag(object sender, MouseButtonEventArgs e)
    {
        if (settings.Locked || e.Handled || !CaptureMouse()) return;
        dragStart = (PointToScreen(e.GetPosition(this)), PointToScreen(new Point()));
        e.Handled = true;
    }

    private void MoveDrag(object sender, MouseEventArgs e)
    {
        if (dragStart is not { } start) return;
        if (e.LeftButton != MouseButtonState.Pressed) { ReleaseMouseCapture(); return; }
        var position = start.Window + (PointToScreen(e.GetPosition(this)) - start.Cursor);
        Native.MoveWindow(new WindowInteropHelper(this).Handle, (int)Math.Round(position.X), (int)Math.Round(position.Y));
    }

    private void OnLostDragCapture(object sender, MouseEventArgs e)
    {
        if (e.OriginalSource != this || dragStart is not { } start) return;
        dragStart = null;
        if (PointToScreen(new Point()) == start.Window) return;
        settings.Left = Left;
        settings.Top = Top;
        SaveSettings();
    }

    internal static TextBlock Text(string value, double size, string color, bool bold = false) => new()
    {
        Text = value, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
    };
    internal static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    private void Tick()
    {
        if (quitting) return;
        if (demo)
        {
            Render(RewardCard.Sample);
            return;
        }
        var broadcast = ReadMap();
        settingsWindow?.UpdateInferredStage(broadcast, stageOffset);
        inputUnavailable = broadcast.FileUnavailable;
        if (inputUnavailable && interactiveSetup && notifiedMissingPath != settings.BroadcastPath)
        {
            notifiedMissingPath = settings.BroadcastPath;
            tray?.ShowBalloonTip(5000, BroadcastFiles.SelectLabel, "当前无法读取" + BroadcastFiles.DisplayName +
                "。右键托盘选择文本，或按 " + settings.SettingsHotkey + " 打开设置。", Forms.ToolTipIcon.Info);
        }
        else if (!inputUnavailable) notifiedMissingPath = "";
        if (identity != broadcast.Identity)
        {
            identity = broadcast.Identity;
            generation++;
            Render(DisplayInfo.Resolve(broadcast, null));
        }
        else if (lastDisplay is { Available: true } displayed && displayed.Kills != broadcast.Kills)
        {
            Render(displayed with { Kills = broadcast.Kills });
        }
        if (!broadcast.IsZe || broadcast.Stage is null || broadcast.Error.Length > 0 || loading) return;
        FetchAndRender(broadcast, generation);
    }

    private async void FetchAndRender(Broadcast broadcast, int version)
    {
        loading = true;
        bool retry = false;
        try
        {
            var data = await rewards.GetAsync(broadcast.Map);
            // Re-read after every await: never put the previous map's result on a new map.
            if (!quitting)
            {
                var latest = ReadMap();
                if (version == generation && latest.Identity == broadcast.Identity) Render(DisplayInfo.Resolve(latest, data));
                else retry = true;
            }
        }
        finally { loading = false; }
        if (retry) Tick();
    }

    private void ApplyMapSource()
    {
        if (!demo && gsi is null)
        {
            gsi = GsiListener.Start(out gsiError);
            if (gsi is not null) gsi.KillCountChanged += OnKillCountChanged;
        }
        if (settings.MapSource == MapSource.Broadcast)
        {
            stageOffsetScope = null;
            stageOffset = 0;
        }
        UpdateGsiStatus();
    }

    private void UpdateGsiStatus() => settingsWindow?.UpdateGsiStatus(gsi?.State, gsiError, demo);

    private void OnKillCountChanged() => Dispatcher.BeginInvoke(DispatcherPriority.Background, Tick);

    private Broadcast ReadMap()
    {
        if (settings.MapSource != MapSource.Gsi)
        {
            var text = BroadcastReader.Read(settings.BroadcastPath);
            return gsi?.AddKills(text) ?? text;
        }
        var broadcast = gsi?.Read(settings.BroadcastPath) ?? BroadcastReader.Invalid(gsiError);
        var scope = (broadcast.Map, broadcast.ServerNumber);
        if (stageOffsetScope != scope)
        {
            stageOffsetScope = scope;
            stageOffset = 0;
        }
        return broadcast.CtScore is int score
            ? broadcast with { Stage = (int)Math.Clamp((long)score + 1 + stageOffset, 1, int.MaxValue) }
            : broadcast;
    }

    private void AdjustStageOffset(int step)
    {
        if (demo || settings.MapSource != MapSource.Gsi) return;
        var current = ReadMap();
        if (current.CtScore is not int score || current.Stage is not int stage) return;
        int adjusted = (int)Math.Clamp((long)stage + step, 1, int.MaxValue);
        if (adjusted == stage) return;
        stageOffset = adjusted - (score + 1);
        Tick();
    }

    private void Render(DisplayInfo info)
    {
        if (lastDisplay == info) return;
        lastDisplay = info;
        card.Render(info, inputUnavailable, demo);
        PublishObsFrame(info);
    }

    private void ApplyObsSettings()
    {
        obsError = "";
        if (settings.ObsEnabled)
        {
            try { obs ??= new ObsBrowserSource(); }
            catch (HttpListenerException ex) { obsError = ex.Message; }
            if (obs is not null)
            {
                obsCard ??= new RewardCard(() => { }, () => { }, () => { });
                var appearance = settings.Copy();
                // The broadcast card always uses its normal display appearance.
                appearance.Locked = true;
                obsCard.ApplyAppearance(appearance);
            }
        }
        else
        {
            obs?.Dispose();
            obs = null;
            obsCard = null;
        }
        UpdateObsStatus();
    }

    private void UpdateObsStatus() => settingsWindow?.UpdateObsStatus(obs is not null, obsError);

    private void PublishObsFrame(DisplayInfo info)
    {
        if (obs is null || obsCard is null) return;
        obsCard.Render(info, inputUnavailable, demo);
        using var png = new MemoryStream();
        RenderVisual(obsCard.Element, png, new Size(double.PositiveInfinity, double.PositiveInfinity));
        obs.Publish(png.ToArray());
    }

    private void ApplyUpdateIntervals()
    {
        timer.Interval = TimeSpan.FromMilliseconds(settings.PollIntervalMilliseconds);
        rewards.RefreshInterval = TimeSpan.FromMinutes(settings.RewardRefreshMinutes);
    }

    public void SelectBroadcastFile()
    {
        var selected = BroadcastFiles.Select(this, settings.BroadcastPath);
        if (selected is null) return;
        var next = settings.Copy(); next.BroadcastPath = selected;
        var error = ApplySettings(next);
        if (error is not null) MessageBox.Show(error, "无法切换" + BroadcastFiles.DisplayName);
    }

    private void CreateTray()
    {
        using var iconResource = Application.GetResourceStream(Program.AppIconUri).Stream;
        trayIcon = new System.Drawing.Icon(iconResource, Forms.SystemInformation.SmallIconSize);
        tray = new Forms.NotifyIcon { Icon = trayIcon, Visible = true, Text = "ZE 奖励悬浮窗" };
        trayMenu = new TrayMenu();
        var visibility = trayMenu.AddAction("隐藏悬浮窗", ToggleVisibility);
        var lockState = trayMenu.AddAction("拖动悬浮窗", ToggleLock);
        trayMenu.AddAction("恢复悬浮窗位置", ResetPosition);
        trayMenu.AddDivider();
        trayMenu.AddAction("设置…", OpenSettings);
        trayMenu.AddAction(BroadcastFiles.SelectLabel, SelectBroadcastFile);
        trayMenu.AddAction("立即更新奖励", RefreshRewards);
        trayMenu.AddDivider();
        trayMenu.AddAction("退出", Quit);
        trayMenu.Opening += (_, _) =>
        {
            visibility.Text = IsVisible ? "隐藏悬浮窗" : "显示悬浮窗";
            lockState.Text = settings.Locked ? "拖动悬浮窗" : "锁定悬浮窗";
        };
        tray.ContextMenuStrip = trayMenu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
    }

    private void HandleHotkey(int id)
    {
        if (id is >= 1 and <= 5 && settingsWindow?.CaptureRegisteredShortcut(settings.Hotkeys[id - 1]) == true) return;
        if (id == 1) ToggleVisibility();
        else if (id == 2) ToggleLock();
        else if (id == 3) OpenSettings();
        else if (id == 4) AdjustStageOffset(1);
        else if (id == 5) AdjustStageOffset(-1);
    }
    public void ToggleVisibility() { if (IsVisible) Hide(); else Show(); }
    public void ToggleLock()
    {
        if (dragStart is not null) ReleaseMouseCapture();
        settings.Locked = !settings.Locked;
        Show();
        ApplyAppearance();
        SaveSettings();
    }
    private void ApplyAppearance()
    {
        card.ApplyAppearance(settings);
        Cursor = settings.Locked ? null : Cursors.SizeAll;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) Native.SetClickThrough(handle, settings.Locked);
    }

    private void PlaceWindow()
    {
        var workArea = SystemParameters.WorkArea;
        double defaultLeft = Settings.DefaultLeft + ActualWidth <= workArea.Right
            ? Settings.DefaultLeft : workArea.Right - ActualWidth - 24;
        Left = Math.Clamp(settings.Left ?? defaultLeft, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - ActualWidth));
        Top = Math.Clamp(settings.Top ?? Settings.DefaultTop, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - ActualHeight));
    }
    public void ResetPosition()
    {
        settings.Left = null; settings.Top = null;
        Show(); UpdateLayout(); PlaceWindow(); SaveSettings();
    }
    public void OpenSettings()
    {
        if (settingsWindow is not null)
        {
            if (settingsWindow.WindowState == WindowState.Minimized) SystemCommands.RestoreWindow(settingsWindow);
            settingsWindow.Activate();
            return;
        }
        settingsWindow = new SettingsWindow(() => settings, ApplySettings, RefreshRewards, ResetPosition, ToggleLock, AdjustStageOffset);
        settingsWindow.Closed += (_, _) =>
        {
            gsiStatusTimer.Stop();
            settingsWindow = null;
        };
        UpdateGsiStatus();
        UpdateObsStatus();
        gsiStatusTimer.Start();
        settingsWindow.Show();
        settingsWindow.Activate();
        if (!demo && settings.MapSource == MapSource.Gsi)
        {
            var broadcast = ReadMap();
            settingsWindow.UpdateInferredStage(broadcast, stageOffset);
        }
    }
    private string? ApplySettings(Settings next, bool keepPositionAndLock = true)
    {
        if (hotkeys is null) return "快捷键尚未初始化";
        if (!hotkeys.Apply(next.Hotkeys, next.ActiveHotkeyCount, out var error)) return error;
        if (keepPositionAndLock)
        {
            next.Left = settings.Left;
            next.Top = settings.Top;
            next.Locked = settings.Locked;
        }
        try { next.Save(directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            hotkeys.Apply(settings.Hotkeys, settings.ActiveHotkeyCount, out _);
            return "设置保存失败：" + ex.Message;
        }
        if (dragStart is not null)
        {
            dragStart = null;
            ReleaseMouseCapture();
        }
        if (settings.MapSource != next.MapSource) lastDisplay = null;
        settings = next;
        identity = ""; generation++;
        ApplyMapSource();
        ApplyUpdateIntervals();
        ApplyObsSettings();
        ApplyAppearance(); UpdateLayout(); PlaceWindow();
        var previousDisplay = lastDisplay;
        Tick();
        if (lastDisplay is { } info && ReferenceEquals(info, previousDisplay)) PublishObsFrame(info);
        return null;
    }
    private void SaveSettings()
    {
        try { settings.Save(directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { tray?.ShowBalloonTip(3000, "ZE 奖励悬浮窗", "设置保存失败：" + ex.Message, Forms.ToolTipIcon.Warning); }
    }
    public void RefreshRewards() { rewards.Invalidate(); Tick(); }
    public void Quit()
    {
        quitting = true; timer.Stop(); SaveSettings(); settingsWindow?.Close(); Close(); Application.Current.Shutdown();
    }

    // Render the application's own visual tree for layout QA; no desktop or other application is captured.
    internal static void SaveVisual(FrameworkElement element, string path, Size size)
    {
        using var file = File.Create(path);
        RenderVisual(element, file, size);
    }

    private static void RenderVisual(FrameworkElement element, Stream output, Size size)
    {
        var margin = element.Margin;
        element.Margin = new Thickness(0);
        element.Measure(size);
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(output);
        element.Margin = margin;
    }
    internal void RenderPreviews(string output)
    {
        Directory.CreateDirectory(output);
        ApplyAppearance();
        Render(RewardCard.Sample);
        SaveVisual(card.Element, Path.Combine(output, "reward-preview.png"), new Size(double.PositiveInfinity, double.PositiveInfinity));
        Render(DisplayInfo.Resolve(BroadcastReader.Parse("地图: fys_lobby_liyue"), null));
        SaveVisual(card.Element, Path.Combine(output, "unavailable-preview.png"), new Size(double.PositiveInfinity, double.PositiveInfinity));
        var settingsPreview = new SettingsWindow(() => settings, (_, _) => null, () => { }, () => { }, () => { }, _ => { });
        var layout = (FrameworkElement)settingsPreview.Content;
        var settingsSize = new Size(1180, 750);
        layout.Width = settingsSize.Width;
        layout.Height = settingsSize.Height;
        for (int i = 0; i < settingsPreview.PageCount; i++)
        {
            settingsPreview.SelectPreviewTab(i);
            SaveVisual(layout, Path.Combine(output, "settings-tab-" + i + ".png"), settingsSize);
        }
        rewards.Dispose();
    }

    internal async System.Threading.Tasks.Task SmokeTestAsync(string output)
    {
        await System.Threading.Tasks.Task.Delay(1200);
        var handle = new WindowInteropHelper(this).Handle;
        bool initialLock = settings.Locked;
        if (!initialLock) ToggleLock();
        var lockedStyles = Native.GetWindowLong(handle, -20);
        bool locked = (lockedStyles & 0x20) != 0 && (lockedStyles & 0x08000000) != 0;
        ToggleLock();
        bool unlocked = (Native.GetWindowLong(handle, -20) & 0x20) == 0;
        bool missingFileAction = !inputUnavailable || card.FileActionVisible;
        ToggleVisibility(); bool hidden = !IsVisible;
        ToggleVisibility(); bool shown = IsVisible;
        var startLeft = Left; var startTop = Top;
        Left += 25; Top += 25;
        settings.Left = Left; settings.Top = Top; SaveSettings();
        var persisted = Settings.Load(directory);
        bool moved = Math.Abs(persisted.Left!.Value - Left) < 1 && Math.Abs(persisted.Top!.Value - Top) < 1;
        Left = startLeft; Top = startTop; settings.Left = Left; settings.Top = Top;
        if (initialLock) ToggleLock();
        var originalSettings = settings.Copy();
        var custom = settings.Copy();
        custom.PollIntervalMilliseconds = 250; custom.RewardRefreshMinutes = 3;
        custom.OverlayFontFamily = "Consolas";
        custom.GlobalTextStyle = new(20, "#ABCDEF", 500);
        custom.FieldTextStyles.Clear();
        custom.FieldTextStyles[OverlayField.RankPasses] = new(Size: 31, Weight: 800);
        custom.FieldTextStyles[OverlayField.Source] = new(Color: "#123456");
        custom.HiddenFields = OverlayField.MapName | OverlayField.RankDamage | OverlayField.Source;
        var applyError = ApplySettings(custom);
        bool intervalsApplied = timer.Interval == TimeSpan.FromMilliseconds(250) && rewards.RefreshInterval == TimeSpan.FromMinutes(3);
        bool fontApplied = card.UsesFont("Consolas");
        Render(new(true, "ze_test", "测试", 1, new(1, 2, 3, 4), "离线缓存", ""));
        bool typographyApplied = card.ShowsCustomTypography();
        bool fieldsHidden = card.ShowsExpectedFields();
        var hiddenSettings = settings.Copy(); hiddenSettings.HiddenFields = OverlayField.All; ApplySettings(hiddenSettings);
        Render(DisplayInfo.Resolve(BroadcastReader.Invalid("测试不可用状态"), null));
        bool unavailableRetained = card.ShowsUnavailableMessage();
        lastDisplay = null;
        ApplySettings(originalSettings);
        var report = new { HotkeysRegistered = hotkeys?.RegisteredCount, LockedClickThrough = locked, UnlockedMouse = unlocked, HideWorks = hidden, ShowWorks = shown, PositionPersists = moved, MissingFileActionAvailable = missingFileAction, CustomSettingsAccepted = applyError is null, UpdateIntervalsApplied = intervalsApplied, CustomFontAppliedToTextAndNumbers = fontApplied, TypographyOverridesApplied = typographyApplied, InformationHiddenIndependently = fieldsHidden, UnavailableMessageRetained = unavailableRetained, Display = lastDisplay };
        File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        SaveVisual(card.Element, Path.ChangeExtension(output, ".png"), new Size(double.PositiveInfinity, double.PositiveInfinity));
        Quit();
    }
}

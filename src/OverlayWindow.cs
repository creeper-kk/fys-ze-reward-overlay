using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
    private HotkeyManager? hotkeys;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private SettingsWindow? settingsWindow;
    private readonly Border card = new();
    private readonly StackPanel content = new();
    private readonly TextBlock heading = Text("ZE  /  通关奖励", 12, "#A4ED86", true);
    private readonly TextBlock title = Text("等待地图信息", 19, "#F2F5F8", true);
    private readonly TextBlock map = Text("", 12, "#A5AFBC");
    private readonly TextBlock stage = Text("", 12, "#A4ED86", true);
    private readonly TextBlock unavailable = Text(DisplayInfo.UnavailableText, 17, "#F2F5F8", true);
    private readonly TextBlock reason = Text("", 11, "#A5AFBC");
    private readonly TextBlock source = Text("", 10, "#ADB8C8");
    private readonly TextBlock hint = Text("", 10, "#98A6B7");
    private readonly TextBlock[] values = new TextBlock[4];
    private readonly FrameworkElement[] fieldRows = new FrameworkElement[4];
    private readonly DockPanel top = new() { Margin = new Thickness(0, 0, 0, 12) };
    private readonly StackPanel footer = new() { Margin = new Thickness(0, 7, 0, 0) };
    private Button? selectFileButton;
    private readonly StackPanel rewardRows = new();
    private readonly StackPanel unavailablePanel = new();
    private readonly StackPanel editButtons = new() { Orientation = Orientation.Horizontal };
    private bool loading;
    private bool quitting;
    private string identity = "";
    private int generation;
    private DisplayInfo? lastDisplay;
    private bool inputUnavailable;
    private string notifiedMissingPath = "";

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
        BuildContent();
        SourceInitialized += (_, _) =>
        {
            hotkeys = new HotkeyManager(new WindowInteropHelper(this).Handle, HandleHotkey);
            CreateTray();
            if (!hotkeys.Apply(settings.Hotkeys, out var error))
                Dispatcher.BeginInvoke(() => { OpenSettings(); settingsWindow?.SetError(error); });
            ApplyAppearance();
        };
        Loaded += (_, _) =>
        {
            PlaceWindow();
            Tick();
            timer.Start();
            if (!demo && interactiveSetup && inputUnavailable) OpenSettings();
        };
        timer.Tick += (_, _) => Tick();
        Closing += (_, e) => { if (!quitting) { e.Cancel = true; Hide(); } };
        Closed += (_, _) =>
        {
            timer.Stop();
            hotkeys?.Dispose();
            tray?.Dispose();
            trayIcon?.Dispose();
            rewards.Dispose();
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (settings.Locked || e.OriginalSource is Button) return;
            try { DragMove(); settings.Left = Left; settings.Top = Top; SaveSettings(); }
            catch (InvalidOperationException) { }
        };
        MouseRightButtonUp += (_, _) => { if (!settings.Locked) OpenSettings(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape && !settings.Locked) ToggleLock(); };
    }

    internal static TextBlock Text(string value, double size, string color, bool bold = false) => new()
    {
        Text = value, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
    };
    internal static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    private void BuildContent()
    {
        content.Width = 344;
        content.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 0.8 };
        card.Child = content;
        card.Padding = new Thickness(18, 14, 18, 12);
        card.CornerRadius = new CornerRadius(9);
        card.BorderThickness = new Thickness(1);
        card.Margin = new Thickness(2);
        Content = card;

        var settingsButton = SmallButton("设置", OpenSettings);
        var lockButton = SmallButton("锁定", ToggleLock);
        editButtons.Children.Add(settingsButton);
        editButtons.Children.Add(lockButton);
        DockPanel.SetDock(editButtons, Dock.Right);
        top.Children.Add(editButtons);
        top.Children.Add(heading);
        content.Children.Add(top);
        content.Children.Add(title);
        map.Margin = new Thickness(0, 4, 0, 8);
        content.Children.Add(map);
        stage.Margin = new Thickness(0, 0, 0, 12);
        content.Children.Add(stage);

        string[] labels = { "通关云点", "云点伤害转化比", "通关龙晶", "龙晶伤害转化比" };
        for (int i = 0; i < 4; i++)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, i == 1 ? 13 : 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Text(labels[i], 13, "#C7D0DB"));
            values[i] = Text("—", i % 2 == 0 ? 23 : 18, i < 2 ? "#B4E8FF" : "#F4D799", true);
            Grid.SetColumn(values[i], 1);
            row.Children.Add(values[i]);
            fieldRows[i] = row;
            rewardRows.Children.Add(row);
        }
        content.Children.Add(rewardRows);
        unavailable.Margin = new Thickness(0, 3, 0, 8);
        unavailablePanel.Children.Add(unavailable);
        reason.TextWrapping = TextWrapping.Wrap;
        reason.Margin = new Thickness(0, 0, 0, 10);
        unavailablePanel.Children.Add(reason);
        selectFileButton = SmallButton("选择广播文件…", SelectBroadcastFile);
        selectFileButton.Margin = new Thickness(0, 0, 0, 8);
        selectFileButton.HorizontalAlignment = HorizontalAlignment.Left;
        unavailablePanel.Children.Add(selectFileButton);
        content.Children.Add(unavailablePanel);
        footer.Children.Add(new Border { Height = 1, Background = Brush("#324354"), Margin = new Thickness(0, 0, 0, 8) });
        footer.Children.Add(source);
        hint.Margin = new Thickness(0, 5, 0, 0);
        hint.TextWrapping = TextWrapping.Wrap;
        footer.Children.Add(hint);
        content.Children.Add(footer);
    }

    private static Button SmallButton(string text, Action action)
    {
        var button = new Button { Content = text, FontSize = 10, Foreground = Brushes.White, Background = Brush("#263947"), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(5, 0, 0, 0), BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
        button.Click += (_, e) => { e.Handled = true; action(); };
        button.PreviewMouseLeftButtonDown += (_, e) => e.Handled = false;
        return button;
    }

    private void Tick()
    {
        if (demo)
        {
            Render(new(true, "ze_ffxii_westersand_v7_t", "最终幻想 XII · 西沙", 4, new(18, 20000, 12, 24000), "演示数据 · 非当前游戏", "伤害转化比 = 伤害 : 1 点货币"));
            return;
        }
        var broadcast = BroadcastReader.Read(settings.BroadcastPath);
        inputUnavailable = broadcast.FileUnavailable;
        if (inputUnavailable && interactiveSetup && notifiedMissingPath != settings.BroadcastPath)
        {
            notifiedMissingPath = settings.BroadcastPath;
            tray?.ShowBalloonTip(5000, "请选择广播文件", "当前无法读取实时文件。右键托盘 → 选择广播文件，或按 " + settings.SettingsHotkey + " 打开设置。", Forms.ToolTipIcon.Info);
        }
        else if (!inputUnavailable) notifiedMissingPath = "";
        if (identity != broadcast.Identity)
        {
            identity = broadcast.Identity;
            generation++;
            Render(DisplayInfo.Resolve(broadcast, null));
        }
        if (!broadcast.IsZe || broadcast.Stage is null || broadcast.Error.Length > 0 || loading) return;
        FetchAndRender(broadcast, generation);
    }

    private async void FetchAndRender(Broadcast broadcast, int version)
    {
        loading = true;
        try
        {
            var data = await rewards.GetAsync(broadcast.Map);
            // Re-read after every await: never put the previous map's result on a new map.
            if (!quitting && version == generation && BroadcastReader.Read(settings.BroadcastPath).Identity == broadcast.Identity)
                Render(DisplayInfo.Resolve(broadcast, data));
        }
        finally { loading = false; }
    }

    private void Render(DisplayInfo info)
    {
        if (lastDisplay == info) return;
        lastDisplay = info;
        title.Text = info.Available ? (string.IsNullOrWhiteSpace(info.Title) ? info.Map : info.Title) : "地图信息";
        title.ToolTip = title.Text;
        map.Text = info.Map;
        stage.Text = info.Available ? "第 " + info.Stage + " 关" : "";
        rewardRows.Visibility = info.Available ? Visibility.Visible : Visibility.Collapsed;
        unavailablePanel.Visibility = info.Available ? Visibility.Collapsed : Visibility.Visible;
        if (info.Reward is { } reward)
        {
            values[0].Text = reward.RankPasses.ToString();
            values[1].Text = DisplayInfo.Ratio(reward.RankDamage);
            values[2].Text = reward.EconPasses.ToString();
            values[3].Text = DisplayInfo.Ratio(reward.EconDamage);
        }
        reason.Text = info.Detail + (inputUnavailable ? "\n可从系统托盘选择广播文件，或按 " + settings.SettingsHotkey + " 打开设置。" : "");
        source.Text = info.Available ? info.Source : "等待有效的 ZE 地图与关卡";
        source.Foreground = Brush(info.Source.StartsWith("离线") ? "#F4D799" : "#ADB8C8");
        source.ToolTip = info.Detail;
        ApplyInformationVisibility();
    }

    private void ApplyInformationVisibility()
    {
        static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
        var info = lastDisplay;
        top.Visibility = Show(!settings.Hides(OverlayField.Heading) || !settings.Locked);
        title.Visibility = Show(!settings.Hides(OverlayField.MapTitle));
        map.Visibility = Show(!settings.Hides(OverlayField.MapName) && !string.IsNullOrWhiteSpace(info?.Map));
        stage.Visibility = Show(info?.Available == true && !settings.Hides(OverlayField.Stage));
        OverlayField[] rows = { OverlayField.RankPasses, OverlayField.RankDamage, OverlayField.EconPasses, OverlayField.EconDamage };
        for (int i = 0; i < rows.Length; i++) fieldRows[i].Visibility = Show(!settings.Hides(rows[i]));
        reason.Visibility = Show(!settings.Hides(OverlayField.ErrorDetail));
        source.Visibility = Show(!settings.Hides(OverlayField.Source));
        hint.Visibility = Show(!settings.Hides(OverlayField.HotkeyHint) || !settings.Locked);
        footer.Visibility = Show(source.Visibility == Visibility.Visible || hint.Visibility == Visibility.Visible);
        if (selectFileButton is not null) selectFileButton.Visibility = Show(inputUnavailable && !settings.Locked);
        // With every normal field hidden, keep the window invisible until edit mode or an error.
        var normalFields = OverlayField.All & ~OverlayField.ErrorDetail;
        card.Opacity = info?.Available == true && settings.Locked && (settings.HiddenFields & normalFields) == normalFields ? 0 : 1;
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
        if (error is not null) MessageBox.Show(error, "无法切换广播文件");
    }

    private void CreateTray()
    {
        using var iconResource = Application.GetResourceStream(Program.AppIconUri).Stream;
        trayIcon = new System.Drawing.Icon(iconResource, Forms.SystemInformation.SmallIconSize);
        tray = new Forms.NotifyIcon { Icon = trayIcon, Visible = true, Text = "ZE 奖励悬浮窗" };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏", null, (_, _) => Dispatcher.Invoke(ToggleVisibility));
        menu.Items.Add("切换拖动 / 锁定", null, (_, _) => Dispatcher.Invoke(ToggleLock));
        menu.Items.Add("设置…", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add("选择广播文件…", null, (_, _) => Dispatcher.Invoke(SelectBroadcastFile));
        menu.Items.Add("立即更新奖励", null, (_, _) => Dispatcher.Invoke(RefreshRewards));
        menu.Items.Add("恢复悬浮窗位置", null, (_, _) => Dispatcher.Invoke(ResetPosition));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Quit));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
    }

    private void HandleHotkey(int id)
    {
        if (id == 1) ToggleVisibility();
        else if (id == 2) ToggleLock();
        else if (id == 3) OpenSettings();
    }
    public void ToggleVisibility() { if (IsVisible) Hide(); else Show(); }
    public void ToggleLock()
    {
        settings.Locked = !settings.Locked;
        Show();
        ApplyAppearance();
        SaveSettings();
    }
    private void ApplyAppearance()
    {
        FontFamily = FontCatalog.Resolve(settings.OverlayFontFamily);
        double backgroundOpacity = settings.Locked ? settings.BackgroundOpacity : Math.Max(0.15, settings.BackgroundOpacity);
        card.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(backgroundOpacity * 255), 13, 20, 28));
        card.BorderBrush = settings.Locked ? Brushes.Transparent : Brush("#A4ED86");
        content.LayoutTransform = new ScaleTransform(settings.Scale, settings.Scale);
        editButtons.Visibility = settings.Locked ? Visibility.Collapsed : Visibility.Visible;
        heading.Text = settings.Locked ? "ZE  /  通关奖励" : "ZE  /  拖动模式";
        hint.Text = settings.Locked ? settings.ToggleHotkey + " 显隐   ·   " + settings.MoveHotkey + " 拖动" : "拖动空白区域移动 · Esc 锁定 · 右键设置";
        ApplyInformationVisibility();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) Native.SetClickThrough(handle, settings.Locked);
    }
    private void PlaceWindow()
    {
        double defaultLeft = SystemParameters.WorkArea.Right - ActualWidth - 24;
        Left = Math.Clamp(settings.Left ?? defaultLeft, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - ActualWidth));
        Top = Math.Clamp(settings.Top ?? SystemParameters.WorkArea.Top + 28, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - ActualHeight));
    }
    public void ResetPosition()
    {
        settings.Left = null; settings.Top = null;
        Show(); UpdateLayout(); PlaceWindow(); SaveSettings();
    }
    public void OpenSettings()
    {
        if (settingsWindow is not null) { settingsWindow.Activate(); return; }
        settingsWindow = new SettingsWindow(settings.Copy(), directory, ApplySettings, RefreshRewards, ResetPosition);
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
        settingsWindow.Activate();
    }
    private string? ApplySettings(Settings next)
    {
        if (hotkeys is null) return "快捷键尚未初始化";
        if (!hotkeys.Apply(next.Hotkeys, out var error)) return error;
        next.Left = settings.Left; next.Top = settings.Top; next.Locked = settings.Locked;
        try { next.Save(directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            hotkeys.Apply(settings.Hotkeys, out _);
            return "设置保存失败：" + ex.Message;
        }
        settings = next;
        identity = ""; generation++;
        ApplyUpdateIntervals();
        ApplyAppearance(); UpdateLayout(); PlaceWindow(); Tick();
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
        var margin = element.Margin;
        element.Margin = new Thickness(0);
        element.Measure(size);
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
        element.Margin = margin;
    }
    internal void RenderPreviews(string output)
    {
        Directory.CreateDirectory(output);
        ApplyAppearance();
        Render(new(true, "ze_ffxii_westersand_v7_t", "最终幻想 XII · 西沙", 4, new(18, 20000, 12, 24000), "演示数据 · 非当前游戏", "伤害转化比 = 伤害 : 1 点货币"));
        SaveVisual(card, Path.Combine(output, "reward-preview.png"), new Size(double.PositiveInfinity, double.PositiveInfinity));
        Render(DisplayInfo.Resolve(BroadcastReader.Parse("地图: fys_lobby_liyue"), null));
        SaveVisual(card, Path.Combine(output, "unavailable-preview.png"), new Size(double.PositiveInfinity, double.PositiveInfinity));
        var settingsPreview = new SettingsWindow(settings.Copy(), directory, _ => null, () => { }, () => { });
        for (int i = 0; i < 4; i++)
        {
            settingsPreview.SelectPreviewTab(i);
            SaveVisual((FrameworkElement)settingsPreview.Content, Path.Combine(output, "settings-tab-" + i + ".png"), new Size(588, 650));
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
        bool missingFileAction = !inputUnavailable || selectFileButton?.Visibility == Visibility.Visible;
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
        custom.HiddenFields = OverlayField.MapName | OverlayField.RankDamage | OverlayField.Source;
        var applyError = ApplySettings(custom);
        bool intervalsApplied = timer.Interval == TimeSpan.FromMilliseconds(250) && rewards.RefreshInterval == TimeSpan.FromMinutes(3);
        bool fontApplied = FontFamily.Source == "Consolas" && values[0].FontFamily.Source == "Consolas";
        bool fieldsHidden = map.Visibility == Visibility.Collapsed && fieldRows[1].Visibility == Visibility.Collapsed && source.Visibility == Visibility.Collapsed && fieldRows[0].Visibility == Visibility.Visible;
        var hiddenSettings = settings.Copy(); hiddenSettings.HiddenFields = OverlayField.All; ApplySettings(hiddenSettings);
        Render(DisplayInfo.Resolve(BroadcastReader.Invalid("测试不可用状态"), null));
        bool unavailableRetained = unavailablePanel.Visibility == Visibility.Visible && unavailable.Text == DisplayInfo.UnavailableText && card.Opacity == 1;
        lastDisplay = null;
        ApplySettings(originalSettings);
        var report = new { HotkeysRegistered = hotkeys?.RegisteredCount, LockedClickThrough = locked, UnlockedMouse = unlocked, HideWorks = hidden, ShowWorks = shown, PositionPersists = moved, MissingFileActionAvailable = missingFileAction, CustomSettingsAccepted = applyError is null, UpdateIntervalsApplied = intervalsApplied, CustomFontAppliedToTextAndNumbers = fontApplied, InformationHiddenIndependently = fieldsHidden, UnavailableMessageRetained = unavailableRetained, Display = lastDisplay };
        File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        SaveVisual(card, Path.ChangeExtension(output, ".png"), new Size(double.PositiveInfinity, double.PositiveInfinity));
        Quit();
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace FysZeRewardOverlay;

public sealed class SettingsWindow : Window
{
    private readonly Settings initial;
    private readonly RadioButton gsiSource;
    private readonly TextBox path = new();
    private readonly TextBlock pathStatus = OverlayWindow.Text("", 11, "#A1A1AA");
    private readonly TextBox pollSeconds = new();
    private readonly TextBox rewardMinutes = new();
    private readonly ComboBox fonts = new();
    private readonly TextBox textSize = new();
    private readonly TextBox textColor = new();
    private readonly Dictionary<OverlayField, TextStyleDraft> styleDrafts = new();
    private readonly Dictionary<OverlayField, RadioButton> targetButtons = new();
    private readonly Dictionary<string, RadioButton> swatches = new();
    private readonly RadioButton[] weights = new RadioButton[OverlayTextStyle.Weights.Length];
    private OverlayField textTarget;
    private bool loadingTextStyle;
    private readonly Dictionary<OverlayField, CheckBox> displayFields = new();
    private readonly List<(RadioButton Button, ScrollViewer Page)> pages = new();
    private readonly StackPanel navigation = new();
    private readonly StackPanel sidebarFooter = new();
    private readonly TextBlock gsiStatus = OverlayWindow.Text("GSI · 等待启动", 11, "#A1A1AA");
    private readonly ContentControl pageHost = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };
    private readonly DockPanel editor = new();
    private readonly TextBox[] shortcuts = new TextBox[5];
    private readonly TextBlock inferredStage = OverlayWindow.Text("等待比分推断关卡", 11, "#A1A1AA");
    private readonly Slider opacity = new() { Minimum = 0, Maximum = 95, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly Slider scale = new() { Minimum = 70, Maximum = 180, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly Slider rowSpacing = new() { Maximum = Settings.MaxRowSpacing, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly Slider columnSpacing = new()
    {
        Minimum = Settings.MinColumnSpacing, Maximum = Settings.MaxColumnSpacing, TickFrequency = 1, IsSnapToTickEnabled = true
    };
    private RewardValueAlignment valueAlignment;
    private readonly TextBlock error = OverlayWindow.Text("", 12, "#FFC2BC");
    private readonly TextBlock saved = OverlayWindow.Text("", 12, "#A4ED86");
    private readonly OverlaySettingsPreview preview;
    private readonly Func<Settings, string?> apply;

    public SettingsWindow(Settings initial, Func<Settings, string?> apply, Action refresh, Action reset,
        Action? toggleMove = null, Action<int>? adjustStage = null)
    {
        this.initial = initial;
        this.apply = apply;
        Title = "ZE 奖励悬浮窗 · 设置";
        var resources = new Uri($"pack://application:,,,/{typeof(SettingsWindow).Assembly.GetName().Name};component/", UriKind.Absolute);
        Icon = BitmapFrame.Create(new Uri(resources, Program.AppIconUri));
        Width = Math.Min(1240, SystemParameters.WorkArea.Width);
        Height = Math.Min(810, SystemParameters.WorkArea.Height);
        MinWidth = Math.Min(930, Width);
        MinHeight = Math.Min(560, Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 29, ResizeBorderThickness = new Thickness(5),
            GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        Background = OverlayWindow.Brush("#131313");
        Foreground = OverlayWindow.Brush("#F0F0F2");
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(resources, "SettingsTheme.xaml") });

        var root = new Grid { Background = Background };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        root.RowDefinitions.Add(new RowDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(186) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.12, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Content = root;
        UseLayoutRounding = true;

        var sidebarContent = new DockPanel();
        DockPanel.SetDock(sidebarFooter, Dock.Bottom);
        sidebarContent.Children.Add(sidebarFooter);
        sidebarContent.Children.Add(navigation);
        var sidebar = new Border
        {
            Background = OverlayWindow.Brush("#111112"), BorderBrush = OverlayWindow.Brush("#2B2B2E"),
            BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(15, 23, 15, 16), Child = sidebarContent
        };
        Grid.SetRowSpan(sidebar, 2);
        root.Children.Add(sidebar);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 5, 0, 30) };
        brand.Children.Add(new Image { Source = Icon, Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 0) });
        brand.Children.Add(OverlayWindow.Text("ZE 奖励悬浮窗", 15, "#F5F5F6", true));
        navigation.Children.Add(brand);

        Grid.SetRow(editor, 1);
        Grid.SetColumn(editor, 1);
        root.Children.Add(editor);
        var actionBar = new DockPanel { LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Button("关闭", Close, false));
        var save = Button("保存设置", Save, true);
        save.Margin = new Thickness(0);
        buttons.Children.Add(save);
        DockPanel.SetDock(buttons, Dock.Right);
        actionBar.Children.Add(buttons);
        error.TextWrapping = TextWrapping.Wrap;
        error.Margin = new Thickness(0, 0, 10, 0);
        var feedback = new Grid();
        feedback.Children.Add(error);
        feedback.Children.Add(saved);
        actionBar.Children.Add(feedback);
        var bottom = new Border
        {
            BorderBrush = OverlayWindow.Brush("#2B2B2E"), BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(24, 12, 24, 12), Child = actionBar
        };
        DockPanel.SetDock(bottom, Dock.Bottom);
        editor.Children.Add(bottom);
        editor.Children.Add(pageHost);

        preview = new OverlaySettingsPreview((Style)FindResource("Option")) { Margin = new Thickness(4, 8, 22, 20) };
        Grid.SetRow(preview, 1);
        Grid.SetColumn(preview, 2);
        root.Children.Add(preview);
        var caption = BuildCaptionButtons();
        Grid.SetColumn(caption, 1);
        Grid.SetColumnSpan(caption, 2);
        root.Children.Add(caption);

        var panel = AddPage("数据与文件", "\uE8A5");
        Label(panel, "地图来源");
        var txtSource = CreateSourceRow(BroadcastFiles.DisplayName);
        gsiSource = CreateSourceRow("CS2 GSI");
        txtSource.IsChecked = initial.MapSource == MapSource.Broadcast;
        gsiSource.IsChecked = initial.MapSource == MapSource.Gsi;
        panel.Children.Add(gsiSource);
        var gsiPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
        gsiPanel.Children.Add(Button("保存 GSI 配置文件…", SaveGsiConfig, true));
        Note(gsiPanel, @"将配置保存到 CS2 的 game\csgo\cfg 后重启游戏，即可读取击杀进度。选择 GSI 作为地图来源时，也可按 CT 比分推断关卡。");
        var adjustments = new WrapPanel { IsEnabled = false, Margin = new Thickness(0, 0, 0, 8) };
        if (adjustStage is not null)
        {
            adjustments.Children.Add(Button("降低关卡", () => adjustStage(-1), false));
            adjustments.Children.Add(Button("提高关卡", () => adjustStage(1), false));
            var stageControls = new StackPanel();
            stageControls.Children.Add(adjustments);
            stageControls.Children.Add(inferredStage);
            gsiPanel.Children.Add(stageControls);
            void UpdateStageControls() => stageControls.Visibility = UsesGsi ? Visibility.Visible : Visibility.Collapsed;
            gsiSource.Checked += (_, _) => UpdateStageControls();
            txtSource.Checked += (_, _) => UpdateStageControls();
            UpdateStageControls();
            inferredStage.IsEnabledChanged += (_, _) => adjustments.IsEnabled = inferredStage.IsEnabled;
            inferredStage.IsEnabled = false;
        }
        panel.Children.Add(gsiPanel);
        panel.Children.Add(txtSource);
        path.Text = initial.BroadcastPath; StyleTextBox(path);
        path.ToolTip = "可手动填写完整路径或 %LOCALAPPDATA% 等环境变量，只读此文件。";
        path.TextChanged += (_, _) => UpdatePathStatus();
        panel.Children.Add(path);
        var browse = Button("选择文件…", () =>
        {
            var selected = BroadcastFiles.Select(this, path.Text);
            if (selected is not null) path.Text = selected;
        }, true);
        var fileActions = new WrapPanel { Margin = new Thickness(0, 9, 0, 8) };
        fileActions.Children.Add(browse);
        fileActions.Children.Add(Button("恢复默认路径", () => path.Text = BroadcastFiles.DefaultPath, false));
        panel.Children.Add(fileActions);
        pathStatus.TextWrapping = TextWrapping.Wrap;
        pathStatus.Margin = new Thickness(0, 0, 0, 20);
        panel.Children.Add(pathStatus);
        void UpdateMapSourceControls()
        {
            txtSource.Content = UsesGsi ? BroadcastFiles.DisplayName + "（可选关卡来源）" : BroadcastFiles.DisplayName;
            if (!UsesGsi && string.IsNullOrWhiteSpace(path.Text)) path.Text = BroadcastFiles.DefaultPath;
            UpdatePathStatus();
        }
        txtSource.Checked += (_, _) => { UpdateMapSourceControls(); UpdatePreview(); };
        gsiSource.Checked += (_, _) => { UpdateMapSourceControls(); UpdatePreview(); };
        UpdateMapSourceControls();
        Label(panel, "数据更新频率");
        pollSeconds.Text = (initial.PollIntervalMilliseconds / 1000d).ToString("0.###", CultureInfo.CurrentCulture);
        rewardMinutes.Text = initial.RewardRefreshMinutes.ToString();
        AddNumber(panel, "地图 / 关卡读取", pollSeconds, "秒（0.2～60）");
        AddNumber(panel, "奖励联网更新", rewardMinutes, "分钟（1～1440）");
        Note(panel, "读取间隔越短，切图和关卡变化显示越快。奖励更新间隔控制联网查询频率；断网后每 30 秒重试。");
        panel.Children.Add(Button("立即更新奖励", () => { refresh(); SetError("已请求更新奖励。"); }, false));
        Note(panel, "在 FYS 助手的直播中心 → 直播助手 → 显示内容中勾选“地图”和“关卡”。叠层文本生成后会自动读取。");

        panel = AddPage("外观与字体", "\uE8D2");
        Section(panel, "布局");
        AddSlider(panel, "上下行间距", rowSpacing, initial.RowSpacing, "增加相邻显示行之间的距离", " px");
        AddSlider(panel, "左右文字间距", columnSpacing, initial.ColumnSpacing, "相对默认间距的调整量，负值可拉近奖励名称和数字", " px");
        Label(panel, "数值对齐");
        valueAlignment = initial.ValueAlignment;
        var alignmentOptions = new WrapPanel();
        foreach (var (alignment, label) in new[]
        {
            (RewardValueAlignment.Left, "左对齐"), (RewardValueAlignment.Center, "居中"), (RewardValueAlignment.Right, "右对齐")
        })
        {
            var option = Option(label, "ValueAlignment");
            option.IsChecked = valueAlignment == alignment;
            option.Checked += (_, _) => { valueAlignment = alignment; UpdatePreview(); };
            alignmentOptions.Children.Add(option);
        }
        panel.Children.Add(alignmentOptions);
        AddSlider(panel, "背景不透明度", opacity, initial.BackgroundOpacity * 100, "0% 为全透明，文字保持清晰");
        AddSlider(panel, "整体缩放", scale, initial.Scale * 100, "缩放悬浮窗和所有文字");
        var positionActions = new WrapPanel { Margin = new Thickness(0, 2, 0, 12) };
        if (toggleMove is not null) positionActions.Children.Add(Button("拖动 / 锁定悬浮窗", toggleMove, false));
        positionActions.Children.Add(Button("恢复悬浮窗位置", reset, false));
        panel.Children.Add(positionActions);
        Label(panel, "悬浮窗字体");
        fonts.ItemsSource = FontCatalog.Installed;
        fonts.ItemTemplate = (DataTemplate)FindResource("FontOptionLabel");
        TextSearch.SetTextPath(fonts, nameof(FontOption.DisplayName));
        fonts.SelectedValuePath = nameof(FontOption.Name); fonts.SelectedValue = FontCatalog.Resolve(initial.OverlayFontFamily).Source;
        fonts.Padding = new Thickness(8, 6, 8, 6); fonts.FontSize = 14;
        fonts.MaxDropDownHeight = 300; fonts.IsTextSearchEnabled = true;
        panel.Children.Add(fonts);
        BuildTextStyleEditor(panel);

        panel = AddPage("显示信息", "\uE890");
        Label(panel, "开启要在悬浮窗中显示的项目");
        var visibilityStyle = (Style)FindResource("VisibilitySwitch");
        foreach (var (field, label) in OverlayOptions.Fields)
        {
            var check = new CheckBox { Content = label, IsChecked = !initial.Hides(field), Style = visibilityStyle };
            displayFields.Add(field, check);
            panel.Children.Add(check);
        }
        var showButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        showButtons.Children.Add(Button("显示全部", () => { foreach (var check in displayFields.Values) check.IsChecked = true; }, false));
        showButtons.Children.Add(Button("仅显示四项奖励", () =>
        {
            foreach (var entry in displayFields) entry.Value.IsChecked = (entry.Key & (OverlayField.RankPasses | OverlayField.RankDamage | OverlayField.EconPasses | OverlayField.EconDamage)) != 0;
        }, false));
        panel.Children.Add(showButtons);
        Note(panel, "“当前无法获取该地图信息”仍会显示。拖动模式临时显示操作标题和提示，方便重新调整。");

        panel = AddPage("快捷键", "\uE765");
        Label(panel, "全局快捷键");
        var tip = OverlayWindow.Text("点击输入框后，直接按下新的组合键。", 11, "#A1A1AA");
        tip.Margin = new Thickness(0, 0, 0, 9); panel.Children.Add(tip);
        string[] labels = { "显示 / 隐藏", "拖动 / 锁定", "打开设置", "提高推断关卡", "降低推断关卡" };
        var current = initial.Hotkeys;
        for (int i = 0; i < shortcuts.Length; i++)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(OverlayWindow.Text(labels[i], 13, "#DEDEE4"));
            var box = new TextBox { Text = current[i], IsReadOnly = true, ToolTip = labels[i] + "快捷键" };
            AutomationProperties.SetName(box, labels[i] + "快捷键");
            StyleTextBox(box);
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Tab) return;
                e.Handled = true;
                string? value = Shortcut.FromEvent(e);
                if (value is not null) { box.Text = value; SetError(""); UpdatePreview(); }
            };
            shortcuts[i] = box;
            Grid.SetColumn(box, 1); row.Children.Add(box); panel.Children.Add(row);
        }
        var shortcutNote = OverlayWindow.Text("支持 Ctrl / Alt / Shift 加字母、数字、功能键或 = / -。关卡快捷键仅用于 CS2 GSI 的比分推断。", 10, "#A1A1AA");
        shortcutNote.TextWrapping = TextWrapping.Wrap;
        shortcutNote.Margin = new Thickness(0, 1, 0, 15); panel.Children.Add(shortcutNote);
        Note(panel, "快捷键对整个桌面生效。系统托盘始终提供显示、拖动、设置及选择文件的入口。");
        gsiStatus.Margin = new Thickness(11, 0, 0, 8);
        sidebarFooter.Children.Add(gsiStatus);
        AutomationProperties.SetLiveSetting(gsiStatus, AutomationLiveSetting.Polite);
        AddAboutPage();
        fonts.SelectionChanged += (_, _) => UpdatePreview();
        opacity.ValueChanged += (_, _) => UpdatePreview();
        scale.ValueChanged += (_, _) => UpdatePreview();
        rowSpacing.ValueChanged += (_, _) => UpdatePreview();
        columnSpacing.ValueChanged += (_, _) => UpdatePreview();
        foreach (var check in displayFields.Values)
        {
            check.Checked += (_, _) => UpdatePreview();
            check.Unchecked += (_, _) => UpdatePreview();
        }
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        SelectPage(initial.MapSource == MapSource.Broadcast && BroadcastReader.Read(initial.BroadcastPath).FileUnavailable ? 0 : 1);
        UpdatePreview();
    }

    private void AddAboutPage()
    {
        var panel = AddPage("关于", "\uE946", sidebarFooter);
        var assembly = typeof(SettingsWindow).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? assembly.GetName().Version?.ToString() ?? "";
        var repository = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(metadata => metadata.Key == "RepositoryUrl")?.Value
            ?? "https://github.com/creeper-kk/fys-ze-reward-overlay";

        Label(panel, "ZE 奖励悬浮窗");
        Note(panel, "版本 " + version);
        Note(panel, "作者 creeper_kk");
        Label(panel, "GitHub 仓库");
        var link = Button(repository, () => OpenAboutLink(repository), false);
        link.Content = new TextBlock { Text = repository, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(link);
        var acknowledgements = Button("致谢与开源依赖", () => OpenAboutLink(repository + "/blob/main/ACKNOWLEDGMENTS.md"), false);
        acknowledgements.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(acknowledgements);
    }

    private void OpenAboutLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Win32Exception ex) { SetError("无法打开链接：" + ex.Message); }
    }

    internal void UpdateGsiStatus(GsiListener.ConnectionState? state, string error, bool demo)
    {
        var (label, color, detail) = state switch
        {
            GsiListener.ConnectionState.Receiving => ("GSI · 正在接收", "#A4ED86", "正在接收 CS2 推送"),
            GsiListener.ConnectionState.TimedOut => ("GSI · 推送超时", "#F4D799", "已超过 15 秒未收到 CS2 推送"),
            GsiListener.ConnectionState.Waiting => ("GSI · 等待推送", "#A1A1AA", $"正在监听 127.0.0.1:{GsiListener.Port}"),
            _ when error.Length > 0 => ("GSI · 监听失败", "#FFC2BC", error),
            _ => (demo ? "GSI · 演示模式" : "GSI · 未启动", "#A1A1AA", "")
        };
        if (gsiStatus.Text != label)
        {
            gsiStatus.Text = label;
            gsiStatus.Foreground = OverlayWindow.Brush(color);
        }
        gsiStatus.ToolTip = detail.Length > 0 ? detail : null;
    }

    private RadioButton CreateSourceRow(string name)
    {
        var option = Option(name, "MapSource");
        option.HorizontalContentAlignment = HorizontalAlignment.Left;
        option.Margin = new Thickness(0, 0, 0, 8);
        return option;
    }

    internal void UpdateInferredStage(Broadcast broadcast, int offset)
    {
        inferredStage.IsEnabled = broadcast.CtScore.HasValue;
        inferredStage.Text = broadcast.CtScore.HasValue
            ? $"CT 比分 {broadcast.CtScore}  ·  推断第 {broadcast.Stage} 关  ·  偏移 {offset:+0;-0;0}"
            : broadcast.Stage.HasValue ? $"叠层文本已识别第 {broadcast.Stage} 关" : "等待比分推断关卡";
    }

    internal bool CaptureRegisteredShortcut(string shortcut)
    {
        if (!IsActive || Keyboard.FocusedElement is not TextBox box || !shortcuts.Contains(box)) return false;
        box.Text = shortcut;
        SetError("");
        UpdatePreview();
        return true;
    }

    private StackPanel BuildCaptionButtons()
    {
        var caption = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        WindowChrome.SetIsHitTestVisibleInChrome(caption, true);
        AddButton("最小化", "\uE921", () => SystemCommands.MinimizeWindow(this));
        var maximize = AddButton("最大化", "\uE922", () =>
        {
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        });
        AddButton("关闭", "\uE8BB", Close).Style = (Style)FindResource("CloseCaptionButton");
        StateChanged += (_, _) =>
        {
            bool maximized = WindowState == WindowState.Maximized;
            maximize.Content = maximized ? "\uE923" : "\uE922";
            maximize.ToolTip = maximized ? "还原" : "最大化";
            AutomationProperties.SetName(maximize, (string)maximize.ToolTip);
        };
        return caption;

        Button AddButton(string label, string glyph, Action action)
        {
            var button = new Button
            {
                Style = (Style)FindResource("CaptionButton"), Content = glyph, ToolTip = label
            };
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => action();
            caption.Children.Add(button);
            return button;
        }
    }

    private void BuildTextStyleEditor(StackPanel panel)
    {
        Section(panel, "文字样式");
        Label(panel, "应用到");
        var targets = new WrapPanel();
        AddTarget(OverlayField.None, "全局文字");
        foreach (var entry in OverlayTypography.Labels) AddTarget(entry.Key, entry.Value);
        panel.Children.Add(targets);

        foreach (var entry in initial.FieldTextStyles) styleDrafts[entry.Key] = TextStyleDraft.FromStyle(entry.Value);
        styleDrafts[OverlayField.None] = TextStyleDraft.FromStyle(initial.GlobalTextStyle);
        Note(panel, "单项未设置的属性沿用全局。全局未设置时使用默认样式。");

        Label(panel, "字号  8–64");
        var sizeRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        sizeRow.Children.Add(Button("A−", () => AdjustSize(-1), false));
        StyleTextBox(textSize);
        textSize.Width = 68;
        textSize.HorizontalContentAlignment = HorizontalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(textSize, "字号");
        textSize.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Up or Key.Down)) return;
            AdjustSize(e.Key == Key.Up ? 1 : -1);
            e.Handled = true;
        };
        var sizeInput = new Grid { Margin = new Thickness(0, 0, 8, 0) };
        var sizePlaceholder = OverlayWindow.Text("沿用", 12, "#A1A1AA");
        sizePlaceholder.HorizontalAlignment = HorizontalAlignment.Center;
        sizePlaceholder.IsHitTestVisible = false;
        sizeInput.Children.Add(textSize);
        sizeInput.Children.Add(sizePlaceholder);
        sizeRow.Children.Add(sizeInput);
        sizeRow.Children.Add(Button("A＋", () => AdjustSize(1), false));
        var resetSize = Button("沿用", () => textSize.Text = "", false);
        resetSize.ToolTip = "清除字号覆盖";
        sizeRow.Children.Add(resetSize);
        panel.Children.Add(sizeRow);

        Label(panel, "文字颜色");
        var palette = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        (string Color, string Name, bool DarkMark)[] colors =
        {
            ("#FFFFFF", "白色", true), ("#39A7E5", "蓝色", true), ("#32BDB7", "青色", true),
            ("#A4ED86", "绿色", true), ("#F7D34C", "黄色", true), ("#FF9D3F", "橙色", true),
            ("#F45858", "红色", true), ("#F272A0", "粉色", true), ("#A366D6", "紫色", false)
        };
        foreach (var (color, name, darkMark) in colors)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("Swatch"), GroupName = "TextColor", Background = OverlayWindow.Brush(color),
                Foreground = darkMark ? Brushes.Black : Brushes.White, ToolTip = name + " " + color,
                Margin = new Thickness(0, 0, 6, 6)
            };
            System.Windows.Automation.AutomationProperties.SetName(swatch, name + " " + color);
            swatch.Click += (_, _) => textColor.Text = color;
            swatches.Add(color, swatch);
            palette.Children.Add(swatch);
        }
        var inheritColor = Option("沿用", "TextColor");
        inheritColor.ToolTip = "清除颜色覆盖";
        inheritColor.Click += (_, _) => textColor.Text = "";
        swatches.Add("", inheritColor);
        palette.Children.Add(inheritColor);
        var customColor = Button("选色…", PickTextColor, false);
        palette.Children.Add(customColor);
        StyleTextBox(textColor);
        textColor.Width = 94;
        textColor.ToolTip = "#RRGGBB";
        textColor.Margin = new Thickness(0, 0, 0, 6);
        System.Windows.Automation.AutomationProperties.SetName(textColor, "自定义十六进制颜色");
        palette.Children.Add(textColor);
        panel.Children.Add(palette);

        Label(panel, "字重");
        var weightRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        for (int i = 0; i < OverlayTextStyle.Weights.Length; i++)
        {
            int index = i;
            var (value, name) = OverlayTextStyle.Weights[i];
            var button = Option(name, "TextWeight");
            button.FontWeight = FontWeight.FromOpenTypeWeight(value ?? 400);
            button.Click += (_, _) => StoreTextDraft(weightIndex: index);
            weights[i] = button;
            weightRow.Children.Add(button);
        }
        panel.Children.Add(weightRow);
        panel.Children.Add(Button("清除所选项样式", () => LoadTextStyle(TextStyleDraft.Empty), false));

        textSize.TextChanged += (_, _) =>
        {
            sizePlaceholder.Visibility = string.IsNullOrEmpty(textSize.Text) ? Visibility.Visible : Visibility.Collapsed;
            StoreTextDraft();
        };
        textColor.TextChanged += (_, _) => StoreTextDraft();
        SelectTextTarget(OverlayField.None);

        void AddTarget(OverlayField field, string name)
        {
            var button = Option(name, "TextTarget");
            button.Click += (_, _) => SelectTextTarget(field);
            targetButtons.Add(field, button);
            targets.Children.Add(button);
        }
    }

    private void SelectTextTarget(OverlayField field)
    {
        textTarget = field;
        targetButtons[field].IsChecked = true;
        LoadTextStyle(styleDrafts.GetValueOrDefault(field, TextStyleDraft.Empty));
    }

    private void LoadTextStyle(TextStyleDraft draft)
    {
        loadingTextStyle = true;
        textSize.Text = draft.Size;
        textColor.Text = draft.Color;
        loadingTextStyle = false;
        StoreTextDraft(draft.WeightIndex);
    }

    private void StoreTextDraft(int? weightIndex = null)
    {
        if (loadingTextStyle) return;
        var previous = styleDrafts.GetValueOrDefault(textTarget, TextStyleDraft.Empty);
        var draft = new TextStyleDraft(textSize.Text, textColor.Text, weightIndex ?? previous.WeightIndex);
        styleDrafts[textTarget] = draft;
        weights[draft.WeightIndex].IsChecked = true;
        string selected = draft.Color.Trim().ToUpperInvariant();
        foreach (var entry in swatches) entry.Value.IsChecked = entry.Key == selected;
        UpdatePreview();
    }

    private OverlayTextStyle EffectiveTextStyle()
    {
        var global = styleDrafts[OverlayField.None].GetStyle(out _);
        return styleDrafts[textTarget].GetStyle(out _).WithFallback(global).WithFallback(OverlayTypography.DefaultFor(textTarget));
    }

    private void AdjustSize(int step)
    {
        double size = Math.Clamp(EffectiveTextStyle().Size!.Value + step, OverlayTextStyle.MinSize, OverlayTextStyle.MaxSize);
        textSize.Text = size.ToString(CultureInfo.CurrentCulture);
    }

    private void UpdatePreview()
    {
        if (shortcuts[^1] is null) return;
        saved.Text = "";
        var draft = ReadAppearance();
        foreach (var (field, textDraft) in styleDrafts)
        {
            var style = textDraft.GetStyle(out _);
            if (field == OverlayField.None) draft.GlobalTextStyle = style;
            else draft.FieldTextStyles[field] = style;
        }
        preview.Update(draft, textTarget);
    }

    private void PickTextColor()
    {
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.ColorTranslator.FromHtml(EffectiveTextStyle().Color!)
        };
        var owner = new DialogOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        if (dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK)
            textColor.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    }

    private sealed class DialogOwner(IntPtr handle) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle => handle;
    }

    private bool TrySaveTextStyles(Settings next)
    {
        next.FieldTextStyles.Clear();
        foreach (var (field, draft) in styleDrafts)
        {
            var style = draft.GetStyle(out var message);
            if (message is not null)
            {
                SelectPage(1);
                SelectTextTarget(field);
                string target = field == OverlayField.None ? "全局文字" : OverlayTypography.Labels[field];
                SetError(target + "：" + message);
                textSize.BringIntoView();
                return false;
            }
            if (field == OverlayField.None) next.GlobalTextStyle = style;
            else if (style.Size.HasValue || style.Color is not null || style.Weight.HasValue) next.FieldTextStyles.Add(field, style);
        }
        return true;
    }

    private sealed record TextStyleDraft(string Size, string Color, int WeightIndex)
    {
        internal static TextStyleDraft Empty { get; } = new("", "", 0);

        internal static TextStyleDraft FromStyle(OverlayTextStyle style) => new(
            style.Size?.ToString(CultureInfo.CurrentCulture) ?? "", style.Color ?? "",
            Math.Max(0, Array.FindIndex(OverlayTextStyle.Weights, option => option.Value == style.Weight)));

        internal OverlayTextStyle GetStyle(out string? error)
        {
            double? size = null;
            string? color = null;
            error = null;
            if (!string.IsNullOrWhiteSpace(Size))
            {
                if (double.TryParse(Size, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && OverlayTextStyle.IsValidSize(value))
                    size = value;
                else error = "字号请输入 8～64 之间的数字，或留空。";
            }
            if (!string.IsNullOrWhiteSpace(Color))
            {
                var hex = Color.Trim();
                if (OverlayTextStyle.IsValidColor(hex)) color = hex.ToUpperInvariant();
                else error ??= "颜色请输入 #RRGGBB 或通过选色按钮设置。";
            }
            return new(size, color, OverlayTextStyle.Weights[WeightIndex].Value);
        }
    }

    private StackPanel AddPage(string label, string glyph, Panel? navigationHost = null)
    {
        var panel = new StackPanel { Margin = new Thickness(24, 23, 24, 20) };
        var pageTitle = OverlayWindow.Text(label, 23, "#F5F5F6", true);
        pageTitle.Margin = new Thickness(0, 0, 0, 22);
        panel.Children.Add(pageTitle);
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel, CanContentScroll = false
        };
        var icon = OverlayWindow.Text(glyph, 15, "#A9B4C2");
        icon.FontFamily = new FontFamily("Segoe MDL2 Assets");
        icon.Margin = new Thickness(0, 0, 10, 0);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(icon);
        content.Children.Add(OverlayWindow.Text(label, 13, "#E8E8EC", true));
        var option = Option(content, "Navigation");
        option.Background = Brushes.Transparent;
        option.HorizontalContentAlignment = HorizontalAlignment.Left;
        option.MinHeight = 42;
        option.Margin = new Thickness(0, 0, 0, 5);
        System.Windows.Automation.AutomationProperties.SetName(option, label);
        int index = pages.Count;
        option.Click += (_, _) => SelectPage(index);
        (navigationHost ?? navigation).Children.Add(option);
        pages.Add((option, scroll));
        return panel;
    }

    private void SelectPage(int index)
    {
        var (button, page) = pages[index];
        button.IsChecked = true;
        pageHost.Content = page;
        bool about = button.Parent == sidebarFooter;
        preview.Visibility = about ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumnSpan(editor, about ? 2 : 1);
    }

    private RadioButton Option(object content, string group) => new()
    {
        Content = content, GroupName = group, Style = (Style)FindResource("Option"),
        HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 6, 6)
    };

    private static void Section(StackPanel panel, string title)
    {
        panel.Children.Add(new Border { Height = 1, Background = OverlayWindow.Brush("#323235"), Margin = new Thickness(0, 20, 0, 16) });
        var label = Label(panel, title);
        label.FontSize = 16;
        label.Margin = new Thickness(0, 0, 0, 14);
    }

    private static void Note(Panel panel, string text)
    {
        var note = OverlayWindow.Text(text, 11, "#A1A1AA");
        note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(0, 9, 0, 15); panel.Children.Add(note);
    }
    private static void AddNumber(Panel panel, string label, TextBox input, string unit)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(142) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(OverlayWindow.Text(label, 12, "#DEDEE4"));
        StyleTextBox(input); input.ToolTip = label; Grid.SetColumn(input, 1); row.Children.Add(input);
        var suffix = OverlayWindow.Text(unit, 11, "#A1A1AA"); suffix.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(suffix, 2); row.Children.Add(suffix); panel.Children.Add(row);
    }
    private void UpdatePathStatus()
    {
        if (UsesGsi && string.IsNullOrWhiteSpace(path.Text)) { pathStatus.Text = "路径留空时按 CT 比分推断关卡。"; return; }
        if (!BroadcastFiles.TryNormalize(path.Text, out var normalized)) { pathStatus.Text = "请填写叠层文本的完整路径，或点击“选择文件”。"; return; }
        var broadcast = BroadcastReader.Read(normalized);
        pathStatus.Text = broadcast.FileUnavailable ? broadcast.Error + "。" : "叠层文本可读取。" + broadcast.Error;
    }

    private bool UsesGsi => gsiSource.IsChecked == true;

    private void SaveGsiConfig()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "保存 CS2 GSI 配置文件", FileName = GsiListener.ConfigFileName,
            Filter = "CS2 配置文件 (*.cfg)|*.cfg", DefaultExt = ".cfg", OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, GsiListener.Config);
            SetError(@"GSI 配置已保存。请确认文件位于 game\csgo\cfg，再重启 CS2。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError("GSI 配置保存失败：" + ex.Message);
        }
    }

    private static TextBlock Label(Panel panel, string text)
    {
        var label = OverlayWindow.Text(text, 13, "#E6E6EB", true);
        label.Margin = new Thickness(0, 0, 0, 8); panel.Children.Add(label);
        return label;
    }
    private static void StyleTextBox(TextBox box)
    {
        box.Padding = new Thickness(9, 7, 9, 7); box.Background = OverlayWindow.Brush("#252527");
        box.Foreground = OverlayWindow.Brush("#EFF5FD"); box.BorderBrush = OverlayWindow.Brush("#38383D");
        box.CaretBrush = Brushes.White; box.FontSize = 12;
    }
    private Button Button(string text, Action action, bool primary)
    {
        var button = new Button
        {
            Content = text, Style = (Style)FindResource("SettingsButton"),
            Margin = new Thickness(0, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Left
        };
        if (primary) button.Background = OverlayWindow.Brush("#006BD0");
        button.Click += (_, _) => action();
        return button;
    }

    private static void AddSlider(Panel panel, string label, Slider slider, double value, string description, string unit = "%")
    {
        var header = new DockPanel { Margin = new Thickness(0, 9, 0, 6) };
        var amount = OverlayWindow.Text("", 13, "#E6E6EB", true);
        DockPanel.SetDock(amount, Dock.Right); header.Children.Add(amount);
        header.Children.Add(OverlayWindow.Text(label, 13, "#DEDEE4")); panel.Children.Add(header);
        slider.ValueChanged += (_, _) => amount.Text = Math.Round(slider.Value) + unit;
        slider.Value = value; amount.Text = Math.Round(slider.Value) + unit;
        slider.ToolTip = label; panel.Children.Add(slider);
        AutomationProperties.SetName(slider, label);
        var note = OverlayWindow.Text(description, 10, "#A1A1AA");
        note.Margin = new Thickness(0, 5, 0, 7); panel.Children.Add(note);
    }
    private void Save()
    {
        saved.Text = "";
        string selectedPath = "";
        if ((!UsesGsi || !string.IsNullOrWhiteSpace(path.Text)) && !BroadcastFiles.TryNormalize(path.Text, out selectedPath))
        { SelectPage(0); SetError("请填写" + BroadcastFiles.DisplayName + "的绝对路径。"); return; }
        if (!OverlayOptions.TryIntervals(pollSeconds.Text, rewardMinutes.Text, out int milliseconds, out int minutes, out var validation))
        { SelectPage(0); SetError(validation); return; }
        var next = ReadAppearance();
        if (!TrySaveTextStyles(next)) return;
        next.BroadcastPath = selectedPath;
        next.PollIntervalMilliseconds = milliseconds;
        next.RewardRefreshMinutes = minutes;
        var message = apply(next);
        if (message is not null) SetError(message);
        else
        {
            error.Text = "";
            saved.Text = "设置已保存";
        }
    }
    private Settings ReadAppearance()
    {
        var draft = initial.Copy();
        draft.MapSource = UsesGsi ? MapSource.Gsi : MapSource.Broadcast;
        draft.ToggleHotkey = shortcuts[0].Text;
        draft.MoveHotkey = shortcuts[1].Text;
        draft.SettingsHotkey = shortcuts[2].Text;
        draft.StageUpHotkey = shortcuts[3].Text;
        draft.StageDownHotkey = shortcuts[4].Text;
        draft.BackgroundOpacity = opacity.Value / 100;
        draft.Scale = scale.Value / 100;
        draft.RowSpacing = (int)rowSpacing.Value;
        draft.ColumnSpacing = (int)columnSpacing.Value;
        draft.ValueAlignment = valueAlignment;
        draft.OverlayFontFamily = (fonts.SelectedItem as FontOption)?.Name ?? FontCatalog.DefaultName;
        draft.HiddenFields = displayFields.Where(entry => entry.Value.IsChecked != true)
            .Aggregate(OverlayField.None, (fields, entry) => fields | entry.Key);
        return draft;
    }

    public void SetError(string value)
    {
        saved.Text = "";
        error.Text = value;
    }
    internal void SelectPreviewTab(int index) => SelectPage(index);
}

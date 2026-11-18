using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace FysZeRewardOverlay;

public sealed class SettingsWindow : Window
{
    private const string GlobalTextLabel = "全局配置";
    private const string UseGlobalTextLabel = OverlayTextStyle.UseGlobalLabel;
    private const string UseDefaultTextLabel = "使用默认样式";
    private readonly RadioButton txtSource;
    private readonly RadioButton gsiSource;
    private readonly TextBlock mapSourceHint = OverlayWindow.Text("", 12, "#A1A1AA");
    private readonly TextBlock gsiConfigLabel = OverlayWindow.Text("", 13, "#E6E6EB", true);
    private readonly TextBlock broadcastPathLabel = OverlayWindow.Text("", 13, "#E6E6EB", true);
    private readonly StackPanel gsiStageControls = new();
    private readonly CheckBox isSvip = new();
    private readonly CheckBox useNativeDamage = new()
    {
        Tag = "击杀估算", Content = "原生伤害", IsEnabled = Settings.NativeDamageEnabled
    };
    private readonly TextBlock roundDamageStatus = OverlayWindow.Text("GSI 本回合伤害 · 等待 GSI 推送", 11, "#A1A1AA");
    private readonly TextBox path = new();
    private readonly TextBlock pathStatus = OverlayWindow.Text("", 11, "#A1A1AA");
    private readonly TextBox pollSeconds = new();
    private readonly TextBox rewardMinutes = new();
    private readonly CheckBox autoUpdate = new() { Tag = "关闭", Content = "开启" };
    private readonly TextBox autoUpdateDays = new();
    private int aboutPageIndex;
    internal AppUpdates? Updates { get; init; }
    private readonly ComboBox fonts = new();
    private readonly ComboBox textTargets = new() { Width = 224, MaxDropDownHeight = 300, SelectedValuePath = nameof(Tag) };
    private readonly TextBox textSize = new();
    private readonly TextBlock textSizePlaceholder = OverlayWindow.Text("", 12, "#A1A1AA");
    private readonly TextBox textColor = new();
    private readonly ComboBox textWeight = new() { Width = 150, MaxDropDownHeight = 300 };
    private readonly Dictionary<OverlayField, TextStyleDraft> styleDrafts = new();
    private readonly Dictionary<string, RadioButton> swatches = new();
    private readonly ComboBox kzColorTargets = new() { Width = 150, MaxDropDownHeight = 300 };
    private readonly TextBox kzColorValue = new() { Width = 80 };
    private readonly Dictionary<string, RadioButton> kzSwatches = new();
    private readonly Dictionary<string, string> kzColorDrafts = new();
    private Button resetTextSize = null!;
    private Button resetTextColor = null!;
    private OverlayField textTarget;
    private bool loadingTextStyle;
    private readonly Dictionary<OverlayField, CheckBox> displayFields = new();
    private readonly StackPanel displayRows = new() { Background = Brushes.Transparent };
    private readonly Dictionary<OverlayField, Grid> sortableRows = new();
    private readonly Border displayDropSlot = new()
    {
        Background = OverlayWindow.Brush("#202F40"), BorderBrush = OverlayWindow.Brush("#349CFF"),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Visibility = Visibility.Collapsed,
        VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, RenderTransform = new TranslateTransform()
    };
    private Grid? draggedDisplayRow;
    private int displayDragTarget = -1;
    private readonly CheckBox obsEnabled = new() { Content = "启用 OBS 浏览器源" };
    private readonly CheckBox kzEnabled = new() { Tag = "关闭", Content = "开启" };
    private readonly ComboBox kzMode = new() { SelectedValuePath = nameof(Tag) };
    private readonly KzMapPanel kzMapPanel = new() { Visibility = Visibility.Collapsed };
    private int kzPageIndex;
    private readonly TextBlock obsStatus = OverlayWindow.Text("", 12, "#A1A1AA");
    private readonly TextBox obsUrl = new() { Text = ObsBrowserSource.Url, IsReadOnly = true };
    private Button obsOpen = null!;
    private readonly Dictionary<RewardValueAlignment, RadioButton> alignmentOptions = new();
    private readonly List<(RadioButton Button, ScrollViewer Page)> pages = new();
    private readonly StackPanel navigation = new();
    private readonly StackPanel sidebarFooter = new();
    private readonly Grid sidebarBody = new() { ClipToBounds = true };
    private readonly TranslateTransform navigationHoverOffset = new();
    private readonly Border navigationHover = new()
    {
        Background = OverlayWindow.Brush("#28282C"), CornerRadius = new CornerRadius(7), Opacity = 0,
        VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false
    };
    private readonly TextBlock gsiStatus = OverlayWindow.Text("GSI · 等待启动", 11, "#A1A1AA");
    private readonly ContentControl pageHost = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };
    private readonly DockPanel editor = new();
    private readonly DockPanel rightPane = new() { Margin = new Thickness(4, 8, 22, 20) };
    private readonly Border integration;
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
    private readonly Func<Settings> currentSettings;
    private readonly Func<Settings, bool, string?> apply;
    private bool loadingSettings;

    public SettingsWindow(Func<Settings> currentSettings, Func<Settings, bool, string?> apply, Action refresh, Action reset,
        Action? toggleMove = null, Action<int>? adjustStage = null)
    {
        this.currentSettings = currentSettings;
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
        navigationHover.RenderTransform = navigationHoverOffset;
        sidebarBody.Children.Add(navigationHover);
        sidebarBody.Children.Add(sidebarContent);
        sidebarBody.SizeChanged += (_, _) =>
        {
            if (pages.FirstOrDefault(item => item.Button.IsMouseOver).Button is { } button)
                MoveNavigationHover(button, animate: false);
        };
        var sidebar = new Border
        {
            Background = OverlayWindow.Brush("#111112"), BorderBrush = OverlayWindow.Brush("#2B2B2E"),
            BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(15, 23, 15, 16), Child = sidebarBody
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

        preview = new OverlaySettingsPreview((Style)FindResource("Option"));
        integration = BuildComponentIntegration();
        DockPanel.SetDock(integration, Dock.Bottom);
        rightPane.Children.Add(integration);
        rightPane.Children.Add(new Grid { Children = { preview, kzMapPanel } });
        Grid.SetRow(rightPane, 1);
        Grid.SetColumn(rightPane, 2);
        root.Children.Add(rightPane);
        var caption = BuildCaptionButtons();
        Grid.SetColumn(caption, 1);
        Grid.SetColumnSpan(caption, 2);
        root.Children.Add(caption);

        var panel = AddPage("数据与文件", "\uE8A5");
        var sourceLabel = Label(panel, "地图与关卡来源");
        txtSource = CreateSourceOption(BroadcastFiles.DisplayName);
        gsiSource = CreateSourceOption("CS2 GSI");
        gsiSource.Margin = new Thickness(0, 0, 4, 0);
        txtSource.Margin = new Thickness(4, 0, 0, 0);
        var sourceOptions = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
        sourceOptions.Children.Add(gsiSource);
        sourceOptions.Children.Add(txtSource);
        AutomationProperties.SetLabeledBy(sourceOptions, sourceLabel);
        panel.Children.Add(sourceOptions);
        mapSourceHint.TextWrapping = TextWrapping.Wrap;
        mapSourceHint.Margin = new Thickness(0, 8, 0, 16);
        panel.Children.Add(mapSourceHint);
        var gsiPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
        gsiConfigLabel.Margin = new Thickness(0, 0, 0, 8);
        gsiPanel.Children.Add(gsiConfigLabel);
        var gsiActions = new WrapPanel();
        var autoConfig = Button("自动配置", null, true);
        var gsiConfigStatus = OverlayWindow.Text("", 11, "#A1A1AA");
        gsiConfigStatus.TextWrapping = TextWrapping.Wrap;
        gsiConfigStatus.Margin = new Thickness(0, 8, 0, 0);
        gsiConfigStatus.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(gsiConfigStatus, AutomationLiveSetting.Polite);
        autoConfig.Click += async (_, _) => await ConfigureGsiAutomatically(autoConfig, gsiConfigStatus);
        gsiActions.Children.Add(autoConfig);
        gsiActions.Children.Add(Button("手动配置", SaveGsiConfig, false));
        gsiPanel.Children.Add(gsiActions);
        gsiPanel.Children.Add(gsiConfigStatus);
        Note(gsiPanel, @"自动配置会写入并打开 CS2 的 game\csgo\cfg 文件夹。配置后重启 CS2。");
        var adjustments = new WrapPanel { IsEnabled = false, Margin = new Thickness(0, 0, 0, 8) };
        if (adjustStage is not null)
        {
            adjustments.Children.Add(Button("减少关卡计数", () => adjustStage(-1), false));
            adjustments.Children.Add(Button("增加关卡计数", () => adjustStage(1), false));
            gsiStageControls.Children.Add(adjustments);
            gsiStageControls.Children.Add(inferredStage);
            gsiPanel.Children.Add(gsiStageControls);
            inferredStage.IsEnabledChanged += (_, _) => adjustments.IsEnabled = inferredStage.IsEnabled;
            inferredStage.IsEnabled = false;
        }
        panel.Children.Add(gsiPanel);
        broadcastPathLabel.Margin = new Thickness(0, 4, 0, 8);
        panel.Children.Add(broadcastPathLabel);
        StyleTextBox(path);
        AutomationProperties.SetLabeledBy(path, broadcastPathLabel);
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
        pathStatus.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(pathStatus);
        txtSource.Checked += (_, _) => OnMapSourceChanged();
        gsiSource.Checked += (_, _) => OnMapSourceChanged();
        panel.Children.Add(CreateSwitchRow("是否为SVIP", isSvip, new Thickness(0, 0, 0, 8)));
        var damageRow = CreateSwitchRow("伤害计算（原生伤害暂不可用）", useNativeDamage, new Thickness(0, 0, 0, 3));
        ToolTipService.SetShowOnDisabled(useNativeDamage, true);
        useNativeDamage.ToolTip = "当前使用 GSI 击杀数估算伤害，每次击杀按 1,000 伤害计算。";
        panel.Children.Add(damageRow);
        roundDamageStatus.Margin = new Thickness(0, 0, 0, 16);
        AutomationProperties.SetName(roundDamageStatus, "当前回合原生伤害");
        panel.Children.Add(roundDamageStatus);
        Label(panel, "数据更新频率");
        AddNumber(panel, "地图 / 关卡读取", pollSeconds, "秒（0.2～60）");
        AddNumber(panel, "奖励联网更新", rewardMinutes, $"分钟（1～{Settings.MaxRewardRefreshMinutes}）");
        Note(panel, "读取间隔越短，切图和关卡变化显示越快。奖励更新间隔控制联网查询频率；断网后每 30 秒重试。");
        panel.Children.Add(Button("立即更新奖励", () => { refresh(); SetError("已请求更新奖励。"); }, false));
        Note(panel, "在 FYS 助手的直播中心 → 直播助手 → 显示内容中勾选“地图”和“关卡”。叠层文本生成后会自动读取。");

        panel = AddPage("外观与字体", "\uE8D2");
        Section(panel, "布局");
        AddSlider(panel, "上下行间距", rowSpacing, "增加相邻显示行之间的距离", " px");
        AddSlider(panel, "左右文字间距", columnSpacing, "相对默认间距的调整量，负值可拉近奖励名称和数字", " px");
        Label(panel, "数值对齐");
        var alignmentRow = new WrapPanel();
        foreach (var (alignment, label) in new[]
        {
            (RewardValueAlignment.Left, "左对齐"), (RewardValueAlignment.Center, "居中"), (RewardValueAlignment.Right, "右对齐")
        })
        {
            var option = Option(label, "ValueAlignment");
            option.Checked += (_, _) => { valueAlignment = alignment; UpdatePreview(); };
            alignmentOptions.Add(alignment, option);
            alignmentRow.Children.Add(option);
        }
        panel.Children.Add(alignmentRow);
        AddSlider(panel, "背景不透明度", opacity, "0% 为全透明，文字保持清晰");
        AddSlider(panel, "整体缩放", scale, "缩放悬浮窗和所有文字");
        var positionActions = new WrapPanel { Margin = new Thickness(0, 2, 0, 12) };
        if (toggleMove is not null) positionActions.Children.Add(Button("拖动 / 锁定悬浮窗", toggleMove, false));
        positionActions.Children.Add(Button("恢复悬浮窗位置", reset, false));
        panel.Children.Add(positionActions);
        Label(panel, "悬浮窗字体");
        fonts.ItemsSource = FontCatalog.Installed;
        fonts.ItemTemplate = (DataTemplate)FindResource("FontOptionLabel");
        TextSearch.SetTextPath(fonts, nameof(FontOption.DisplayName));
        fonts.SelectedValuePath = nameof(FontOption.Name);
        fonts.Padding = new Thickness(8, 6, 8, 6); fonts.FontSize = 14;
        fonts.MaxDropDownHeight = 300; fonts.IsTextSearchEnabled = true;
        panel.Children.Add(fonts);
        BuildTextStyleEditor(panel);
        BuildKzColorEditor(panel);

        panel = AddPage("显示信息", "\uE890");
        Label(panel, "开启要在悬浮窗中显示的项目");
        Note(panel, "按住左侧的拖动柄上下移动，调整悬浮窗和 OBS 的显示顺序。");
        var visibilityStyle = (Style)FindResource("VisibilitySwitch");
        var displayList = new Grid();
        displayList.Children.Add(displayDropSlot);
        displayList.Children.Add(displayRows);
        foreach (var (field, label) in OverlayOptions.Fields)
        {
            var check = new CheckBox { Content = label, Style = visibilityStyle };
            displayFields.Add(field, check);
            if (!OverlayOptions.ReorderableFields.Contains(field)) panel.Children.Add(check);
            else
            {
                if (displayList.Parent is null) panel.Children.Add(displayList);
                var row = CreateSortableRow(field, check);
                sortableRows.Add(field, row);
                displayRows.Children.Add(row);
            }
        }
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || draggedDisplayRow is null) return;
            FinishDisplayDrag(commit: false);
            Mouse.Capture(null);
            e.Handled = true;
        };
        var showButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        showButtons.Children.Add(Button("显示全部", () => { foreach (var check in displayFields.Values) check.IsChecked = true; }, false));
        showButtons.Children.Add(Button("仅显示四项奖励", () =>
        {
            foreach (var entry in displayFields) entry.Value.IsChecked = (entry.Key & (OverlayField.RankPasses | OverlayField.RankDamage | OverlayField.EconPasses | OverlayField.EconDamage)) != 0;
        }, false));
        panel.Children.Add(showButtons);
        Note(panel, "“当前无法获取该地图信息”仍会显示。拖动模式下可右键悬浮窗打开操作菜单。");

        AddKzPage();

        panel = AddPage("快捷键", "\uE765");
        Label(panel, "全局快捷键");
        var tip = OverlayWindow.Text("点击输入框后，直接按下新的组合键。", 11, "#A1A1AA");
        tip.Margin = new Thickness(0, 0, 0, 9); panel.Children.Add(tip);
        string[] labels = { "显示 / 隐藏", "拖动 / 锁定", "打开设置", "提高推断关卡", "降低推断关卡" };
        for (int i = 0; i < shortcuts.Length; i++)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(OverlayWindow.Text(labels[i], 13, "#DEDEE4"));
            var box = new TextBox { IsReadOnly = true, ToolTip = labels[i] + "快捷键" };
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
        AddConfigurationPage();
        gsiStatus.Margin = new Thickness(11, 0, 0, 8);
        sidebarFooter.Children.Add(gsiStatus);
        AutomationProperties.SetLiveSetting(gsiStatus, AutomationLiveSetting.Polite);
        AddAboutPage();
        fonts.SelectionChanged += (_, _) => UpdatePreview();
        opacity.ValueChanged += (_, _) => UpdatePreview();
        scale.ValueChanged += (_, _) => UpdatePreview();
        rowSpacing.ValueChanged += (_, _) => UpdatePreview();
        columnSpacing.ValueChanged += (_, _) => UpdatePreview();
        foreach (var (field, check) in displayFields)
        {
            check.Checked += (_, _) => Changed();
            check.Unchecked += (_, _) => Changed();

            void Changed()
            {
                UpdatePreview();
                if (!loadingSettings && field is OverlayField.KzDifficulty or OverlayField.KzMode) preview.ShowKz();
            }
        }
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        var initial = currentSettings();
        LoadEditor(initial);
        SelectPage(initial.MapSource == MapSource.Broadcast && BroadcastReader.Read(initial.BroadcastPath).FileUnavailable ? 0 : 1);
    }

    private void AddKzPage()
    {
        kzPageIndex = pages.Count;
        var panel = AddPage("KZ 地图探测", "\uE81D");
        panel.Children.Add(CreateSwitchRow("启用 KZ 地图探测", kzEnabled, new Thickness(0, 0, 0, 16)));
        var label = Label(panel, "显示 Tier 的模式");
        AutomationProperties.SetLabeledBy(kzMode, label);
        kzMode.Items.Add(new ComboBoxItem { Content = "自动（读取控制台日志）", Tag = KzMode.Auto });
        kzMode.Items.Add(new ComboBoxItem { Content = "CKZ", Tag = KzMode.Ckz });
        kzMode.Items.Add(new ComboBoxItem { Content = "VNL", Tag = KzMode.Vnl });
        panel.Children.Add(kzMode);
        kzEnabled.Checked += (_, _) => kzMode.IsEnabled = true;
        kzEnabled.Unchecked += (_, _) => kzMode.IsEnabled = false;
        Note(panel, "地图名来自 CS2 GSI，难度来自 CS2KZ 官方 API。首次使用请在「数据与文件」配置 GSI，然后重启游戏。");
        Note(panel, "识别当前模式需要在 Steam 的 CS2 启动选项添加 -condebug 并重启。若右侧显示「待识别」，在游戏聊天中输入 !mode 可读取当前模式。");
        Note(panel, "NUB 允许使用传送，PRO 为无传送。更改设置后点击「保存设置」。");
    }

    internal void UpdateKzMap(string map, string status, KzMode? currentMode = null, KzMode tierMode = KzMode.Auto, KzMap? data = null) =>
        kzMapPanel.Update(map, status, currentMode, tierMode, data);

    private void AddConfigurationPage()
    {
        var panel = AddPage("配置管理", "\uE8B7");
        AddActionRow(panel, "导入配置", "从 JSON 文件载入配置并立即应用，覆盖现有设置。", Button("导入…", ImportSettings, false));
        AddActionRow(panel, "导出配置", "将当前已保存的设置备份为 JSON 文件。", Button("导出…", ExportSettings, false));
        Section(panel, "操作");
        var reset = Button("恢复为默认", RestoreDefaults, false);
        reset.Foreground = reset.BorderBrush = OverlayWindow.Brush("#FF5377");
        reset.Background = Brushes.Transparent;
        AddActionRow(panel, "恢复为默认", "将悬浮窗的全部设置恢复为默认配置。", reset);
    }

    private Border BuildComponentIntegration()
    {
        var panel = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
        DockPanel.SetDock(obsStatus, Dock.Right);
        obsStatus.Margin = new Thickness(8, 0, 0, 0);
        AutomationProperties.SetLiveSetting(obsStatus, AutomationLiveSetting.Polite);
        header.Children.Add(obsStatus);
        header.Children.Add(OverlayWindow.Text("组件集成", 15, "#F0F0F2", true));
        panel.Children.Add(header);

        obsEnabled.Style = (Style)FindResource("VisibilitySwitch");
        obsEnabled.Margin = new Thickness(-8, 0, -8, 5);
        panel.Children.Add(obsEnabled);

        Label(panel, "浏览器源 URL").Margin = new Thickness(0, 0, 0, 5);
        AutomationProperties.SetName(obsUrl, "浏览器源 URL");
        obsUrl.FontFamily = new FontFamily("Consolas");
        StyleTextBox(obsUrl);
        panel.Children.Add(obsUrl);
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 7) };
        actions.Children.Add(Button("复制 URL", () =>
        {
            try { Clipboard.SetText(obsUrl.Text); SetSuccess("已复制浏览器源 URL"); }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                obsUrl.Focus(); obsUrl.SelectAll(); SetError("剪贴板暂时不可用，请按 Ctrl+C 重试。");
            }
        }, true));
        obsOpen = Button("在浏览器预览", () => OpenLink(obsUrl.Text), false);
        UpdateObsStatus(false, "");
        actions.Children.Add(obsOpen);
        panel.Children.Add(actions);
        var instructions = OverlayWindow.Text("启用后保存设置，再将 URL 粘贴到 OBS 的「浏览器」源。", 11, "#A1A1AA");
        instructions.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(instructions);

        return new Border
        {
            Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(14, 11, 14, 12),
            Background = OverlayWindow.Brush("#19191B"), BorderBrush = OverlayWindow.Brush("#2B2B2E"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Child = panel
        };
    }

    internal void UpdateObsStatus(bool running, string detail)
    {
        obsStatus.Text = running ? "服务已启动" : detail.Length > 0 ? "服务启动失败" : "服务未启用";
        obsStatus.Foreground = OverlayWindow.Brush(running ? "#A4ED86" : detail.Length > 0 ? "#FFC2BC" : "#A1A1AA");
        obsStatus.ToolTip = detail.Length > 0 ? detail : null;
        AutomationProperties.SetHelpText(obsStatus, detail);
        obsOpen.IsEnabled = running;
    }

    private static void AddActionRow(Panel panel, string name, string description, Button button)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        button.Margin = new Thickness(14, 0, 0, 0);
        button.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(button, Dock.Right);
        row.Children.Add(button);
        var copy = new StackPanel();
        Label(copy, name).Margin = new Thickness(0, 0, 0, 4);
        var note = OverlayWindow.Text(description, 11, "#A1A1AA");
        note.TextWrapping = TextWrapping.Wrap;
        copy.Children.Add(note);
        row.Children.Add(copy);
        panel.Children.Add(row);
    }

    private Grid CreateSortableRow(OverlayField field, CheckBox check)
    {
        var row = new Grid { Tag = field, RenderTransform = new TranslateTransform() };
        var background = new Border { CornerRadius = new CornerRadius(7), IsHitTestVisible = false };
        var layout = new DockPanel();
        var grip = new TextBlock
        {
            Text = "⠿", Foreground = OverlayWindow.Brush("#A1A1AA"), Width = 26,
            Background = Brushes.Transparent, Cursor = Cursors.SizeNS, FontSize = 18,
            TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "拖动排序 · " + check.Content
        };
        double startY = 0;
        double grabOffset = 0;
        grip.PreviewMouseLeftButtonDown += (_, e) =>
        {
            startY = e.GetPosition(displayRows).Y;
            grabOffset = e.GetPosition(row).Y;
            e.Handled = grip.CaptureMouse();
        };
        grip.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!grip.IsMouseCaptured) return;
            var point = e.GetPosition(displayRows);
            if (draggedDisplayRow == row) MoveDisplayRow(row, point.Y - grabOffset);
            var hit = InputHitTest(e.GetPosition(this)) as DependencyObject;
            FinishDisplayDrag(new Rect(displayRows.RenderSize).Contains(point) &&
                hit is not null && (hit == displayRows || displayRows.IsAncestorOf(hit)));
            grip.ReleaseMouseCapture();
            e.Handled = true;
        };
        grip.LostMouseCapture += (_, _) => { if (draggedDisplayRow == row) FinishDisplayDrag(commit: false); };
        grip.MouseMove += (_, e) =>
        {
            if (!grip.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
            double y = e.GetPosition(displayRows).Y;
            if (draggedDisplayRow is null)
            {
                if (Math.Abs(y - startY) < SystemParameters.MinimumVerticalDragDistance) return;
                BeginDisplayDrag(row);
            }
            MoveDisplayRow(row, y - grabOffset);
        };
        DockPanel.SetDock(grip, Dock.Left);
        layout.Children.Add(grip);
        layout.Children.Add(check);
        row.Children.Add(background);
        row.Children.Add(layout);
        return row;
    }

    private void BeginDisplayDrag(Grid row)
    {
        draggedDisplayRow = row;
        Panel.SetZIndex(row, 1);
        ((Border)row.Children[0]).Background = OverlayWindow.Brush("#303945");
        displayDropSlot.Height = row.ActualHeight;
    }

    private void MoveDisplayRow(Grid row, double top)
    {
        var children = displayRows.Children;
        int from = children.IndexOf(row);
        double height = row.ActualHeight;
        double bottom = Math.Max(0, displayRows.ActualHeight - height);
        top = Math.Clamp(top, 0, bottom);
        SetDisplayRowOffset(row, top - VisualTreeHelper.GetOffset(row).Y, animate: false);

        int target = 0;
        double center = top + height / 2;
        if (top >= bottom) target = children.Count - 1;
        else if (top > 0)
        {
            for (int i = 0; i < children.Count; i++)
            {
                if (i == from) continue;
                var other = (Grid)children[i];
                if (center < VisualTreeHelper.GetOffset(other).Y + other.ActualHeight / 2) break;
                target++;
            }
        }
        if (target == displayDragTarget) return;
        displayDragTarget = target;

        for (int i = 0; i < children.Count; i++)
        {
            if (i == from) continue;
            double offset = from < i && i <= target ? -height : target <= i && i < from ? height : 0;
            SetDisplayRowOffset((Grid)children[i], offset, animate: true);
        }
        var targetRow = (Grid)children[target];
        double slotTop = VisualTreeHelper.GetOffset(targetRow).Y;
        if (target > from) slotTop += targetRow.ActualHeight - height;
        var slotOffset = (TranslateTransform)displayDropSlot.RenderTransform;
        AnimateValue(slotOffset, TranslateTransform.YProperty, slotTop, 150,
            displayDropSlot.Visibility == Visibility.Visible && SystemParameters.ClientAreaAnimation);
        displayDropSlot.Visibility = Visibility.Visible;
    }

    private static void SetDisplayRowOffset(Grid row, double offset, bool animate) =>
        AnimateValue(row.RenderTransform, TranslateTransform.YProperty, offset, 150,
            animate && SystemParameters.ClientAreaAnimation);

    private void FinishDisplayDrag(bool commit)
    {
        if (draggedDisplayRow is not { } row) return;
        int target = displayDragTarget;
        draggedDisplayRow = null;
        displayDragTarget = -1;
        displayDropSlot.Visibility = Visibility.Collapsed;
        ((Border)row.Children[0]).Background = null;
        Panel.SetZIndex(row, 0);

        var children = displayRows.Children;
        if (!commit || target < 0 || target == children.IndexOf(row))
        {
            foreach (Grid item in children) SetDisplayRowOffset(item, 0, animate: true);
            return;
        }

        var visiblePositions = children.Cast<Grid>().ToDictionary(item => item,
            item => VisualTreeHelper.GetOffset(item).Y + ((TranslateTransform)item.RenderTransform).Y);
        children.Remove(row);
        children.Insert(target, row);
        displayRows.UpdateLayout();
        foreach (Grid item in children)
        {
            SetDisplayRowOffset(item, visiblePositions[item] - VisualTreeHelper.GetOffset(item).Y, animate: false);
            SetDisplayRowOffset(item, 0, animate: true);
        }
        UpdatePreview();
    }

    internal bool SmokeTestDisplayReorder()
    {
        SelectPage(2);
        UpdateLayout();
        var children = displayRows.Children;
        var first = (Grid)children[0];
        var second = (Grid)children[1];
        var last = (Grid)children[children.Count - 1];
        if (first.ActualHeight <= 0) return false;

        BeginDisplayDrag(first);
        MoveDisplayRow(first, displayRows.ActualHeight);
        var shift = (TranslateTransform)second.RenderTransform;
        bool previewed = displayDropSlot.Visibility == Visibility.Visible && displayDragTarget == children.Count - 1 &&
            Equals(shift.GetAnimationBaseValue(TranslateTransform.YProperty), -first.ActualHeight) &&
            (!SystemParameters.ClientAreaAnimation || shift.HasAnimatedProperties);
        FinishDisplayDrag(commit: true);
        var changedOrder = ReadAppearance().DisplayOrder;
        BeginDisplayDrag(last);
        MoveDisplayRow(last, 0);
        bool movesUp = displayDragTarget == 0;
        FinishDisplayDrag(commit: false);
        return previewed && movesUp && changedOrder[0] == (OverlayField)second.Tag &&
            changedOrder[^1] == (OverlayField)first.Tag && changedOrder.SequenceEqual(ReadAppearance().DisplayOrder);
    }

    private void LoadEditor(Settings next)
    {
        loadingSettings = true;
        txtSource.IsChecked = next.MapSource == MapSource.Broadcast;
        gsiSource.IsChecked = next.MapSource == MapSource.Gsi;
        isSvip.IsChecked = next.IsSvip;
        useNativeDamage.IsChecked = next.UseNativeDamage;
        path.Text = next.BroadcastPath;
        pollSeconds.Text = (next.PollIntervalMilliseconds / 1000d).ToString("0.###", CultureInfo.CurrentCulture);
        rewardMinutes.Text = next.RewardRefreshMinutes.ToString(CultureInfo.CurrentCulture);
        autoUpdate.IsChecked = next.AutoUpdateDays > 0;
        autoUpdateDays.Text = (next.AutoUpdateDays > 0 ? next.AutoUpdateDays : Settings.DefaultAutoUpdateDays).ToString(CultureInfo.CurrentCulture);
        autoUpdateDays.IsEnabled = autoUpdate.IsChecked == true;
        obsEnabled.IsChecked = next.ObsEnabled;
        kzEnabled.IsChecked = next.KzEnabled;
        kzMode.SelectedValue = next.KzMode;
        kzMode.IsEnabled = next.KzEnabled;
        var hotkeys = next.Hotkeys;
        for (int i = 0; i < shortcuts.Length; i++) shortcuts[i].Text = hotkeys[i];
        opacity.Value = next.BackgroundOpacity * 100;
        scale.Value = next.Scale * 100;
        rowSpacing.Value = next.RowSpacing;
        columnSpacing.Value = next.ColumnSpacing;
        foreach (var (alignment, option) in alignmentOptions) option.IsChecked = alignment == next.ValueAlignment;
        fonts.SelectedValue = FontCatalog.Resolve(next.OverlayFontFamily).Source;
        displayRows.Children.Clear();
        foreach (var field in next.DisplayOrder) displayRows.Children.Add(sortableRows[field]);
        foreach (var (field, check) in displayFields) check.IsChecked = !next.Hides(field);
        styleDrafts.Clear();
        foreach (var (field, style) in next.FieldTextStyles) styleDrafts[field] = TextStyleDraft.FromStyle(style);
        styleDrafts[OverlayField.None] = TextStyleDraft.FromStyle(next.GlobalTextStyle);
        kzColorDrafts.Clear();
        foreach (var (token, color) in next.KzTextColors) kzColorDrafts[token] = color;
        kzColorTargets.SelectedItem ??= "T1";
        kzColorValue.Text = kzColorDrafts.GetValueOrDefault((string)kzColorTargets.SelectedItem, "");
        SelectTextTarget(textTarget);
        loadingSettings = false;
        OnMapSourceChanged();
    }

    private void AddAboutPage()
    {
        aboutPageIndex = pages.Count;
        var panel = AddPage("关于", "\uE946", sidebarFooter);
        var repository = ReleaseCheck.Repository;

        Label(panel, "ZE 奖励悬浮窗");
        Note(panel, "版本 v" + ReleaseCheck.LocalVersion);
        Note(panel, "作者 creeper_kk");
        panel.Children.Add(CreateSwitchRow("自动更新", autoUpdate, new Thickness(0, 0, 0, 8)));
        AddNumber(panel, "检查间隔", autoUpdateDays, $"天（1～{Settings.MaxAutoUpdateDays}）");
        autoUpdate.Checked += (_, _) => autoUpdateDays.IsEnabled = true;
        autoUpdate.Unchecked += (_, _) => autoUpdateDays.IsEnabled = false;
        Note(panel, "自动下载的版本会在下次启动时启用。关闭后永不自动检查或下载更新。");
        var check = Button("检查更新", null, true);
        panel.Children.Add(check);
        var updateStatus = OverlayWindow.Text("", 12, "#A1A1AA");
        updateStatus.TextWrapping = TextWrapping.Wrap;
        updateStatus.Margin = new Thickness(0, 10, 0, 12);
        AutomationProperties.SetLiveSetting(updateStatus, AutomationLiveSetting.Polite);
        panel.Children.Add(updateStatus);
        check.Click += async (_, _) =>
        {
            if (Updates is null)
            {
                updateStatus.Text = "当前运行模式暂不可用。";
                return;
            }
            check.IsEnabled = false;
            try
            {
                await Updates.CheckManuallyAsync((message, failed) =>
                {
                    updateStatus.Text = message;
                    updateStatus.Foreground = OverlayWindow.Brush(failed ? "#FFC2BC" : "#A1A1AA");
                });
            }
            finally { check.IsEnabled = true; }
        };
        Label(panel, "GitHub 仓库");
        var link = Button(repository, () => OpenLink(repository), false);
        link.Content = new TextBlock { Text = repository, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(link);
        var acknowledgements = Button("致谢与开源依赖", () => OpenLink(repository + "/blob/main/ACKNOWLEDGMENTS.md"), false);
        acknowledgements.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(acknowledgements);
        AddRecentVersions(panel);
    }

    private void AddRecentVersions(StackPanel panel)
    {
        var title = Label(panel, "最近版本");
        title.FontSize = 16;
        title.Margin = new Thickness(0, 16, 0, 8);
        (string Version, string Summary)[] releases =
        [
            ("1.3.0.1", "新增 KZ 地图探测、路线 Tier 和「非全球图」提示。悬浮窗和 OBS 随地图自动切换 ZE 奖励或 KZ 难度与模式，支持独立设置各个 Tier 和模式的文字颜色。"),
            ("1.2.5.3", "「关于」新增最近三个版本的更新摘要，并突出显示当前版本。"),
            ("1.2.5.2", "新增自动更新和发布说明。改进地图与关卡来源、文字样式和拖动排序，修复悬浮窗拖动时的位置偏移。")
        ];
        foreach (var (version, summary) in releases)
        {
            bool current = version == ReleaseCheck.LocalVersion;
            var details = new StackPanel();
            var heading = current ? $"v{version}　当前版本" : $"v{version}";
            details.Children.Add(OverlayWindow.Text(heading, 13, current ? "#F0F0F2" : "#A9B4C2", true));
            var description = OverlayWindow.Text(summary, 12, current ? "#CACAD0" : "#A1A1AA");
            description.TextWrapping = TextWrapping.Wrap;
            description.Margin = new Thickness(0, 3, 0, 0);
            details.Children.Add(description);
            panel.Children.Add(new Border
            {
                Background = current ? (Brush)FindResource("Surface") : Brushes.Transparent,
                CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(0, 0, 0, 4), Child = details
            });
        }
    }

    private void OpenLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Win32Exception ex) { SetError("无法打开链接：" + ex.Message); }
    }

    internal void UpdateGsiStatus(GsiListener.ConnectionState? state, int? roundDamage, string error, bool demo)
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
        string damageText = roundDamage is int damage ? damage.ToString("N0", CultureInfo.CurrentCulture)
            : state == GsiListener.ConnectionState.Receiving ? "当前推送未提供" : "等待 GSI 推送";
        roundDamageStatus.Text = "GSI 本回合伤害 · " + (demo ? "演示模式" : damageText);
    }

    private DockPanel CreateSwitchRow(string label, CheckBox toggle, Thickness margin)
    {
        var row = new DockPanel { Margin = margin };
        toggle.Style = (Style)FindResource("LabeledSwitch");
        toggle.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(toggle, label);
        toggle.Checked += (_, _) => UpdatePreview();
        toggle.Unchecked += (_, _) => UpdatePreview();
        DockPanel.SetDock(toggle, Dock.Right);
        row.Children.Add(toggle);
        row.Children.Add(OverlayWindow.Text(label, 13, "#E6E6EB", true));
        return row;
    }

    private RadioButton CreateSourceOption(string name) => new()
    {
        Content = name, GroupName = "MapSource", Style = (Style)FindResource("MapSourceOption")
    };

    internal void UpdateInferredStage(Broadcast broadcast, RewardData? data, int offset)
    {
        int? configuredStage = broadcast.GetSingleRewardStage(data);
        inferredStage.IsEnabled = data is not null && configuredStage is null && broadcast.CtScore.HasValue;
        if (configuredStage is int stage)
            inferredStage.Text = $"单关地图  ·  奖励配置为第 {stage} 关";
        else if (broadcast.IsGsiStageFallback && data is null)
            inferredStage.Text = "正在获取该地图奖励…";
        else if (inferredStage.IsEnabled)
            inferredStage.Text = $"CT 比分 {broadcast.CtScore}  ·  推断第 {broadcast.Stage} 关  ·  偏移 {offset:+0;-0;0}";
        else
            inferredStage.Text = broadcast.Stage.HasValue ? $"叠层文本已识别第 {broadcast.Stage} 关" : "等待比分推断关卡";
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
        textTargets.MinHeight = 34;
        textTargets.Items.Add(new ComboBoxItem { Tag = OverlayField.None, Content = GlobalTextLabel });
        foreach (var (field, name) in OverlayTypography.Labels)
            if (field is not (OverlayField.KzDifficulty or OverlayField.KzMode))
                textTargets.Items.Add(new ComboBoxItem { Tag = field, Content = name });
        AutomationProperties.SetName(textTargets, "应用到");
        textTargets.SelectionChanged += (_, _) =>
        {
            if (textTargets.SelectedValue is OverlayField field) SelectTextTarget(field);
        };
        AddTextStyleRow(panel, "应用到", textTargets);

        var decrease = Button("A−", () => AdjustSize(-1), false);
        var increase = Button("A＋", () => AdjustSize(1), false);
        decrease.Padding = increase.Padding = new Thickness(6, 4, 6, 4);
        AutomationProperties.SetName(decrease, "减小字号");
        AutomationProperties.SetName(increase, "增大字号");
        StyleTextBox(textSize);
        textSize.Width = 58;
        textSize.ToolTip = $"字号 {OverlayTextStyle.MinSize}～{OverlayTextStyle.MaxSize}";
        textSize.HorizontalContentAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetName(textSize, "字号");
        textSize.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Up or Key.Down)) return;
            AdjustSize(e.Key == Key.Up ? 1 : -1);
            e.Handled = true;
        };
        var sizeInput = new Grid { Margin = new Thickness(0, 0, 8, 0) };
        textSizePlaceholder.HorizontalAlignment = HorizontalAlignment.Center;
        textSizePlaceholder.IsHitTestVisible = false;
        sizeInput.Children.Add(textSize);
        sizeInput.Children.Add(textSizePlaceholder);
        resetTextSize = Button(UseGlobalTextLabel, () => textSize.Text = "", false);
        resetTextSize.Margin = new Thickness(0);
        resetTextSize.Padding = new Thickness(8, 6, 8, 6);
        AddTextStyleRow(panel, "字号", decrease, sizeInput, increase, resetTextSize);

        StyleTextBox(textColor);
        textColor.Width = 80;
        textColor.ToolTip = "#RRGGBB";
        AutomationProperties.SetName(textColor, "自定义十六进制颜色");
        var colorInput = new Grid { Margin = new Thickness(0, 0, 8, 0) };
        var colorPlaceholder = OverlayWindow.Text("#RRGGBB", 11, "#A1A1AA");
        colorPlaceholder.HorizontalAlignment = HorizontalAlignment.Center;
        colorPlaceholder.IsHitTestVisible = false;
        colorInput.Children.Add(textColor);
        colorInput.Children.Add(colorPlaceholder);
        resetTextColor = Button(UseGlobalTextLabel, () => textColor.Text = "", false);
        resetTextColor.Margin = new Thickness(0);
        resetTextColor.Padding = resetTextSize.Padding;
        var pickColor = Button("选色…", PickTextColor, false);
        pickColor.Padding = resetTextColor.Padding;
        AddTextStyleRow(panel, "文字颜色", colorInput, pickColor, resetTextColor);
        panel.Children.Add(BuildColorPalette(textColor, swatches, "TextColor"));

        textWeight.MinHeight = 34;
        AutomationProperties.SetName(textWeight, "字重");
        foreach (var (value, name) in OverlayTextStyle.Weights)
        {
            textWeight.Items.Add(new ComboBoxItem
            {
                Content = name, FontWeight = FontWeight.FromOpenTypeWeight(value ?? 400)
            });
        }
        AddTextStyleRow(panel, "字重", textWeight);
        var resetStyle = Button("清除所选项样式", () => LoadTextStyle(TextStyleDraft.Empty), false);
        resetStyle.HorizontalAlignment = HorizontalAlignment.Right;
        resetStyle.Margin = new Thickness(0);
        panel.Children.Add(resetStyle);

        textSize.TextChanged += (_, _) => StoreTextDraft();
        textColor.TextChanged += (_, _) =>
        {
            colorPlaceholder.Visibility = string.IsNullOrWhiteSpace(textColor.Text) ? Visibility.Visible : Visibility.Collapsed;
            StoreTextDraft();
        };
        textWeight.SelectionChanged += (_, _) => StoreTextDraft();
    }

    private void BuildKzColorEditor(StackPanel panel)
    {
        Section(panel, "KZ 文字颜色");
        kzColorTargets.ItemsSource = KzTypography.ColorTargets;
        AutomationProperties.SetName(kzColorTargets, "要设置颜色的 KZ 难度或模式");
        AddTextStyleRow(panel, "应用到", kzColorTargets);

        StyleTextBox(kzColorValue);
        kzColorValue.ToolTip = "#RRGGBB";
        AutomationProperties.SetName(kzColorValue, "KZ 自定义十六进制颜色");
        var input = new Grid { Margin = new Thickness(0, 0, 8, 0) };
        var placeholder = OverlayWindow.Text("#RRGGBB", 11, "#A1A1AA");
        placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        placeholder.IsHitTestVisible = false;
        input.Children.Add(kzColorValue);
        input.Children.Add(placeholder);
        var pick = Button("选色…", () =>
        {
            if (kzColorTargets.SelectedItem is string token)
                PickTextColor(kzColorValue, KzTypography.DefaultColor(token));
        }, false);
        var reset = Button("使用默认样式", () => kzColorValue.Text = "", false);
        reset.Margin = new Thickness(0);
        AddTextStyleRow(panel, "文字颜色", input, pick, reset);
        panel.Children.Add(BuildColorPalette(kzColorValue, kzSwatches, "KzTextColor"));

        kzColorTargets.SelectionChanged += (_, _) =>
        {
            if (kzColorTargets.SelectedItem is not string token) return;
            kzColorValue.Text = kzColorDrafts.GetValueOrDefault(token, "");
            if (!loadingSettings) preview.ShowKz(token);
        };
        kzColorValue.TextChanged += (_, _) =>
        {
            if (kzColorTargets.SelectedItem is not string token) return;
            var color = kzColorValue.Text.Trim().ToUpperInvariant();
            kzColorDrafts[token] = color;
            placeholder.Visibility = color.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (hex, swatch) in kzSwatches) swatch.IsChecked = hex == color;
            if (loadingSettings) return;
            UpdatePreview();
            preview.ShowKz(token);
        };
    }

    private WrapPanel BuildColorPalette(TextBox input, Dictionary<string, RadioButton> buttons, string group)
    {
        var palette = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
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
                Style = (Style)FindResource("Swatch"), GroupName = group, Background = OverlayWindow.Brush(color),
                Foreground = darkMark ? Brushes.Black : Brushes.White, ToolTip = name + " " + color,
                Margin = new Thickness(0, 0, 5, 6)
            };
            AutomationProperties.SetName(swatch, name + " " + color);
            swatch.Click += (_, _) => input.Text = color;
            buttons.Add(color, swatch);
            palette.Children.Add(swatch);
        }
        return palette;
    }

    private static void AddTextStyleRow(Panel panel, string label, params UIElement[] controls)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        foreach (var control in controls) actions.Children.Add(control);
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions);
        var title = OverlayWindow.Text(label, 13, "#E6E6EB", true);
        title.TextWrapping = TextWrapping.Wrap;
        row.Children.Add(title);
        panel.Children.Add(row);
    }

    private void SelectTextTarget(OverlayField field)
    {
        if (!Equals(textTargets.SelectedValue, field))
        {
            textTargets.SelectedValue = field;
            return;
        }
        textTarget = field;
        LoadTextStyle(styleDrafts.GetValueOrDefault(field, TextStyleDraft.Empty));
    }

    private void LoadTextStyle(TextStyleDraft draft)
    {
        loadingTextStyle = true;
        string fallback = textTarget == OverlayField.None ? UseDefaultTextLabel : UseGlobalTextLabel;
        resetTextSize.Content = resetTextColor.Content = fallback;
        AutomationProperties.SetName(resetTextSize, "字号 · " + fallback);
        AutomationProperties.SetName(resetTextColor, "文字颜色 · " + fallback);
        textWeight.SelectedIndex = -1;
        ((ComboBoxItem)textWeight.Items[0]).Content = fallback;
        textWeight.SelectedIndex = draft.WeightIndex;
        textSize.Text = draft.Size;
        textColor.Text = draft.Color;
        loadingTextStyle = false;
        StoreTextDraft();
    }

    private void StoreTextDraft()
    {
        if (loadingTextStyle) return;
        var draft = new TextStyleDraft(textSize.Text, textColor.Text, Math.Max(0, textWeight.SelectedIndex));
        styleDrafts[textTarget] = draft;
        textSizePlaceholder.Visibility = string.IsNullOrWhiteSpace(draft.Size) ? Visibility.Visible : Visibility.Collapsed;
        textSizePlaceholder.Text = textTarget == OverlayField.None ? "默认" : EffectiveTextStyle().Size?.ToString(CultureInfo.CurrentCulture);
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
        if (loadingSettings) return;
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

    private void PickTextColor() => PickTextColor(textColor, EffectiveTextStyle().Color!);

    private void PickTextColor(TextBox input, string fallback)
    {
        string original = input.Text;
        var initial = (Color)ColorConverter.ConvertFromString(OverlayTextStyle.IsValidColor(original.Trim()) ? original.Trim() : fallback);
        var picker = new TextColorPicker(initial, hex => input.Text = hex, (Style)FindResource("SettingsButton"))
        {
            Owner = this, FontFamily = FontFamily
        };
        // Keep the overlay preview to the right of the picker.
        var origin = editor.TranslatePoint(new Point(), this);
        picker.Left = Left + origin.X + Math.Max(0, (editor.ActualWidth - picker.Width) / 2);
        picker.Top = Top + origin.Y + Math.Max(0, (editor.ActualHeight - picker.Height) / 2);
        input.Text = picker.ShowDialog() == true ? picker.SelectedHex : original;
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
                string target = field == OverlayField.None ? GlobalTextLabel : OverlayTypography.Labels[field];
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
        var scroll = new SmoothScrollViewer
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
        option.Style = (Style)FindResource("NavigationOption");
        option.HorizontalContentAlignment = HorizontalAlignment.Left;
        option.MinHeight = 42;
        option.Margin = new Thickness(0, 0, 0, 5);
        AutomationProperties.SetName(option, label);
        int index = pages.Count;
        option.Click += (_, _) => SelectPage(index);
        option.MouseEnter += (_, _) => MoveNavigationHover(option, animate: true);
        option.MouseLeave += (_, _) =>
            AnimateValue(navigationHover, OpacityProperty, 0, 140, SystemParameters.ClientAreaAnimation);
        (navigationHost ?? navigation).Children.Add(option);
        pages.Add((option, scroll));
        return panel;
    }

    private void MoveNavigationHover(RadioButton button, bool animate)
    {
        navigationHover.Height = button.ActualHeight;
        double top = button.TranslatePoint(new Point(), sidebarBody).Y;
        animate = animate && SystemParameters.ClientAreaAnimation;
        AnimateValue(navigationHoverOffset, TranslateTransform.YProperty, top, 170,
            animate && navigationHover.Opacity > 0.01);
        AnimateValue(navigationHover, OpacityProperty, 1, 110, animate);
    }

    private static void AnimateValue(DependencyObject target, DependencyProperty property, double to, int milliseconds, bool animate)
    {
        var from = (double)target.GetValue(property);
        var animation = animate && Math.Abs(from - to) > 0.001
            ? new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
            }
            : null;
        target.SetValue(property, to);
        if (target is UIElement element) element.BeginAnimation(property, animation);
        else ((TranslateTransform)target).BeginAnimation(property, animation);
    }

    private void SelectPage(int index)
    {
        var (button, page) = pages[index];
        button.IsChecked = true;
        pageHost.Content = page;
        bool about = button.Parent == sidebarFooter;
        bool kz = index == kzPageIndex;
        rightPane.Visibility = about ? Visibility.Collapsed : Visibility.Visible;
        preview.Visibility = integration.Visibility = kz ? Visibility.Collapsed : Visibility.Visible;
        kzMapPanel.Visibility = kz ? Visibility.Visible : Visibility.Collapsed;
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
        if (loadingSettings) return;
        if (UsesGsi && string.IsNullOrWhiteSpace(path.Text)) { pathStatus.Text = "未指定文本，关卡由奖励配置或 CT 比分确定。"; return; }
        if (!BroadcastFiles.TryNormalize(path.Text, out var normalized)) { pathStatus.Text = "请填写叠层文本的完整路径，或点击“选择文件”。"; return; }
        var broadcast = BroadcastReader.Read(normalized);
        pathStatus.Text = broadcast.FileUnavailable ? broadcast.Error + "。" : "叠层文本可读取。" + broadcast.Error;
    }

    private bool UsesGsi => gsiSource.IsChecked == true;

    private void OnMapSourceChanged()
    {
        if (loadingSettings) return;
        bool usesGsi = UsesGsi;
        mapSourceHint.Text = usesGsi
            ? "地图由 GSI 读取。关卡优先使用匹配的叠层文本，其次使用单关奖励配置或 CT 比分。"
            : "地图和关卡均从直播助手叠层文本读取。";
        gsiConfigLabel.Text = usesGsi ? "CS2 GSI 配置" : "CS2 GSI 配置（用于击杀估算与 KZ 地图探测）";
        broadcastPathLabel.Text = usesGsi ? "叠层文本路径（可选，用于读取关卡）" : "叠层文本路径";
        gsiStageControls.Visibility = usesGsi ? Visibility.Visible : Visibility.Collapsed;
        if (!usesGsi && string.IsNullOrWhiteSpace(path.Text)) path.Text = BroadcastFiles.DefaultPath;
        UpdatePathStatus();
        UpdatePreview();
    }

    private const string ConfigurationFilter = "悬浮窗配置 (*.json)|*.json";

    private void ImportSettings()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入悬浮窗配置", Filter = ConfigurationFilter, CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ApplyConfiguration(Settings.Import(dialog.FileName), "配置已导入并应用");
        }
        catch (JsonException)
        {
            SetError("导入失败。请选择有效的悬浮窗 JSON 配置文件，并检查配置中的叠层文本路径。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError("配置读取失败：" + ex.Message);
        }
    }

    private void ExportSettings()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出悬浮窗配置", FileName = "FysZeRewardOverlay.settings.json",
            Filter = ConfigurationFilter, DefaultExt = ".json", OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            currentSettings().Export(dialog.FileName);
            SetSuccess("配置已导出");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError("配置导出失败：" + ex.Message);
        }
    }

    private void RestoreDefaults()
    {
        var choice = MessageBox.Show(this, "恢复为默认会覆盖已保存的悬浮窗设置，并丢弃未保存的修改。确定继续吗？",
            "恢复为默认", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (choice == MessageBoxResult.Yes) ApplyConfiguration(Settings.CreateDefault(), "已恢复为默认配置");
    }

    private void ApplyConfiguration(Settings next, string success)
    {
        var message = apply(next, false);
        if (message is not null) { SetError(message); return; }
        LoadEditor(next);
        SetSuccess(success);
    }

    private static async Task ConfigureGsiAutomatically(Button button, TextBlock status)
    {
        button.IsEnabled = false;
        status.Visibility = Visibility.Visible;
        status.Foreground = OverlayWindow.Brush("#A1A1AA");
        status.Text = "正在查找 CS2 配置文件夹…";
        try
        {
            string? directory = await Task.Run(() =>
            {
                var config = SteamCs2.FindConfigDirectory();
                if (config is not null)
                    AtomicFile.Write(Path.Combine(config, GsiListener.ConfigFileName), GsiListener.Config);
                return config;
            });
            if (directory is null)
            {
                status.Foreground = OverlayWindow.Brush("#FFC2BC");
                status.Text = "未找到 CS2 的 cfg 文件夹。请确认 Steam 已完成安装，或使用手动配置。";
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
                status.Foreground = OverlayWindow.Brush("#A4ED86");
                status.Text = "GSI 配置已保存，已打开 cfg 文件夹。请重启 CS2。";
            }
            catch (Win32Exception)
            {
                status.Text = $"GSI 配置已保存，请重启 CS2。请在资源管理器中打开 {directory}。";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            status.Foreground = OverlayWindow.Brush("#FFC2BC");
            status.Text = "GSI 自动配置失败：" + ex.Message;
        }
        finally { button.IsEnabled = true; }
    }

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
    private Button Button(string text, Action? action, bool primary)
    {
        var button = new Button
        {
            Content = text, Style = (Style)FindResource("SettingsButton"),
            Margin = new Thickness(0, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Left
        };
        if (primary) button.Background = OverlayWindow.Brush("#006BD0");
        if (action is not null) button.Click += (_, _) => action();
        return button;
    }

    private static void AddSlider(Panel panel, string label, Slider slider, string description, string unit = "%")
    {
        var header = new DockPanel { Margin = new Thickness(0, 9, 0, 6) };
        var amount = OverlayWindow.Text("", 13, "#E6E6EB", true);
        DockPanel.SetDock(amount, Dock.Right); header.Children.Add(amount);
        header.Children.Add(OverlayWindow.Text(label, 13, "#DEDEE4")); panel.Children.Add(header);
        slider.ValueChanged += (_, _) => amount.Text = Math.Round(slider.Value) + unit;
        amount.Text = Math.Round(slider.Value) + unit;
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
        int updateDays = 0;
        if (autoUpdate.IsChecked == true && (!int.TryParse(autoUpdateDays.Text, out updateDays) || updateDays is < 1 or > Settings.MaxAutoUpdateDays))
        {
            SelectPage(aboutPageIndex);
            SetError($"自动更新检查间隔请输入 1～{Settings.MaxAutoUpdateDays} 的整数天数。");
            autoUpdateDays.Focus();
            return;
        }
        var next = ReadAppearance();
        foreach (var (token, color) in kzColorDrafts)
        {
            if (color.Length == 0 || OverlayTextStyle.IsValidColor(color)) continue;
            SelectPage(1);
            kzColorTargets.SelectedItem = token;
            kzColorValue.BringIntoView();
            SetError($"{token} 的颜色请输入 #RRGGBB 或使用选色按钮。");
            return;
        }
        if (!TrySaveTextStyles(next)) return;
        next.BroadcastPath = selectedPath;
        next.PollIntervalMilliseconds = milliseconds;
        next.RewardRefreshMinutes = minutes;
        next.AutoUpdateDays = updateDays;
        next.ObsEnabled = obsEnabled.IsChecked == true;
        next.KzEnabled = kzEnabled.IsChecked == true;
        next.KzMode = kzMode.SelectedValue is KzMode mode ? mode : KzMode.Auto;
        var message = apply(next, true);
        if (message is not null) SetError(message);
        else SetSuccess("设置已保存");
    }
    private Settings ReadAppearance()
    {
        var draft = currentSettings().Copy();
        draft.MapSource = UsesGsi ? MapSource.Gsi : MapSource.Broadcast;
        draft.IsSvip = isSvip.IsChecked == true;
        draft.UseNativeDamage = useNativeDamage.IsChecked == true;
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
        draft.DisplayOrder = displayRows.Children.Cast<Grid>().Select(row => (OverlayField)row.Tag).ToArray();
        draft.HiddenFields = displayFields.Where(entry => entry.Value.IsChecked != true)
            .Aggregate(OverlayField.None, (fields, entry) => fields | entry.Key);
        draft.KzTextColors = kzColorDrafts.Where(entry => OverlayTextStyle.IsValidColor(entry.Value))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        return draft;
    }

    public void SetError(string value)
    {
        saved.Text = "";
        error.Text = value;
    }
    private void SetSuccess(string value)
    {
        error.Text = "";
        saved.Text = value;
    }
    internal int PageCount => pages.Count;
    internal void SelectPreviewTab(int index) => SelectPage(index);
}

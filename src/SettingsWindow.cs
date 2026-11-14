using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FysZeRewardOverlay;

public sealed class SettingsWindow : Window
{
    private readonly Settings initial;
    private readonly TextBox path = new();
    private readonly TextBlock pathStatus = OverlayWindow.Text("", 11, "#9DADBD");
    private readonly TextBox pollSeconds = new();
    private readonly TextBox rewardMinutes = new();
    private readonly ComboBox fonts = new();
    private readonly TextBlock fontPreview = OverlayWindow.Text("字体预览：通关云点 18 · 20,000 : 1", 18, "#D5EDFF");
    private readonly Dictionary<OverlayField, CheckBox> hiddenFields = new();
    private readonly TabControl tabs = new();
    private readonly TextBox[] shortcuts = new TextBox[3];
    private readonly Slider opacity = new() { Minimum = 0, Maximum = 95, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly Slider scale = new() { Minimum = 70, Maximum = 180, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly TextBlock error = OverlayWindow.Text("", 12, "#F1B7A5");
    private readonly Func<Settings, string?> apply;

    public SettingsWindow(Settings initial, string directory, Func<Settings, string?> apply, Action refresh, Action reset)
    {
        this.initial = initial;
        this.apply = apply;
        Title = "ZE 奖励悬浮窗 · 设置";
        Icon = BitmapFrame.Create(new Uri(new Uri("pack://application:,,,/"), Program.AppIconUri));
        Width = 640; Height = 740; MinWidth = 540; MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = OverlayWindow.Brush("#111A24");
        Foreground = OverlayWindow.Brush("#E7EEF5");
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        var root = new DockPanel { Margin = new Thickness(26, 20, 26, 20), Background = Background };
        Content = root;
        var bottom = new StackPanel();
        error.TextWrapping = TextWrapping.Wrap;
        error.Margin = new Thickness(0, 4, 0, 12);
        bottom.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Button("取消", () => Close(), false));
        buttons.Children.Add(Button("保存设置", Save, true));
        bottom.Children.Add(buttons);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        var header = new StackPanel();
        header.Children.Add(OverlayWindow.Text("ZE 奖励悬浮窗", 24, "#ECF4FD", true));
        var subtitle = OverlayWindow.Text("随地图和关卡更新，锁定后鼠标穿透。", 12, "#9DADBD");
        subtitle.Margin = new Thickness(0, 5, 0, 19); header.Children.Add(subtitle);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        tabs.Background = Background; tabs.BorderBrush = OverlayWindow.Brush("#33485E");
        root.Children.Add(tabs);
        var panel = AddTab("数据与文件");
        Label(panel, "实时文件");
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
        panel.Children.Add(pathStatus); UpdatePathStatus();
        Label(panel, "数据更新频率");
        pollSeconds.Text = (initial.PollIntervalMilliseconds / 1000d).ToString("0.###", CultureInfo.CurrentCulture);
        rewardMinutes.Text = initial.RewardRefreshMinutes.ToString();
        AddNumber(panel, "地图 / 关卡读取", pollSeconds, "秒（0.2～60）");
        AddNumber(panel, "奖励联网更新", rewardMinutes, "分钟（1～1440）");
        Note(panel, "读取间隔越短，切图和关卡变化显示越快。奖励更新间隔控制联网查询频率；断网后每 30 秒重试。");
        panel.Children.Add(Button("立即更新奖励", () => { refresh(); SetError("已请求更新奖励。"); }, false));
        Note(panel, "找不到文件时，可点击“选择文件”添加，或手动填写路径后保存。文件尚未生成也可保存路径，出现后会自动读取。");
        Note(panel, "FYS 助手：直播中心 → 直播助手 → 显示内容，需勾选“地图”和“关卡”。");

        panel = AddTab("外观与字体");
        Label(panel, "悬浮窗字体");
        fonts.ItemsSource = FontCatalog.Installed; fonts.DisplayMemberPath = nameof(FontOption.DisplayName);
        fonts.SelectedValuePath = nameof(FontOption.Name); fonts.SelectedValue = FontCatalog.Resolve(initial.OverlayFontFamily).Source;
        fonts.Padding = new Thickness(8, 6, 8, 6); fonts.FontSize = 14;
        fonts.Foreground = Brushes.Black;
        fonts.MaxDropDownHeight = 300; fonts.IsTextSearchEnabled = true;
        fonts.SelectionChanged += (_, _) => fontPreview.FontFamily = (fonts.SelectedItem as FontOption)?.Family ?? FontCatalog.Resolve(initial.OverlayFontFamily);
        panel.Children.Add(fonts);
        fontPreview.FontFamily = FontCatalog.Resolve(initial.OverlayFontFamily);
        fontPreview.TextWrapping = TextWrapping.Wrap; fontPreview.Margin = new Thickness(0, 12, 0, 14);
        panel.Children.Add(fontPreview);
        Note(panel, "可选择本机已安装的字体，应用到悬浮窗全部文字和奖励数字。部分字体没有中文字形时，系统会自动补用中文字体。");
        AddSlider(panel, "背景不透明度", opacity, initial.BackgroundOpacity * 100, "0% 为全透明，文字保持清晰");
        AddSlider(panel, "显示大小", scale, initial.Scale * 100, "调整整体字号与悬浮窗尺寸");
        panel.Children.Add(Button("恢复悬浮窗位置", reset, false));
        Note(panel, "游戏请使用窗口化或无边框窗口模式，以便桌面悬浮窗正常显示。");

        panel = AddTab("隐藏信息");
        Label(panel, "勾选需要隐藏的信息");
        Note(panel, "勾选 = 隐藏；取消勾选 = 显示。保存后立即生效，隐藏行不会留下空白。");
        var fieldsGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        panel.Children.Add(fieldsGrid);
        foreach (var (field, label) in OverlayOptions.Fields)
        {
            var check = new CheckBox { Content = label, IsChecked = initial.Hides(field), Foreground = Foreground, Margin = new Thickness(0, 0, 0, 12), FontSize = 13 };
            hiddenFields.Add(field, check); fieldsGrid.Children.Add(check);
        }
        var showButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        showButtons.Children.Add(Button("显示全部", () => { foreach (var check in hiddenFields.Values) check.IsChecked = false; }, false));
        showButtons.Children.Add(Button("仅显示四项奖励", () =>
        {
            foreach (var entry in hiddenFields) entry.Value.IsChecked = (entry.Key & (OverlayField.RankPasses | OverlayField.RankDamage | OverlayField.EconPasses | OverlayField.EconDamage)) == 0;
        }, false));
        panel.Children.Add(showButtons);
        Note(panel, "“当前无法获取该地图信息”仍会显示。拖动模式临时显示操作标题和提示，方便重新调整。");

        panel = AddTab("快捷键");
        Label(panel, "全局快捷键");
        var tip = OverlayWindow.Text("点击输入框后，直接按下新的组合键。", 11, "#9DADBD");
        tip.Margin = new Thickness(0, 0, 0, 9); panel.Children.Add(tip);
        string[] labels = { "显示 / 隐藏", "拖动 / 锁定", "打开设置" };
        var current = initial.Hotkeys;
        for (int i = 0; i < 3; i++)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(OverlayWindow.Text(labels[i], 13, "#D7E1ED"));
            var box = new TextBox { Text = current[i], IsReadOnly = true, ToolTip = labels[i] + "快捷键" };
            StyleTextBox(box);
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Tab) return;
                e.Handled = true;
                string? value = Shortcut.FromEvent(e);
                if (value is not null) { box.Text = value; error.Text = ""; }
            };
            shortcuts[i] = box;
            Grid.SetColumn(box, 1); row.Children.Add(box); panel.Children.Add(row);
        }
        var shortcutNote = OverlayWindow.Text("支持 Ctrl / Alt / Shift + 字母、数字或功能键；占用时会提示。", 10, "#91A2B4");
        shortcutNote.TextWrapping = TextWrapping.Wrap;
        shortcutNote.Margin = new Thickness(0, 1, 0, 15); panel.Children.Add(shortcutNote);
        Note(panel, "快捷键对整个桌面生效。系统托盘始终提供显示、拖动、设置及选择文件的入口。");
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private StackPanel AddTab(string label)
    {
        var panel = new StackPanel { Margin = new Thickness(15, 18, 15, 10) };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
        tabs.Items.Add(new TabItem { Header = label, Content = scroll, Padding = new Thickness(12, 7, 12, 7), Foreground = Brushes.Black });
        return panel;
    }
    private static void Note(Panel panel, string text)
    {
        var note = OverlayWindow.Text(text, 11, "#91A2B4");
        note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(0, 9, 0, 15); panel.Children.Add(note);
    }
    private static void AddNumber(Panel panel, string label, TextBox input, string unit)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(142) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(OverlayWindow.Text(label, 12, "#D7E1ED"));
        StyleTextBox(input); input.ToolTip = label; Grid.SetColumn(input, 1); row.Children.Add(input);
        var suffix = OverlayWindow.Text(unit, 11, "#9DADBD"); suffix.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(suffix, 2); row.Children.Add(suffix); panel.Children.Add(row);
    }
    private void UpdatePathStatus()
    {
        if (!BroadcastFiles.TryNormalize(path.Text, out var normalized)) { pathStatus.Text = "请手动填写完整文件路径，或点击“选择文件”。"; return; }
        var broadcast = BroadcastReader.Read(normalized);
        pathStatus.Text = broadcast.FileUnavailable ? broadcast.Error + "；请手动选择文件，或保存路径等待文件生成。" : "文件可读取；" + (broadcast.Error.Length > 0 ? broadcast.Error : "保存后将跟随此文件更新。");
    }

    private static void Label(Panel panel, string text)
    {
        var label = OverlayWindow.Text(text, 13, "#B3EC96", true);
        label.Margin = new Thickness(0, 0, 0, 8); panel.Children.Add(label);
    }
    private static void StyleTextBox(TextBox box)
    {
        box.Padding = new Thickness(9, 7, 9, 7); box.Background = OverlayWindow.Brush("#1C2A38");
        box.Foreground = OverlayWindow.Brush("#EFF5FD"); box.BorderBrush = OverlayWindow.Brush("#3E5369");
        box.CaretBrush = Brushes.White; box.FontSize = 12;
    }
    private static Button Button(string text, Action action, bool primary)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 10, 0), Background = OverlayWindow.Brush(primary ? "#A4E482" : "#273A4C"), Foreground = OverlayWindow.Brush(primary ? "#142215" : "#EDF5FC"), BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
        button.Click += (_, _) => action(); return button;
    }
    private static void AddSlider(Panel panel, string label, Slider slider, double value, string description)
    {
        var header = new DockPanel { Margin = new Thickness(0, 9, 0, 6) };
        var percent = OverlayWindow.Text("", 13, "#B3EC96", true);
        DockPanel.SetDock(percent, Dock.Right); header.Children.Add(percent);
        header.Children.Add(OverlayWindow.Text(label, 13, "#D7E1ED")); panel.Children.Add(header);
        slider.ValueChanged += (_, _) => percent.Text = Math.Round(slider.Value) + "%";
        slider.Value = value; percent.Text = Math.Round(value) + "%";
        slider.ToolTip = label; panel.Children.Add(slider);
        var note = OverlayWindow.Text(description, 10, "#91A2B4");
        note.Margin = new Thickness(0, 5, 0, 7); panel.Children.Add(note);
    }
    private void Save()
    {
        if (!BroadcastFiles.TryNormalize(path.Text, out var selectedPath)) { tabs.SelectedIndex = 0; SetError("请选择实时文件的完整绝对路径。"); return; }
        if (!OverlayOptions.TryIntervals(pollSeconds.Text, rewardMinutes.Text, out int milliseconds, out int minutes, out var validation))
        { tabs.SelectedIndex = 0; SetError(validation); return; }
        var next = initial.Copy();
        next.BroadcastPath = selectedPath;
        next.ToggleHotkey = shortcuts[0].Text; next.MoveHotkey = shortcuts[1].Text; next.SettingsHotkey = shortcuts[2].Text;
        next.BackgroundOpacity = opacity.Value / 100; next.Scale = scale.Value / 100;
        next.PollIntervalMilliseconds = milliseconds; next.RewardRefreshMinutes = minutes;
        next.OverlayFontFamily = (fonts.SelectedItem as FontOption)?.Name ?? FontCatalog.DefaultName;
        next.HiddenFields = hiddenFields.Where(e => e.Value.IsChecked == true).Aggregate(OverlayField.None, (fields, e) => fields | e.Key);
        var message = apply(next);
        if (message is not null) SetError(message); else Close();
    }
    public void SetError(string value) => error.Text = value;
    internal void SelectPreviewTab(int index) => tabs.SelectedIndex = index;
}

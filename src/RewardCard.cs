using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using static FysZeRewardOverlay.OverlayWindow;

namespace FysZeRewardOverlay;

internal sealed class RewardCard
{
    private readonly Border card = new();
    private readonly StackPanel content = new();
    private readonly TextBlock heading = OverlayTypography.CreateText(OverlayField.Heading, "ZE  /  通关奖励");
    private readonly TextBlock title = OverlayTypography.CreateText(OverlayField.MapTitle, "等待地图信息");
    private readonly TextBlock map = OverlayTypography.CreateText(OverlayField.MapName, "");
    private readonly TextBlock stage = OverlayTypography.CreateText(OverlayField.Stage, "");
    private readonly TextBlock unavailable = OverlayTypography.CreateText(OverlayField.Unavailable, DisplayInfo.UnavailableText);
    private readonly TextBlock reason = OverlayTypography.CreateText(OverlayField.ErrorDetail, "");
    private readonly TextBlock source = OverlayTypography.CreateText(OverlayField.Source, "");
    private readonly TextBlock hint = OverlayTypography.CreateText(OverlayField.HotkeyHint, "");
    private readonly List<(OverlayField Field, TextBlock Text, OverlayTextStyle Default)> styledText = new();
    private static readonly OverlayField[] RewardFields = { OverlayField.RankPasses, OverlayField.RankDamage, OverlayField.EconPasses, OverlayField.EconDamage };
    private const OverlayField RewardFieldMask = OverlayField.RankPasses | OverlayField.RankDamage | OverlayField.EconPasses | OverlayField.EconDamage;
    private static readonly string[] RewardLabels = { "通关云点", "云点转化比", "通关龙晶", "龙晶转化比" };
    private static readonly OverlayTextStyle RewardLabelStyle = new(32, "#AEAEAE", 400);
    private readonly TextBlock[] values = new TextBlock[4];
    private readonly Grid[] fieldRows = new Grid[4];
    private readonly Grid rankProgress = CreateProgressBar();
    private readonly Grid econProgress = CreateProgressBar();
    private DamageProgress? lastRankProgress;
    private DamageProgress? lastEconProgress;
    private readonly DockPanel top = new() { Margin = new Thickness(0, 0, 0, 12) };
    private readonly StackPanel footer = new();
    private Button? selectFileButton;
    private readonly StackPanel rewardRows = new();
    private readonly StackPanel unavailablePanel = new();
    private readonly StackPanel editButtons = new() { Orientation = Orientation.Horizontal };
    private Settings settings = new();
    private DisplayInfo? lastDisplay;
    private bool inputUnavailable;
    private bool demo;

    internal FrameworkElement Element => card;
    internal static DisplayInfo Sample { get; } = new(true, "ze_ffxii_westersand_v7_t", "最终幻想 XII · 西沙", 4,
        new(18, 20000, 12, 24000), "演示数据 · 非当前游戏", "伤害转化比 = 伤害 : 1 点货币") { Kills = 20 };

    internal RewardCard(Action openSettings, Action toggleLock, Action selectBroadcastFile)
    {
        content.Width = 344;
        content.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 0.8 };
        card.Child = content;
        card.Padding = new Thickness(18, 2, 18, 2);
        card.CornerRadius = new CornerRadius(9);
        card.Margin = new Thickness(2);

        RegisterText(OverlayField.Heading, heading);
        RegisterText(OverlayField.MapTitle, title);
        RegisterText(OverlayField.MapName, map);
        RegisterText(OverlayField.Stage, stage);
        RegisterText(OverlayField.Unavailable, unavailable);
        RegisterText(OverlayField.ErrorDetail, reason);
        RegisterText(OverlayField.Source, source);
        RegisterText(OverlayField.HotkeyHint, hint);
        heading.TextWrapping = TextWrapping.Wrap;
        title.TextWrapping = TextWrapping.Wrap;
        map.TextWrapping = TextWrapping.Wrap;
        source.TextWrapping = TextWrapping.Wrap;
        unavailable.TextWrapping = TextWrapping.Wrap;
        var settingsButton = SmallButton(OverlayField.SettingsButton, "设置", openSettings);
        var lockButton = SmallButton(OverlayField.LockButton, "锁定", toggleLock);
        editButtons.Children.Add(settingsButton);
        editButtons.Children.Add(lockButton);
        DockPanel.SetDock(editButtons, Dock.Right);
        top.Children.Add(editButtons);
        top.Children.Add(heading);
        content.Children.Add(top);
        content.Children.Add(title);
        map.Margin = new Thickness(0, 0, 0, 8);
        content.Children.Add(map);
        stage.Margin = new Thickness(0, 0, 0, 12);
        content.Children.Add(stage);

        for (int i = 0; i < RewardFields.Length; i++)
        {
            var field = RewardFields[i];
            var row = new Grid { Margin = new Thickness(0, 0, 0, i == 1 ? 3 : 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock
            {
                Text = RewardLabels[i], VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0)
            };
            RegisterText(field, label, RewardLabelStyle);
            row.Children.Add(label);
            values[i] = OverlayTypography.CreateText(field, "—");
            RegisterText(field, values[i]);
            Grid.SetColumn(values[i], 1);
            row.Children.Add(values[i]);
            if (field is OverlayField.RankPasses or OverlayField.EconPasses)
            {
                var progress = field == OverlayField.RankPasses ? rankProgress : econProgress;
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(progress, 1);
                Grid.SetColumn(progress, 1);
                row.Children.Add(progress);
            }
            fieldRows[i] = row;
            rewardRows.Children.Add(row);
        }
        content.Children.Add(rewardRows);
        unavailable.Margin = new Thickness(0, 0, 0, 8);
        unavailablePanel.Children.Add(unavailable);
        reason.TextWrapping = TextWrapping.Wrap;
        reason.Margin = new Thickness(0, 0, 0, 10);
        unavailablePanel.Children.Add(reason);
        selectFileButton = SmallButton(OverlayField.SelectFileButton, BroadcastFiles.SelectLabel, selectBroadcastFile);
        selectFileButton.Margin = new Thickness(0, 0, 0, 8);
        selectFileButton.HorizontalAlignment = HorizontalAlignment.Left;
        unavailablePanel.Children.Add(selectFileButton);
        content.Children.Add(unavailablePanel);
        footer.Children.Add(new Border { Height = 1, Background = Brush("#324354"), Margin = new Thickness(0, 0, 0, 8) });
        footer.Children.Add(source);
        hint.TextWrapping = TextWrapping.Wrap;
        footer.Children.Add(hint);
        content.Children.Add(footer);
    }

    private void RegisterText(OverlayField field, TextBlock text, OverlayTextStyle? defaults = null) =>
        styledText.Add((field, text, defaults ?? OverlayTypography.DefaultFor(field)));

    private Button SmallButton(OverlayField field, string text, Action action)
    {
        var label = OverlayTypography.CreateText(field, text);
        label.TextWrapping = TextWrapping.Wrap;
        RegisterText(field, label);
        var button = new Button { Content = label, Background = Brush("#263947"), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(5, 0, 0, 0), BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
        System.Windows.Automation.AutomationProperties.SetName(button, text);
        button.Click += (_, e) => { e.Handled = true; action(); };
        return button;
    }


    internal void Render(DisplayInfo info, bool fileUnavailable, bool isDemo)
    {
        inputUnavailable = fileUnavailable;
        demo = isDemo;
        lastDisplay = info;
        title.Text = info.Available ? (string.IsNullOrWhiteSpace(info.Title) ? info.Map : info.Title) : "地图信息";
        title.ToolTip = title.Text;
        map.Text = info.Map;
        map.ToolTip = info.Map;
        stage.Text = info.Stage is int number ? "第 " + number + " 关" + (info.StageInferred ? "（比分推断）" : "") : "";
        unavailable.Text = info.InferredStageMissingReward ? DisplayInfo.InferredStageUnavailableText
            : settings.MapSource == MapSource.Gsi && info.Map.Length > 0 && !inputUnavailable
                ? "当前无法获取关卡奖励" : DisplayInfo.UnavailableText;
        unavailablePanel.Visibility = info.Available ? Visibility.Collapsed : Visibility.Visible;
        if (info.Reward is { } reward)
        {
            var rank = DamageProgress.Calculate(info.Kills, reward.RankDamage);
            var econ = DamageProgress.Calculate(info.Kills, reward.EconDamage);
            SetPassReward(values[0], rankProgress, reward.GetRankPasses(settings.IsSvip), rank, lastRankProgress);
            values[1].Text = DisplayInfo.Ratio(reward.RankDamage);
            SetPassReward(values[2], econProgress, reward.EconPasses, econ, lastEconProgress);
            values[3].Text = DisplayInfo.Ratio(reward.EconDamage);
            lastRankProgress = rank;
            lastEconProgress = econ;
        }
        else lastRankProgress = lastEconProgress = null;
        reason.Text = info.Detail + (inputUnavailable ? "\n可从系统托盘选择" + BroadcastFiles.DisplayName + "，或按 " + settings.SettingsHotkey + " 打开设置。" : "");
        source.Text = info.Available ? info.Source : "等待有效的 ZE 地图与关卡";
        if (!demo && settings.MapSource == MapSource.Gsi)
            source.Text = info.Available
                ? (info.StageInferred ? "GSI 比分推断  /  " : "地图 · CS2 GSI  /  ") + info.Source
                : "地图来源 · CS2 GSI";
        ApplySourceColor();
        source.ToolTip = info.Detail;
        ApplyInformationVisibility();
    }

    private static Grid CreateProgressBar()
    {
        var bar = new Grid { Height = 4, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
        var track = new Border { Background = Brush("#66AEAEAE"), CornerRadius = new CornerRadius(2) };
        bar.Children.Add(track);
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), RenderTransform = new ScaleTransform(0, 1) });
        return bar;
    }

    private static void SetPassReward(TextBlock text, Grid bar, long baseReward, DamageProgress? progress, DamageProgress? previous)
    {
        text.Text = baseReward.ToString(CultureInfo.CurrentCulture);
        bar.Visibility = progress.HasValue ? Visibility.Visible : Visibility.Collapsed;
        var fill = (ScaleTransform)((Border)bar.Children[1]).RenderTransform;
        if (progress is not { } result)
        {
            fill.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            fill.ScaleX = 0;
            return;
        }
        string remaining = result.RemainingKills.ToString("0.###", CultureInfo.CurrentCulture);
        text.Text = $"{baseReward}+{result.Bonus}";
        text.Inlines.Add(new Run($" [{remaining}↑]") { Foreground = Brush("#AEAEAE") });
        if (previous == result) return;
        double start = fill.ScaleX;
        fill.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        fill.ScaleX = result.Fraction;
        if (previous is { } prior && result.Bonus > prior.Bonus && bar.IsLoaded && SystemParameters.ClientAreaAnimation)
        {
            var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170))));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(result.Fraction, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250))));
            fill.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        }
    }

    private void ApplyInformationVisibility()
    {
        static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
        var info = lastDisplay;
        top.Visibility = Show(!settings.Hides(OverlayField.Heading) || !settings.Locked);
        title.Visibility = Show(!settings.Hides(OverlayField.MapTitle));
        map.Visibility = Show(!settings.Hides(OverlayField.MapName) && !string.IsNullOrWhiteSpace(info?.Map));
        stage.Visibility = Show(info?.Stage.HasValue == true && !settings.Hides(OverlayField.Stage));
        for (int i = 0; i < RewardFields.Length; i++) fieldRows[i].Visibility = Show(!settings.Hides(RewardFields[i]));
        rewardRows.Visibility = Show(info?.Available == true && (settings.HiddenFields & RewardFieldMask) != RewardFieldMask);
        reason.Visibility = Show(!settings.Hides(OverlayField.ErrorDetail));
        source.Visibility = Show(!settings.Hides(OverlayField.Source));
        hint.Visibility = Show(!settings.Hides(OverlayField.HotkeyHint) || !settings.Locked);
        footer.Visibility = Show(source.Visibility == Visibility.Visible || hint.Visibility == Visibility.Visible);
        if (selectFileButton is not null) selectFileButton.Visibility = Show(inputUnavailable && !settings.Locked);
        ApplyRowSpacing(content);
        ApplyRowSpacing(rewardRows);
        ApplyRowSpacing(unavailablePanel);
        SetTopMargin(hint, 5 + (source.Visibility == Visibility.Visible ? settings.RowSpacing : 0));
        ApplyColumnSpacing();
        // With every normal field hidden, keep the window invisible until edit mode or an error.
        var normalFields = OverlayField.All & ~OverlayField.ErrorDetail;
        card.Opacity = info?.Available == true && settings.Locked && (settings.HiddenFields & normalFields) == normalFields ? 0 : 1;
    }

    private void ApplyRowSpacing(Panel panel)
    {
        bool followsVisibleRow = false;
        foreach (FrameworkElement row in panel.Children)
        {
            double baseTop = row == map ? 4 : row == unavailable ? 3 : row == footer ? 7 : 0;
            SetTopMargin(row, baseTop + (followsVisibleRow ? settings.RowSpacing : 0));
            followsVisibleRow |= row.Visibility == Visibility.Visible;
        }
    }

    private static void SetTopMargin(FrameworkElement element, double top)
    {
        var margin = element.Margin;
        margin.Top = top;
        element.Margin = margin;
    }

    private void ApplyColumnSpacing()
    {
        double width = 344;
        double numberWidth = 0, labelWidth = 0;
        for (int i = 0; i < RewardFields.Length; i++)
        {
            if (settings.Hides(RewardFields[i])) continue;
            width = Math.Max(width, values[i].FontSize * 10);
            if (rewardRows.Visibility != Visibility.Visible) continue;
            var label = fieldRows[i].Children[0];
            var unconstrained = new Size(double.PositiveInfinity, double.PositiveInfinity);
            label.Measure(unconstrained);
            values[i].Measure(unconstrained);
            labelWidth = Math.Max(labelWidth, label.DesiredSize.Width);
            numberWidth = Math.Max(numberWidth, values[i].DesiredSize.Width);
        }

        if (rewardRows.Visibility != Visibility.Visible)
        {
            content.Width = width;
            return;
        }

        foreach (var row in fieldRows) row.ColumnDefinitions[1].Width = new GridLength(numberWidth);
        content.Width = Math.Max(width + settings.ColumnSpacing, labelWidth + numberWidth);
    }

    internal void ApplyAppearance(Settings next)
    {
        bool svipChanged = settings.IsSvip != next.IsSvip;
        settings = next;
        System.Windows.Documents.TextElement.SetFontFamily(card, FontCatalog.Resolve(settings.OverlayFontFamily));
        foreach (var (field, text, defaults) in styledText)
            OverlayTypography.Apply(text, settings.TextStyleFor(field).WithFallback(defaults));
        var alignment = settings.ValueAlignment switch
        {
            RewardValueAlignment.Left => TextAlignment.Left,
            RewardValueAlignment.Center => TextAlignment.Center,
            _ => TextAlignment.Right
        };
        foreach (var value in values) value.TextAlignment = alignment;
        ((Border)rankProgress.Children[1]).Background = values[0].Foreground;
        ((Border)econProgress.Children[1]).Background = values[2].Foreground;
        ApplySourceColor();
        double backgroundOpacity = settings.Locked ? settings.BackgroundOpacity : Math.Max(0.15, settings.BackgroundOpacity);
        card.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(backgroundOpacity * 255), 13, 20, 28));
        card.BorderThickness = new Thickness(settings.Locked ? 0 : 1);
        card.BorderBrush = settings.Locked ? null : Brush("#A4ED86");
        content.LayoutTransform = new ScaleTransform(settings.Scale, settings.Scale);
        editButtons.Visibility = settings.Locked ? Visibility.Collapsed : Visibility.Visible;
        heading.Text = settings.Locked ? "ZE  /  通关奖励" : "ZE  /  拖动模式";
        hint.Text = settings.Locked ? settings.ToggleHotkey + " 显隐   ·   " + settings.MoveHotkey + " 拖动" : "按住卡片拖动 · Esc 锁定 · 右键设置";
        if (svipChanged && lastDisplay is { } info) Render(info, inputUnavailable, demo);
        else ApplyInformationVisibility();
    }

    private void ApplySourceColor()
    {
        var color = settings.TextStyleFor(OverlayField.Source).Color ??
            (lastDisplay?.Source.StartsWith("离线") == true ? "#F4D799" : OverlayTypography.DefaultFor(OverlayField.Source).Color!);
        source.Foreground = Brush(color);
    }

    internal bool FileActionVisible => selectFileButton?.Visibility == Visibility.Visible;
    internal bool UsesFont(string family) => values[0].FontFamily.Source == family;

    internal bool ShowsCustomTypography()
    {
        var rankLabel = (TextBlock)fieldRows[0].Children[0];
        return values[0].FontSize == 31 && values[0].FontWeight == FontWeights.ExtraBold &&
            rankLabel.FontSize == 31 && rankLabel.Foreground.ToString() == "#FFABCDEF" &&
            values[2].FontSize == 20 && values[2].FontWeight == FontWeights.Medium &&
            source.Foreground.ToString() == "#FF123456" && unavailable.FontSize == 20;
    }

    internal bool ShowsExpectedFields() => map.Visibility == Visibility.Collapsed &&
        fieldRows[1].Visibility == Visibility.Collapsed && source.Visibility == Visibility.Collapsed &&
        fieldRows[0].Visibility == Visibility.Visible;

    internal bool ShowsUnavailableMessage() => unavailablePanel.Visibility == Visibility.Visible &&
        unavailable.Text == DisplayInfo.UnavailableText && card.Opacity == 1;
}

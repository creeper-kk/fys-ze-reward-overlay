using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FysZeRewardOverlay;

public sealed record OverlayTextStyle(double? Size = null, string? Color = null, int? Weight = null)
{
    public const int MinSize = 8;
    public const int MaxSize = 64;

    internal static readonly (int? Value, string Label)[] Weights =
    {
        (null, "沿用"), (300, "细体"), (400, "常规"), (500, "中等"),
        (600, "半粗"), (700, "粗体"), (800, "特粗"), (900, "黑体")
    };

    internal static bool IsValidSize(double size) => size is >= MinSize and <= MaxSize;
    internal static bool IsValidColor(string color) => color.Length == 7 && color[0] == '#' &&
        int.TryParse(color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _);

    internal OverlayTextStyle Validated() => new(
        Size is double size && IsValidSize(size) ? size : null,
        Color is string color && IsValidColor(color) ? color.ToUpperInvariant() : null,
        Weights.Any(option => option.Value == Weight) ? Weight : null);

    public OverlayTextStyle WithFallback(OverlayTextStyle fallback) => new(
        Size ?? fallback.Size, Color ?? fallback.Color, Weight ?? fallback.Weight);
}

internal static class OverlayTypography
{
    internal static readonly IReadOnlyDictionary<OverlayField, string> Labels = OverlayOptions.Fields.Concat(new[]
    {
        (OverlayField.Unavailable, "无法获取信息提示"), (OverlayField.SettingsButton, "设置按钮"),
        (OverlayField.LockButton, "锁定按钮"), (OverlayField.SelectFileButton, "选择" + BroadcastFiles.DisplayName + "按钮")
    }).ToDictionary(entry => entry.Item1, entry => entry.Item2);

    internal static OverlayTextStyle DefaultFor(OverlayField field) => field switch
    {
        OverlayField.Heading => new(12, "#A4ED86", 600),
        OverlayField.MapTitle => new(19, "#F2F5F8", 600),
        OverlayField.MapName => new(12, "#A5AFBC", 400),
        OverlayField.Stage => new(12, "#A4ED86", 600),
        OverlayField.RankPasses or OverlayField.RankDamage => new(32, "#4B9FFF", 400),
        OverlayField.EconPasses or OverlayField.EconDamage => new(32, "#F0BA0C", 400),
        OverlayField.Source => new(10, "#ADB8C8", 400),
        OverlayField.HotkeyHint => new(10, "#98A6B7", 400),
        OverlayField.ErrorDetail => new(11, "#A5AFBC", 400),
        OverlayField.Unavailable => new(17, "#F2F5F8", 600),
        OverlayField.SettingsButton or OverlayField.LockButton or OverlayField.SelectFileButton => new(10, "#FFFFFF", 400),
        _ => new(18, "#D5EDFF", 400)
    };

    internal static TextBlock CreateText(OverlayField field, string value)
    {
        var style = DefaultFor(field);
        return OverlayWindow.Text(value, style.Size!.Value, style.Color!, style.Weight == 600);
    }

    internal static void Apply(TextBlock text, OverlayTextStyle style)
    {
        text.FontSize = style.Size!.Value;
        text.Foreground = OverlayWindow.Brush(style.Color!);
        text.FontWeight = FontWeight.FromOpenTypeWeight(style.Weight!.Value);
    }
}

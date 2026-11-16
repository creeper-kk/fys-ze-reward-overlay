using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace FysZeRewardOverlay;

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<OverlayField>))]
public enum OverlayField
{
    None = 0,
    Heading = 1,
    MapTitle = 2,
    MapName = 4,
    Stage = 8,
    RankPasses = 16,
    RankDamage = 32,
    EconPasses = 64,
    EconDamage = 128,
    Source = 256,
    HotkeyHint = 512,
    ErrorDetail = 1024,
    All = 2047,
    Unavailable = 2048,
    SettingsButton = 4096,
    LockButton = 8192,
    SelectFileButton = 16384
}

public static class OverlayOptions
{
    public static readonly (OverlayField Field, string Label)[] Fields =
    {
        (OverlayField.Heading, "顶部标题"), (OverlayField.MapTitle, "地图译名"),
        (OverlayField.MapName, "地图英文名"), (OverlayField.Stage, "当前关卡"),
        (OverlayField.RankPasses, "通关云点"), (OverlayField.RankDamage, "云点伤害转化比"),
        (OverlayField.EconPasses, "通关龙晶"), (OverlayField.EconDamage, "龙晶伤害转化比"),
        (OverlayField.Source, "数据来源 / 缓存状态"), (OverlayField.HotkeyHint, "底部快捷键提示"),
        (OverlayField.ErrorDetail, "无法获取信息的详细原因")
    };

    internal static readonly OverlayField[] ReorderableFields = Fields.Select(option => option.Field)
        .Where(field => field is not (OverlayField.Heading or OverlayField.Source or OverlayField.HotkeyHint or OverlayField.ErrorDetail))
        .ToArray();

    public static bool TryIntervals(string pollSeconds, string rewardMinutes, out int milliseconds, out int minutes, out string error)
    {
        milliseconds = 600; minutes = 10; error = "";
        if (!double.TryParse(pollSeconds, NumberStyles.Float, CultureInfo.CurrentCulture, out var seconds) || !double.IsFinite(seconds) || seconds < 0.2 || seconds > 60)
        { error = "地图 / 关卡读取间隔请输入 0.2～60 秒。"; return false; }
        if (!int.TryParse(rewardMinutes, out minutes) || minutes < 1 || minutes > Settings.MaxRewardRefreshMinutes)
        { error = $"奖励联网更新间隔请输入 1～{Settings.MaxRewardRefreshMinutes} 的整数分钟。"; return false; }
        milliseconds = (int)Math.Round(seconds * 1000);
        return true;
    }
}

public record FontOption(string Name, string DisplayName, FontFamily Family);
public static class FontCatalog
{
    public const string DefaultName = "Microsoft YaHei UI";
    private static readonly Lazy<FontOption[]> installed = new(() => Fonts.SystemFontFamilies.Select(f =>
    {
        var language = XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag);
        string display = f.FamilyNames.TryGetValue(language, out var localized) ? localized : f.Source;
        return new FontOption(f.Source, display, f);
    }).OrderBy(f => f.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray());
    public static FontOption[] Installed => installed.Value;
    public static FontFamily Resolve(string? name) => Installed.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))?.Family ?? new FontFamily(DefaultName);
}

public static class BroadcastFiles
{
    internal const string DisplayName = "直播助手叠层文本";
    internal const string SelectLabel = "选择" + DisplayName + "…";

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "fys", "fys.broadcast.assistant.txt");
    public static bool TryNormalize(string text, out string path)
    {
        path = "";
        try
        {
            string expanded = Environment.ExpandEnvironmentVariables(text.Trim().Trim('"'));
            if (!Path.IsPathFullyQualified(expanded) || expanded.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            path = Path.GetFullPath(expanded);
            return !Directory.Exists(path) && !Path.EndsInDirectorySeparator(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.Security.SecurityException) { return false; }
    }
    public static string? Select(Window owner, string currentPath)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择" + DisplayName, Filter = DisplayName + " (*.txt)|*.txt|所有文件 (*.*)|*.*",
            CheckFileExists = true, Multiselect = false, FileName = "fys.broadcast.assistant.txt"
        };
        if (TryNormalize(currentPath, out var normalized))
        {
            var parent = Path.GetDirectoryName(normalized);
            if (Directory.Exists(parent)) { dialog.InitialDirectory = parent; dialog.FileName = Path.GetFileName(normalized); }
        }
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using System.Windows.Interop;

namespace FysZeRewardOverlay;

public enum MapSource { Broadcast, Gsi }
public enum RewardValueAlignment { Right, Left, Center }

public sealed class Settings
{
    internal const double DefaultScale = 0.8;
    internal const double DefaultBackgroundOpacity = 0.35;
    internal const double DefaultLeft = 2234;
    internal const double DefaultTop = 250;
    internal const int MaxRowSpacing = 48;
    internal const int MinColumnSpacing = -48;
    internal const int MaxColumnSpacing = 128;
    internal const bool NativeDamageEnabled = false;
    internal const int MaxRewardRefreshMinutes = 4320;
    internal const int DefaultAutoUpdateDays = 3;
    internal const int MaxAutoUpdateDays = 365;
    internal const OverlayField DefaultHiddenFields = OverlayField.Heading | OverlayField.MapTitle |
        OverlayField.MapName | OverlayField.Stage | OverlayField.Source | OverlayField.HotkeyHint;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MapSource MapSource { get; set; }
    public bool IsSvip { get; set; }
    private bool useNativeDamage;
    public bool UseNativeDamage
    {
        get => NativeDamageEnabled && useNativeDamage;
        set => useNativeDamage = value;
    }
    public string BroadcastPath { get; set; } = BroadcastFiles.DefaultPath;
    public string ToggleHotkey { get; set; } = "Ctrl+Shift+F8";
    public string MoveHotkey { get; set; } = "Ctrl+Shift+F9";
    public string SettingsHotkey { get; set; } = "Ctrl+Shift+F10";
    public string StageUpHotkey { get; set; } = "Shift+=";
    public string StageDownHotkey { get; set; } = "Shift+-";
    public double BackgroundOpacity { get; set; } = DefaultBackgroundOpacity;
    public double Scale { get; set; } = DefaultScale;
    public int RowSpacing { get; set; }
    public int ColumnSpacing { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RewardValueAlignment ValueAlignment { get; set; }
    public int PollIntervalMilliseconds { get; set; } = 600;
    public int RewardRefreshMinutes { get; set; } = 10;
    public int AutoUpdateDays { get; set; } = DefaultAutoUpdateDays;
    public bool ObsEnabled { get; set; }
    public string OverlayFontFamily { get; set; } = FontCatalog.DefaultName;
    public OverlayTextStyle GlobalTextStyle { get; set; } = new();
    public Dictionary<OverlayField, OverlayTextStyle> FieldTextStyles { get; set; } = new();
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OverlayField HiddenFields { get; set; } = DefaultHiddenFields;
    public OverlayField[] DisplayOrder { get; set; } = (OverlayField[])OverlayOptions.ReorderableFields.Clone();
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Locked { get; set; } = true;

    // Property initializers preserve the defaults of older, partial settings files.
    public static Settings CreateDefault() => new()
    {
        ToggleHotkey = "Alt+P",
        MoveHotkey = "Alt+O",
        Scale = 0.85,
        RowSpacing = 6,
        ColumnSpacing = -24,
        ValueAlignment = RewardValueAlignment.Left,
        PollIntervalMilliseconds = 10000,
        RewardRefreshMinutes = 1440,
        GlobalTextStyle = new(Size: 28),
        HiddenFields = OverlayField.Heading | OverlayField.MapTitle | OverlayField.Stage |
            OverlayField.Source | OverlayField.HotkeyHint | OverlayField.ErrorDetail,
        Left = DefaultLeft,
        Top = DefaultTop
    };

    public static Settings Load(string directory)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(directory, "settings.json")));
            return settings?.Validate() ?? CreateDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return CreateDefault();
        }
    }

    public static Settings Import(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var writableProperties = JsonSerializerOptions.Default.GetTypeInfo(typeof(Settings)).Properties;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.EnumerateObject().Any(property => writableProperties.Any(candidate => candidate.Set is not null && candidate.Name == property.Name)))
            throw new JsonException("请选择 ZE 奖励悬浮窗的 JSON 配置文件。");

        var settings = root.Deserialize<Settings>()!.Validate();
        if (settings.BroadcastPath.Length > 0)
        {
            if (!BroadcastFiles.TryNormalize(settings.BroadcastPath, out var normalized))
                throw new JsonException("配置中的直播助手叠层文本路径无效，请填写绝对路径。");
            settings.BroadcastPath = normalized;
        }
        return settings;
    }

    private Settings Validate()
    {
        BackgroundOpacity = double.IsFinite(BackgroundOpacity) ? Math.Clamp(BackgroundOpacity, 0, 0.95) : DefaultBackgroundOpacity;
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, 0.7, 1.8) : DefaultScale;
        RowSpacing = Math.Clamp(RowSpacing, 0, MaxRowSpacing);
        ColumnSpacing = Math.Clamp(ColumnSpacing, MinColumnSpacing, MaxColumnSpacing);
        if (!Enum.IsDefined(ValueAlignment)) ValueAlignment = RewardValueAlignment.Right;
        if (Left.HasValue && !double.IsFinite(Left.Value)) Left = null;
        if (Top.HasValue && !double.IsFinite(Top.Value)) Top = null;
        if (!Enum.IsDefined(MapSource)) MapSource = MapSource.Broadcast;
        if (string.IsNullOrWhiteSpace(BroadcastPath)) BroadcastPath = MapSource == MapSource.Gsi ? "" : BroadcastFiles.DefaultPath;
        PollIntervalMilliseconds = Math.Clamp(PollIntervalMilliseconds, 200, 60000);
        RewardRefreshMinutes = Math.Clamp(RewardRefreshMinutes, 1, MaxRewardRefreshMinutes);
        AutoUpdateDays = Math.Clamp(AutoUpdateDays, 0, MaxAutoUpdateDays);
        if (string.IsNullOrWhiteSpace(OverlayFontFamily)) OverlayFontFamily = FontCatalog.DefaultName;
        GlobalTextStyle = GlobalTextStyle?.Validated() ?? new();
        FieldTextStyles = (FieldTextStyles ?? new())
            .Where(entry => OverlayTypography.Labels.ContainsKey(entry.Key) && entry.Value is not null)
            .ToDictionary(entry => entry.Key, entry => entry.Value.Validated());
        HiddenFields &= OverlayField.All;
        var defaultOrder = OverlayOptions.ReorderableFields;
        DisplayOrder = (DisplayOrder ?? Array.Empty<OverlayField>()).Intersect(defaultOrder).Union(defaultOrder).ToArray();
        return this;
    }

    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        Export(Path.Combine(directory, "settings.json"));
    }

    public void Export(string path) =>
        AtomicFile.Write(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

    public Settings Copy()
    {
        var copy = (Settings)MemberwiseClone();
        copy.FieldTextStyles = new(FieldTextStyles);
        copy.DisplayOrder = (OverlayField[])DisplayOrder.Clone();
        return copy;
    }
    [JsonIgnore]
    public string[] Hotkeys => new[] { ToggleHotkey, MoveHotkey, SettingsHotkey, StageUpHotkey, StageDownHotkey };
    internal int ActiveHotkeyCount => MapSource == MapSource.Gsi ? 5 : 3;
    public bool Hides(OverlayField field) => (HiddenFields & field) != 0;
    public OverlayTextStyle TextStyleFor(OverlayField field) => FieldTextStyles.TryGetValue(field, out var style)
        ? style.WithFallback(GlobalTextStyle) : GlobalTextStyle;
}

public readonly record struct Shortcut(uint Modifiers, uint VirtualKey)
{
    public static bool TryParse(string text, out Shortcut result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        uint modifiers = 0;
        Key key = Key.None;
        foreach (var word in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (word.ToUpperInvariant())
            {
                case "CTRL": case "CONTROL": modifiers |= 2; break;
                case "ALT": modifiers |= 1; break;
                case "SHIFT": modifiers |= 4; break;
                default:
                    string name = word switch
                    {
                        "=" => nameof(Key.OemPlus),
                        "-" => nameof(Key.OemMinus),
                        _ => word.Length == 1 && char.IsDigit(word[0]) ? "D" + word : word
                    };
                    if (key != Key.None || !Enum.TryParse(name, true, out key)) return false;
                    break;
            }
        }
        bool validKey = key >= Key.A && key <= Key.Z || key >= Key.D0 && key <= Key.D9 ||
            key >= Key.F1 && key <= Key.F24 || key is Key.OemPlus or Key.OemMinus;
        if (modifiers == 0 || !validKey || key == Key.F12) return false;
        result = new(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        return true;
    }
    public static string? FromEvent(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        string name = key switch { Key.OemPlus => "=", Key.OemMinus => "-", _ => key.ToString() };
        string value = ((mods & ModifierKeys.Control) != 0 ? "Ctrl+" : "") + ((mods & ModifierKeys.Alt) != 0 ? "Alt+" : "") + ((mods & ModifierKeys.Shift) != 0 ? "Shift+" : "") + name;
        if ((mods & ModifierKeys.Windows) != 0) return null;
        return TryParse(value, out _) ? value : null;
    }
}

public sealed class HotkeyManager : IDisposable
{
    private readonly IntPtr handle;
    private readonly HwndSource source;
    private readonly Action<int> action;
    private Shortcut[] registered = Array.Empty<Shortcut>();
    public int RegisteredCount => registered.Length;
    public HotkeyManager(IntPtr handle, Action<int> action)
    {
        this.handle = handle;
        this.action = action;
        source = HwndSource.FromHwnd(handle);
        source.AddHook(Hook);
    }
    public bool Apply(string[] values, out string error) => Apply(values, values.Length, out error);

    internal bool Apply(string[] values, int activeCount, out string error)
    {
        var next = new List<Shortcut>();
        foreach (var value in values)
        {
            if (!Shortcut.TryParse(value, out var shortcut)) { error = "快捷键无效：" + value + "。请使用 Ctrl / Alt / Shift 加字母、数字、功能键（不含 F12）或 = / -。"; return false; }
            if (next.Contains(shortcut)) { error = "快捷键不能重复。"; return false; }
            next.Add(shortcut);
        }
        next.RemoveRange(activeCount, next.Count - activeCount);
        var previous = registered;
        Unregister();
        for (int i = 0; i < next.Count; i++)
        {
            if (!Native.RegisterHotKey(handle, i + 1, next[i].Modifiers | 0x4000, next[i].VirtualKey))
            {
                for (int j = 0; j < i; j++) Native.UnregisterHotKey(handle, j + 1);
                var restored = new List<Shortcut>();
                for (int j = 0; j < previous.Length; j++)
                    if (Native.RegisterHotKey(handle, j + 1, previous[j].Modifiers | 0x4000, previous[j].VirtualKey)) restored.Add(previous[j]);
                registered = previous;
                error = "快捷键 " + values[i] + " 已被其他程序占用，请换一个组合。" + (restored.Count != previous.Length ? " 原快捷键未全部恢复，可从托盘打开设置。" : "");
                return false;
            }
        }
        registered = next.ToArray();
        error = "";
        return true;
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312) { handled = true; action(wParam.ToInt32()); }
        return IntPtr.Zero;
    }
    private void Unregister() { for (int i = 0; i < registered.Length; i++) Native.UnregisterHotKey(handle, i + 1); registered = Array.Empty<Shortcut>(); }
    public void Dispose() { Unregister(); source.RemoveHook(Hook); }
}

internal static class Native
{
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] internal static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
    internal static void MoveWindow(IntPtr window, int x, int y) =>
        SetWindowPos(window, IntPtr.Zero, x, y, 0, 0, 0x1 | 0x4 | 0x10); // Keep size, z-order and activation.
    internal static void SetClickThrough(IntPtr window, bool enabled)
    {
        const int transparent = 0x20, noActivate = 0x08000000, toolWindow = 0x80;
        var styles = GetWindowLong(window, -20) | toolWindow;
        styles = enabled ? styles | transparent | noActivate : styles & ~transparent & ~noActivate;
        SetWindowLong(window, -20, styles);
        SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x20);
    }
}

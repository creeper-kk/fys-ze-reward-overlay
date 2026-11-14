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

public sealed class Settings
{
    public string BroadcastPath { get; set; } = BroadcastFiles.DefaultPath;
    public string ToggleHotkey { get; set; } = "Ctrl+Shift+F8";
    public string MoveHotkey { get; set; } = "Ctrl+Shift+F9";
    public string SettingsHotkey { get; set; } = "Ctrl+Shift+F10";
    public double BackgroundOpacity { get; set; } = 0.55;
    public double Scale { get; set; } = 1;
    public int PollIntervalMilliseconds { get; set; } = 600;
    public int RewardRefreshMinutes { get; set; } = 10;
    public string OverlayFontFamily { get; set; } = FontCatalog.DefaultName;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OverlayField HiddenFields { get; set; } = OverlayField.None;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Locked { get; set; } = true;
    public static Settings Load(string directory)
    {
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(directory, "settings.json"))) ?? new();
            s.BackgroundOpacity = double.IsFinite(s.BackgroundOpacity) ? Math.Clamp(s.BackgroundOpacity, 0, 0.95) : 0.55;
            s.Scale = double.IsFinite(s.Scale) ? Math.Clamp(s.Scale, 0.7, 1.8) : 1;
            if (s.Left.HasValue && !double.IsFinite(s.Left.Value)) s.Left = null;
            if (s.Top.HasValue && !double.IsFinite(s.Top.Value)) s.Top = null;
            if (string.IsNullOrWhiteSpace(s.BroadcastPath)) s.BroadcastPath = new Settings().BroadcastPath;
            s.PollIntervalMilliseconds = Math.Clamp(s.PollIntervalMilliseconds, 200, 60000);
            s.RewardRefreshMinutes = Math.Clamp(s.RewardRefreshMinutes, 1, 1440);
            if (string.IsNullOrWhiteSpace(s.OverlayFontFamily)) s.OverlayFontFamily = FontCatalog.DefaultName;
            s.HiddenFields &= OverlayField.All;
            return s;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return new(); }
    }
    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        AtomicFile.Write(Path.Combine(directory, "settings.json"), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
    public Settings Copy() => (Settings)MemberwiseClone();
    [JsonIgnore]
    public string[] Hotkeys => new[] { ToggleHotkey, MoveHotkey, SettingsHotkey };
    public bool Hides(OverlayField field) => (HiddenFields & field) != 0;
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
                    string name = word.Length == 1 && char.IsDigit(word[0]) ? "D" + word : word;
                    if (key != Key.None || !Enum.TryParse(name, true, out key)) return false;
                    break;
            }
        }
        bool validKey = key >= Key.A && key <= Key.Z || key >= Key.D0 && key <= Key.D9 || key >= Key.F1 && key <= Key.F24;
        if (modifiers == 0 || !validKey || key == Key.F12) return false;
        result = new(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        return true;
    }
    public static string? FromEvent(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        string value = ((mods & ModifierKeys.Control) != 0 ? "Ctrl+" : "") + ((mods & ModifierKeys.Alt) != 0 ? "Alt+" : "") + ((mods & ModifierKeys.Shift) != 0 ? "Shift+" : "") + key.ToString();
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
    public bool Apply(string[] values, out string error)
    {
        var next = new List<Shortcut>();
        foreach (var value in values)
        {
            if (!Shortcut.TryParse(value, out var shortcut)) { error = "快捷键无效：" + value + "。请使用 Ctrl / Alt / Shift 加字母、数字或功能键（不含 F12）。"; return false; }
            if (next.Contains(shortcut)) { error = "三个快捷键不能重复。"; return false; }
            next.Add(shortcut);
        }
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
    internal static void SetClickThrough(IntPtr window, bool enabled)
    {
        const int transparent = 0x20, noActivate = 0x08000000, toolWindow = 0x80;
        var styles = GetWindowLong(window, -20) | toolWindow;
        styles = enabled ? styles | transparent | noActivate : styles & ~transparent & ~noActivate;
        SetWindowLong(window, -20, styles);
        SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x20);
    }
}

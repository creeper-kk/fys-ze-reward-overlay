using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FysZeRewardOverlay;

internal sealed class ReleaseDialog : Window
{
    internal ReleaseDialog(ReleaseCheck release, bool installed, bool canUpdate, Window? owner)
    {
        Title = installed ? $"已更新到 {release.Tag}" : $"发现新版本 {release.Tag}";
        Width = Math.Min(660, SystemParameters.WorkArea.Width);
        Height = Math.Min(500, SystemParameters.WorkArea.Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (owner?.IsVisible == true) Owner = owner;
        ShowInTaskbar = Owner is null;
        Background = OverlayWindow.Brush("#18181B");
        Foreground = OverlayWindow.Brush("#F0F0F2");
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/FysZeRewardOverlay;component/SettingsTheme.xaml")
        });

        var content = new DockPanel { Margin = new Thickness(18) };
        Content = content;
        var heading = OverlayWindow.Text(Title, 20, "#F0F0F2", true);
        heading.Margin = new Thickness(0, 0, 0, 12);
        DockPanel.SetDock(heading, Dock.Top);
        content.Children.Add(heading);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        content.Children.Add(footer);
        Add("查看 GitHub Release", () =>
        {
            try { Process.Start(new ProcessStartInfo(release.PageUrl) { UseShellExecute = true })?.Dispose(); }
            catch (Win32Exception ex) { MessageBox.Show(this, "无法打开发布页面：" + ex.Message); }
        });
        var close = Add(installed ? "关闭" : canUpdate ? "暂不更新" : "关闭", null);
        close.IsCancel = true;
        close.IsDefault = true;
        if (canUpdate)
        {
            var accept = Add("更新并重启", () => DialogResult = true);
            accept.Background = OverlayWindow.Brush("#006BD0");
        }

        if (!installed && !canUpdate)
        {
            var hint = OverlayWindow.Text("可从发布页面下载此版本。", 12, "#A1A1AA");
            hint.Margin = new Thickness(0, 8, 0, 0);
            DockPanel.SetDock(hint, Dock.Bottom);
            content.Children.Add(hint);
        }
        content.Children.Add(new TextBox
        {
            Text = string.IsNullOrWhiteSpace(release.Notes) ? "暂无发布说明。" : release.Notes,
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(12), Background = OverlayWindow.Brush("#222225"), Foreground = Foreground,
            BorderBrush = OverlayWindow.Brush("#38383D")
        });

        Button Add(string label, Action? action)
        {
            var button = new Button
            {
                Content = label, Style = (Style)FindResource("SettingsButton"), Margin = new Thickness(8, 0, 0, 0)
            };
            if (action is not null) button.Click += (_, _) => action();
            footer.Children.Add(button);
            return button;
        }
    }
}

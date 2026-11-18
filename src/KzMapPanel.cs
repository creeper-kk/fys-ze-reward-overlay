using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using static FysZeRewardOverlay.OverlayWindow;

namespace FysZeRewardOverlay;

internal sealed class KzMapPanel : DockPanel
{
    private readonly TextBlock mapName = Text("", 22, "#F5F5F6", true);
    private readonly TextBlock modeLabel = Text("", 12, "#A1BDE1", true);
    private readonly TextBlock status = Text("等待 CS2 GSI 推送地图", 12, "#A1A1AA");
    private readonly StackPanel courses = new();
    private readonly SmoothScrollViewer courseScroll;
    private (string Name, string Status, KzMode? CurrentMode, KzMode TierMode, KzMap? Data) last;

    internal KzMapPanel()
    {
        var heading = new StackPanel { Margin = new Thickness(1, 5, 0, 14) };
        heading.Children.Add(Text("当前地图信息", 15, "#F0F0F2", true));
        mapName.Margin = new Thickness(0, 9, 0, 0);
        mapName.TextWrapping = TextWrapping.Wrap;
        mapName.Visibility = Visibility.Collapsed;
        heading.Children.Add(mapName);
        modeLabel.Margin = new Thickness(0, 5, 0, 0);
        modeLabel.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(modeLabel, AutomationLiveSetting.Polite);
        heading.Children.Add(modeLabel);
        status.Margin = new Thickness(0, 5, 0, 0);
        status.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        heading.Children.Add(status);
        SetDock(heading, Dock.Top);
        Children.Add(heading);
        courseScroll = new SmoothScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = courses, CanContentScroll = false
        };
        Children.Add(courseScroll);
    }

    internal void Update(string name, string message, KzMode? currentMode, KzMode tierMode, KzMap? data)
    {
        if (last == (name, message, currentMode, tierMode, data)) return;
        if (last.Name != name) courseScroll.ScrollToTop();
        last = (name, message, currentMode, tierMode, data);
        mapName.Text = name;
        mapName.Visibility = name.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        modeLabel.Text = currentMode switch
        {
            KzMode.Ckz => "当前模式 · CKZ",
            KzMode.Vnl => "当前模式 · VNL",
            KzMode.Auto => "当前模式 · 待识别",
            _ => ""
        };
        modeLabel.Visibility = currentMode.HasValue ? Visibility.Visible : Visibility.Collapsed;
        courses.Children.Clear();
        status.Text = message;
        status.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (data is null) return;

        foreach (var course in data.Courses)
        {
            var details = new StackPanel();
            var title = Text(course.Name, 14, "#F5F5F6", true);
            title.TextWrapping = TextWrapping.Wrap;
            title.Margin = new Thickness(0, 0, 0, 8);
            details.Children.Add(title);
            if (tierMode is KzMode.Auto or KzMode.Ckz) AddTiers(details, "CKZ", course.Ckz);
            if (tierMode is KzMode.Auto or KzMode.Vnl) AddTiers(details, "VNL", course.Vnl);
            courses.Children.Add(new Border
            {
                Child = details, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(13, 11, 13, 11),
                Background = Brush("#19191B"), BorderBrush = Brush("#2B2B2E"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8)
            });
        }
    }

    private static void AddTiers(Panel details, string label, KzTiers? tiers)
    {
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        var mode = Text(label, 12, "#A1BDE1", true);
        mode.Width = 40;
        row.Children.Add(mode);
        var values = new WrapPanel();
        var nub = Text("NUB  " + KzTiers.Format(tiers?.Nub), 12, "#E6E6EB");
        nub.Margin = new Thickness(0, 0, 14, 0);
        values.Children.Add(nub);
        values.Children.Add(Text("PRO  " + KzTiers.Format(tiers?.Pro), 12, "#E6E6EB"));
        row.Children.Add(values);
        details.Children.Add(row);
    }
}

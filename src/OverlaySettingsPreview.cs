using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static FysZeRewardOverlay.OverlayWindow;

namespace FysZeRewardOverlay;

internal sealed class OverlaySettingsPreview : DockPanel
{
    private readonly RewardCard template = new(() => { }, () => { }, () => { });
    private readonly RadioButton rewards;
    private readonly RadioButton unavailable;
    private readonly RadioButton move;
    private readonly Border canvas;
    private readonly TextBlock empty = Text("当前模板中的显示项均已隐藏", 13, "#B6B6BD");
    private static readonly Brush GridBackground = CreateGridBackground();
    private static readonly Brush SolidBackground = Brush("#202023");
    private Settings? draft;
    private OverlayField field;

    internal OverlaySettingsPreview(Style optionStyle)
    {
        rewards = Option("通关奖励", "PreviewTemplate");
        unavailable = Option("不可用提示", "PreviewTemplate");
        move = Option("拖动模式", "PreviewTemplate");
        var heading = Text("悬浮窗预览", 15, "#F0F0F2", true);
        heading.Margin = new Thickness(0, 0, 0, 13);
        AddAbove(heading);
        var templates = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        templates.Children.Add(rewards);
        templates.Children.Add(unavailable);
        templates.Children.Add(move);
        AddAbove(templates);

        var backgrounds = new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };
        var grid = Option("网格背景", "PreviewBackground");
        var solid = Option("纯色背景", "PreviewBackground");
        backgrounds.Children.Add(grid);
        backgrounds.Children.Add(solid);
        AddAbove(backgrounds);

        var display = new Grid();
        display.Children.Add(new Viewbox
        {
            Child = template.Element, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12), IsHitTestVisible = false
        });
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.Visibility = Visibility.Collapsed;
        display.Children.Add(empty);
        canvas = new Border { Child = display, Background = GridBackground, CornerRadius = new CornerRadius(9), ClipToBounds = true };
        Children.Add(canvas);

        rewards.Checked += (_, _) => Render();
        unavailable.Checked += (_, _) => Render();
        move.Checked += (_, _) => Render();
        grid.Checked += (_, _) => canvas.Background = GridBackground;
        solid.Checked += (_, _) => canvas.Background = SolidBackground;
        rewards.IsChecked = true;
        grid.IsChecked = true;

        RadioButton Option(string name, string group) => new()
        {
            Content = name, GroupName = group, HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 5), Style = optionStyle
        };
    }

    internal void Update(Settings settings, OverlayField selectedField)
    {
        draft = settings;
        if (field != selectedField)
        {
            field = selectedField;
            switch (field)
            {
                case OverlayField.None:
                    break;
                case OverlayField.ErrorDetail or OverlayField.Unavailable or OverlayField.SelectFileButton:
                    unavailable.IsChecked = true;
                    break;
                case OverlayField.SettingsButton or OverlayField.LockButton:
                    move.IsChecked = true;
                    break;
                default:
                    rewards.IsChecked = true;
                    break;
            }
        }
        Render();
    }

    private void Render()
    {
        if (draft is null) return;
        bool showError = unavailable.IsChecked == true;
        bool showFileButton = showError && field == OverlayField.SelectFileButton;
        draft.Locked = move.IsChecked != true && !showFileButton;
        template.ApplyAppearance(draft);
        var info = showError
            ? DisplayInfo.Resolve(BroadcastReader.Invalid(showFileButton ? BroadcastFiles.DisplayName + "尚未生成" : "尚未读取到当前关卡"), null)
            : RewardCard.Sample;
        template.Render(info, showFileButton, false);
        empty.Visibility = template.Element.Opacity == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddAbove(UIElement element)
    {
        SetDock(element, Dock.Top);
        Children.Add(element);
    }

    private static Brush CreateGridBackground()
    {
        var pattern = new DrawingGroup();
        pattern.Children.Add(new GeometryDrawing(Brush("#212123"), null, new RectangleGeometry(new Rect(0, 0, 20, 20))));
        pattern.Children.Add(new GeometryDrawing(Brush("#2C2C2E"), null, new RectangleGeometry(new Rect(0, 0, 10, 10))));
        pattern.Children.Add(new GeometryDrawing(Brush("#2C2C2E"), null, new RectangleGeometry(new Rect(10, 10, 10, 10))));
        var brush = new DrawingBrush(pattern)
        {
            TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 20, 20)
        };
        brush.Freeze();
        return brush;
    }
}

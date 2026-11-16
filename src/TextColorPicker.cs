using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace FysZeRewardOverlay;

internal sealed class TextColorPicker : Window
{
    private static readonly Color[] Spectrum =
        { Colors.Red, Colors.Yellow, Colors.Lime, Colors.Cyan, Colors.Blue, Colors.Magenta, Colors.Red };
    private readonly Action<string> previewColor;
    private readonly Grid colorField = new() { Height = 144, Cursor = Cursors.Cross, Focusable = true };
    private readonly Slider hue = new() { Maximum = 360, SmallChange = 1, LargeChange = 15, IsMoveToPointEnabled = true };
    private readonly SolidColorBrush hueBrush = new();
    private readonly SolidColorBrush selectedBrush = new();
    private readonly TextBlock hexValue = OverlayWindow.Text("", 12, "#E8E8EC");
    private readonly Ellipse marker = new()
    {
        Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2,
        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 0, Opacity = 1 }
    };
    private double saturation;
    private double brightness;

    internal string SelectedHex => hexValue.Text;

    internal TextColorPicker(Color initial, Action<string> previewColor, Style buttonStyle)
    {
        this.previewColor = previewColor;
        Title = "选择文字颜色";
        Width = 320;
        Height = 326;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStyle = WindowStyle.ToolWindow;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = OverlayWindow.Brush("#18181B");
        UseLayoutRounding = true;

        var content = new StackPanel { Margin = new Thickness(16) };
        Content = content;
        colorField.Background = hueBrush;
        colorField.Children.Add(new Border { Background = new LinearGradientBrush(Colors.White, Colors.Transparent, 0) });
        colorField.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90) });
        var markerLayer = new Canvas { IsHitTestVisible = false };
        markerLayer.Children.Add(marker);
        colorField.Children.Add(markerLayer);
        content.Children.Add(colorField);
        AutomationProperties.SetName(colorField, "饱和度与明度");

        var hueLabel = OverlayWindow.Text("色相", 12, "#E8E8EC");
        hueLabel.Margin = new Thickness(0, 12, 0, 4);
        content.Children.Add(hueLabel);
        AutomationProperties.SetLabeledBy(hue, hueLabel);
        content.Children.Add(hue);
        var spectrum = new LinearGradientBrush { EndPoint = new Point(1, 0) };
        for (int i = 0; i < Spectrum.Length; i++)
            spectrum.GradientStops.Add(new GradientStop(Spectrum[i], i / (double)(Spectrum.Length - 1)));
        content.Children.Add(new Border { Height = 6, Background = spectrum, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 2, 0, 14) });

        var footer = new DockPanel();
        AddButton("应用颜色", true);
        AddButton("取消", false);
        var swatch = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 7, 0),
            Background = selectedBrush, BorderBrush = OverlayWindow.Brush("#545459"), BorderThickness = new Thickness(1)
        };
        DockPanel.SetDock(swatch, Dock.Left);
        footer.Children.Add(swatch);
        footer.Children.Add(hexValue);
        content.Children.Add(footer);
        AutomationProperties.SetName(hexValue, "所选颜色");

        var rgb = System.Drawing.Color.FromArgb(initial.R, initial.G, initial.B);
        hue.Value = rgb.GetHue();
        int max = Math.Max(initial.R, Math.Max(initial.G, initial.B));
        int min = Math.Min(initial.R, Math.Min(initial.G, initial.B));
        saturation = max == 0 ? 0 : (max - min) / (double)max;
        brightness = max / 255d;
        UpdateColor(publish: false);

        hue.ValueChanged += (_, _) => UpdateColor();
        colorField.SizeChanged += (_, _) => PositionMarker();
        colorField.MouseLeftButtonDown += (_, e) =>
        {
            colorField.Focus();
            colorField.CaptureMouse();
            Pick(e.GetPosition(colorField));
            e.Handled = true;
        };
        colorField.MouseMove += (_, e) =>
        {
            if (colorField.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
                Pick(e.GetPosition(colorField));
        };
        colorField.MouseLeftButtonUp += (_, e) =>
        {
            if (!colorField.IsMouseCaptured) return;
            Pick(e.GetPosition(colorField));
            colorField.ReleaseMouseCapture();
        };
        colorField.KeyDown += (_, e) =>
        {
            const double step = 0.01;
            switch (e.Key)
            {
                case Key.Left: saturation = Math.Max(0, saturation - step); break;
                case Key.Right: saturation = Math.Min(1, saturation + step); break;
                case Key.Down: brightness = Math.Max(0, brightness - step); break;
                case Key.Up: brightness = Math.Min(1, brightness + step); break;
                default: return;
            }
            UpdateColor();
            e.Handled = true;
        };

        void AddButton(string label, bool accept)
        {
            var button = new Button
            {
                Content = label, Style = buttonStyle, IsDefault = accept, IsCancel = !accept,
                Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(9, 6, 9, 6)
            };
            if (accept)
            {
                button.Background = OverlayWindow.Brush("#006BD0");
                button.Click += (_, _) => DialogResult = true;
            }
            DockPanel.SetDock(button, Dock.Right);
            footer.Children.Add(button);
        }
    }

    private void Pick(Point point)
    {
        if (colorField.ActualWidth <= 0 || colorField.ActualHeight <= 0) return;
        saturation = Math.Clamp(point.X / colorField.ActualWidth, 0, 1);
        brightness = 1 - Math.Clamp(point.Y / colorField.ActualHeight, 0, 1);
        UpdateColor();
    }

    private void UpdateColor(bool publish = true)
    {
        hueBrush.Color = FromHsv(hue.Value, 1, 1);
        PositionMarker();
        var color = FromHsv(hue.Value, saturation, brightness);
        string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        if (hex == SelectedHex) return;
        selectedBrush.Color = color;
        hexValue.Text = hex;
        if (publish) previewColor(hex);
    }

    private void PositionMarker()
    {
        Canvas.SetLeft(marker, saturation * colorField.ActualWidth - marker.Width / 2);
        Canvas.SetTop(marker, (1 - brightness) * colorField.ActualHeight - marker.Height / 2);
    }

    private static Color FromHsv(double hue, double saturation, double brightness)
    {
        double chroma = brightness * saturation;
        double sector = hue % 360 / 60;
        double middle = chroma * (1 - Math.Abs(sector % 2 - 1));
        double offset = brightness - chroma;
        var (red, green, blue) = sector switch
        {
            < 1 => (chroma, middle, 0d),
            < 2 => (middle, chroma, 0d),
            < 3 => (0d, chroma, middle),
            < 4 => (0d, middle, chroma),
            < 5 => (middle, 0d, chroma),
            _ => (chroma, 0d, middle)
        };
        byte Component(double channel) => (byte)Math.Round((channel + offset) * 255);
        return Color.FromRgb(Component(red), Component(green), Component(blue));
    }
}

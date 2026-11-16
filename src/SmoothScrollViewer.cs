using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace FysZeRewardOverlay;

internal sealed class SmoothScrollViewer : ScrollViewer
{
    private const double DurationMilliseconds = 180;
    private readonly Stopwatch elapsed = new();
    private readonly DispatcherTimer animation;
    private double startOffset;
    private double targetOffset;

    public SmoothScrollViewer()
    {
        animation = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        animation.Tick += (_, _) => AnimateOffset();
        Unloaded += (_, _) => animation.Stop();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            animation.Stop();
            base.OnMouseWheel(e);
            return;
        }

        int lines = SystemParameters.WheelScrollLines;
        double step = lines < 0 ? ViewportHeight : lines * 24d;
        double distance = -e.Delta / 120d * step;
        if (distance == 0 || ScrollableHeight == 0) return;

        startOffset = VerticalOffset;
        // Consecutive ticks extend the motion. A reversal starts from the visible position.
        double origin = animation.IsEnabled && Math.Sign(targetOffset - startOffset) == Math.Sign(distance)
            ? targetOffset : startOffset;
        targetOffset = Math.Clamp(origin + distance, 0, ScrollableHeight);
        e.Handled = true;
        if (targetOffset == startOffset)
        {
            animation.Stop();
            return;
        }
        elapsed.Restart();
        animation.Start();
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        animation.Stop();
        base.OnPreviewMouseDown(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        animation.Stop();
        base.OnPreviewKeyDown(e);
    }

    private void AnimateOffset()
    {
        double progress = Math.Min(1, elapsed.Elapsed.TotalMilliseconds / DurationMilliseconds);
        double remaining = 1 - progress;
        ScrollToVerticalOffset(targetOffset + (startOffset - targetOffset) * remaining * remaining * remaining);
        if (progress == 1) animation.Stop();
    }
}

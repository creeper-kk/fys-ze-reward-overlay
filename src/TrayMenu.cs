using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FysZeRewardOverlay;

internal sealed class TrayMenu : ContextMenuStrip
{
    private static readonly Color Surface = Color.FromArgb(31, 31, 31);
    private static readonly Color Outline = Color.FromArgb(57, 57, 57);
    private static readonly Color Divider = Color.FromArgb(85, 85, 85);
    private readonly Font menuFont = new("Microsoft YaHei UI", 9);

    internal TrayMenu()
    {
        ShowImageMargin = false;
        BackColor = Surface;
        ForeColor = Color.FromArgb(242, 242, 242);
        Font = menuFont;
        Renderer = new DarkRenderer();
        Padding = new Padding(0, 6, 0, 6);
    }

    internal ToolStripMenuItem AddAction(string text, Action action)
    {
        var item = new ToolStripMenuItem(text) { Padding = new Padding(0, 4, 0, 4) };
        item.Click += (_, _) => action();
        Items.Add(item);
        return item;
    }

    internal void AddDivider() => Items.Add(new ToolStripSeparator { Margin = new Padding(0, 4, 0, 4) });

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        int diameter = Math.Min(ClientSize.Height, Math.Min(ClientSize.Width, LogicalToDeviceUnits(16)));
        if (diameter < 2) return;
        using var path = new GraphicsPath();
        int right = ClientSize.Width - diameter;
        int bottom = ClientSize.Height - diameter;
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(right, 0, diameter, diameter, 270, 90);
        path.AddArc(right, bottom, diameter, diameter, 0, 90);
        path.AddArc(0, bottom, diameter, diameter, 90, 90);
        path.CloseFigure();
        Region = new Region(path);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) menuFont.Dispose();
    }

    private sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        internal DarkRenderer() => RoundedEdges = false;

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) =>
            e.Graphics.Clear(Surface);

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Outline);
            var bounds = e.ToolStrip.ClientRectangle;
            e.Graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected) return;
            using var brush = new SolidBrush(Outline);
            var bounds = new Rectangle(Point.Empty, e.Item.Size);
            bounds.Inflate(-4, 0);
            e.Graphics.FillRectangle(brush, bounds);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var bounds = e.TextRectangle;
            bounds.Offset(e.ToolStrip?.LogicalToDeviceUnits(4) ?? 4, 0);
            e.TextRectangle = bounds;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Divider);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 0, y, e.Item.Width, y);
        }
    }
}

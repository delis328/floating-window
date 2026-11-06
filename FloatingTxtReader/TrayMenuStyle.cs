using System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace FloatingTxtReader;

internal static class TrayMenuStyle
{
    internal static readonly System.Drawing.Color Surface = System.Drawing.Color.FromArgb(250, 251, 250);
    private static readonly System.Drawing.Color Text = System.Drawing.Color.FromArgb(47, 57, 51);
    private static readonly System.Drawing.Color Muted = System.Drawing.Color.FromArgb(96, 109, 100);
    private static readonly System.Drawing.Color Hover = System.Drawing.Color.FromArgb(235, 241, 237);
    private static readonly System.Drawing.Color Pressed = System.Drawing.Color.FromArgb(224, 233, 227);
    private static readonly System.Drawing.Color ButtonSurface = System.Drawing.Color.FromArgb(239, 243, 240);
    private static readonly Forms.ToolStripRenderer Renderer = new RoundedMenuRenderer();

    internal static void Apply(Forms.ToolStripDropDownMenu menu)
    {
        menu.ShowImageMargin = false;
        menu.Renderer = Renderer;
        menu.BackColor = Surface;
        menu.ForeColor = Text;
        menu.Padding = new Forms.Padding(10);
        menu.DropShadowEnabled = false;
        menu.SizeChanged += UpdateRegion;
        UpdateRegion(menu, EventArgs.Empty);
    }

    internal static void Apply(Forms.Button button, bool primary)
    {
        button.FlatStyle = Forms.FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.UseVisualStyleBackColor = false;
        button.BackColor = primary ? System.Drawing.Color.FromArgb(229, 238, 232) : ButtonSurface;
        button.ForeColor = primary ? System.Drawing.Color.FromArgb(63, 102, 80) : Text;
        button.FlatAppearance.MouseOverBackColor = Hover;
        button.FlatAppearance.MouseDownBackColor = Pressed;
        button.SizeChanged += UpdateRegion;
        button.Paint += PaintButton;
        UpdateRegion(button, EventArgs.Empty);
    }

    private static void UpdateRegion(object? sender, EventArgs e)
    {
        var control = (Forms.Control)sender!;
        if (control.Width <= 0 || control.Height <= 0) return;
        var radius = (control is Forms.ToolStrip ? 16 : 10) * control.DeviceDpi / 96f;
        using var path = RoundedPath(new System.Drawing.RectangleF(0, 0, control.Width, control.Height), radius);
        var previous = control.Region;
        control.Region = new System.Drawing.Region(path);
        previous?.Dispose();
    }

    private static void PaintButton(object? sender, Forms.PaintEventArgs e)
    {
        var button = (Forms.Button)sender!;
        if (!button.Enabled)
        {
            e.Graphics.Clear(ButtonSurface);
            Forms.TextRenderer.DrawText(e.Graphics, button.Text, button.Font, button.ClientRectangle, Muted,
                Forms.TextFormatFlags.HorizontalCenter | Forms.TextFormatFlags.VerticalCenter | Forms.TextFormatFlags.EndEllipsis);
        }
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var corners = RoundedPath(new System.Drawing.RectangleF(0, 0, button.Width, button.Height), 10 * button.DeviceDpi / 96f);
        corners.AddRectangle(button.ClientRectangle);
        using var surface = new System.Drawing.SolidBrush(Surface);
        e.Graphics.FillPath(surface, corners);
    }

    private static GraphicsPath RoundedPath(System.Drawing.RectangleF bounds, float radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class RoundedMenuRenderer : Forms.ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Surface);
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new System.Drawing.RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using var path = RoundedPath(bounds, 16 * e.ToolStrip.DeviceDpi / 96f);
            using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(225, 232, 227));
            e.Graphics.DrawPath(pen, path);
        }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Enabled || (!e.Item.Selected && !e.Item.Pressed)) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedPath(new System.Drawing.RectangleF(6, 1, e.Item.Width - 12, e.Item.Height - 2), 8 * (e.ToolStrip?.DeviceDpi ?? 96) / 96f);
            using var brush = new System.Drawing.SolidBrush(e.Item.Pressed ? Pressed : Hover);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : Muted;
            var bounds = e.TextRectangle;
            bounds.X += 6;
            bounds.Width -= 6;
            e.TextRectangle = bounds;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Muted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            var inset = 16 * (e.ToolStrip?.DeviceDpi ?? 96) / 96;
            var y = e.Item.Height / 2;
            e.Graphics.SmoothingMode = SmoothingMode.None;
            using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(218, 226, 220));
            e.Graphics.DrawLine(pen, inset, y, e.Item.Width - inset - 1, y);
        }
    }
}

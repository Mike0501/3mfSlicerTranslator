using System.Drawing.Drawing2D;
using static Translator.AppTheme;

namespace Translator;

internal static class RoundedShape
{
    internal static GraphicsPath Create(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RoundedPanel : Panel
{
    private readonly Func<Color> fill;
    private readonly int radius;

    internal RoundedPanel(Func<Color> fill, int radius = 12)
    {
        this.fill = fill;
        this.radius = radius;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    internal Color GetFillColor() => fill();

    protected override void OnPaintBackground(PaintEventArgs args)
    {
        base.OnPaintBackground(args);
        if (ClientSize.Width < 2 || ClientSize.Height < 2)
            return;

        GraphicsState state = args.Graphics.Save();
        args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0.5F, 0.5F, ClientSize.Width - 1, ClientSize.Height - 1);
        using GraphicsPath path = RoundedShape.Create(bounds, LogicalToDeviceUnits(radius));
        using var brush = new SolidBrush(GetFillColor());
        using var border = new Pen(Border);
        args.Graphics.FillPath(brush, path);
        args.Graphics.DrawPath(border, path);
        args.Graphics.Restore(state);
    }
}

internal sealed class RoundedButton : Button
{
    private readonly bool primary;
    private readonly ToolbarIcon? icon;
    private (Color Fill, Color Text)? colors;
    private bool hover;
    private bool pressed;

    internal RoundedButton(bool primary = false, ToolbarIcon? icon = null)
    {
        this.primary = primary;
        this.icon = icon;
        AutoSize = true;
        Padding = new Padding(10, 4, 10, 4);
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
    }

    internal void SetColors(Color fill, Color text)
    {
        colors = (fill, text);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        // Paint every pixel so the native button background cannot show through the rounded corners.
        using var background = new SolidBrush(GetUnderlayColor());
        args.Graphics.FillRectangle(background, ClientRectangle);
        var bounds = new RectangleF(0.5F, 0.5F, ClientSize.Width - 1, ClientSize.Height - 2);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var (fill, text) = Enabled
            ? colors ?? (primary ? (Primary, Color.White) : (AccentLight, Accent))
            : (Input, Muted);
        bool down = Enabled && pressed;
        if (Enabled && hover)
            fill = down ? Mix(fill, Color.Black, 0.12F) : Mix(fill, Color.White, 0.10F);
        if (down)
            bounds.Offset(0, 1);

        GraphicsState state = args.Graphics.Save();
        args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float radius = LogicalToDeviceUnits(9);
        using GraphicsPath shape = RoundedShape.Create(bounds, radius);
        Color top = Mix(fill, Color.White, Enabled ? 0.11F : 0.02F);
        Color bottom = Mix(fill, Color.Black, Enabled ? 0.07F : 0.02F);
        using var shading = new LinearGradientBrush(bounds, top, bottom, LinearGradientMode.Vertical);
        using var border = new Pen(primary && Enabled ? Mix(fill, Color.Black, 0.13F) : Mix(fill, Border, 0.55F));
        args.Graphics.FillPath(shading, shape);
        args.Graphics.DrawPath(border, shape);
        if (Focused && ShowFocusCues && bounds.Width > 6 && bounds.Height > 6)
        {
            using var focus = new Pen(text) { DashStyle = DashStyle.Dot };
            using GraphicsPath focusShape = RoundedShape.Create(RectangleF.Inflate(bounds, -2, -2), radius - 2);
            args.Graphics.DrawPath(focus, focusShape);
        }
        args.Graphics.Restore(state);

        Rectangle content = Rectangle.Round(bounds);
        if (icon is ToolbarIcon symbol)
        {
            ToolbarIcons.Draw(args.Graphics, content, symbol, text);
            return;
        }

        content = new Rectangle(content.X + Padding.Left, content.Y + Padding.Top,
            content.Width - Padding.Horizontal, content.Height - Padding.Vertical);
        TextFormatFlags alignment = TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
        if (content.Width > 0 && content.Height > 0)
            TextRenderer.DrawText(args.Graphics, Text, Font, content, text,
                alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    private Color GetUnderlayColor()
    {
        for (Control? container = Parent; container is not null; container = container.Parent)
        {
            if (container is RoundedPanel panel)
                return panel.GetFillColor();
            if (container.BackColor.A == 255)
                return container.BackColor;
        }
        return Canvas;
    }

    protected override void OnMouseEnter(EventArgs args)
    {
        base.OnMouseEnter(args);
        hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs args)
    {
        base.OnMouseLeave(args);
        hover = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        base.OnMouseDown(args);
        if (args.Button == MouseButtons.Left)
            SetPressed(true);
    }

    protected override void OnMouseCaptureChanged(EventArgs args)
    {
        base.OnMouseCaptureChanged(args);
        if (!Capture)
            SetPressed(false);
    }

    protected override void OnMouseUp(MouseEventArgs args)
    {
        SetPressed(false);
        base.OnMouseUp(args);
    }

    private void SetPressed(bool value)
    {
        pressed = value;
        Invalidate();
    }

    private static Color Mix(Color first, Color second, float weight) => Color.FromArgb(
        (int)Math.Round(first.R + (second.R - first.R) * weight),
        (int)Math.Round(first.G + (second.G - first.G) * weight),
        (int)Math.Round(first.B + (second.B - first.B) * weight));
}

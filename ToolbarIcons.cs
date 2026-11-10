using System.Drawing.Drawing2D;

namespace Translator;

internal enum ToolbarIcon
{
    Moon,
    Sun,
    Help
}

internal static class ToolbarIcons
{
    internal static void Draw(Graphics graphics, Rectangle bounds, ToolbarIcon icon, Color color)
    {
        float size = Math.Min(bounds.Width, bounds.Height) * 0.7F;
        GraphicsState state = graphics.Save();
        graphics.TranslateTransform(bounds.X + (bounds.Width - size) / 2, bounds.Y + (bounds.Height - size) / 2);
        graphics.ScaleTransform(size / 24, size / 24);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.8F) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        switch (icon)
        {
            case ToolbarIcon.Moon:
                using (var moon = new GraphicsPath())
                {
                    moon.AddBezier(12, 3, 7, 3, 3, 7, 3, 12);
                    moon.AddBezier(3, 12, 3, 21, 15.5F, 24.2F, 20, 15.5F);
                    moon.AddBezier(20, 15.5F, 11.8F, 17.4F, 7.5F, 9.4F, 12, 3);
                    moon.CloseFigure();
                    graphics.DrawPath(pen, moon);
                }
                break;
            case ToolbarIcon.Sun:
                graphics.DrawEllipse(pen, 7.5F, 7.5F, 9, 9);
                for (int ray = 0; ray < 8; ray++)
                {
                    double angle = ray * Math.PI / 4;
                    float x = (float)Math.Cos(angle);
                    float y = (float)Math.Sin(angle);
                    graphics.DrawLine(pen, 12 + 7.5F * x, 12 + 7.5F * y, 12 + 10 * x, 12 + 10 * y);
                }
                break;
            case ToolbarIcon.Help:
                graphics.DrawEllipse(pen, 2.5F, 2.5F, 19, 19);
                graphics.DrawBezier(pen, 9.5F, 9.3F, 10, 6, 15.3F, 6.6F, 14.9F, 9.7F);
                graphics.DrawBezier(pen, 14.9F, 9.7F, 14.7F, 11.3F, 12, 11.5F, 12, 13.5F);
                using (var dot = new SolidBrush(color))
                    graphics.FillEllipse(dot, 11, 15.6F, 2, 2);
                break;
        }
        graphics.Restore(state);
    }
}

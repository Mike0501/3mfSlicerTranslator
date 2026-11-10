using System.Drawing.Drawing2D;

namespace Translator;

internal sealed class LanguageSelector : ComboBox
{
    internal const int ItalianIndex = 1;

    internal LanguageSelector()
    {
        AccessibleName = T("Language", "Lingua");
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        Width = 123;
        ItemHeight = Font.Height + 6;
        Items.AddRange(["English", "Italiano"]);
        SelectedIndex = IsItalian ? ItalianIndex : 0;
    }

    protected override void OnFontChanged(EventArgs args)
    {
        base.OnFontChanged(args);
        ItemHeight = Font.Height + 6;
    }

    protected override void OnDrawItem(DrawItemEventArgs args)
    {
        if (args.Index < 0)
            return;

        args.DrawBackground();
        bool italian = args.Index == ItalianIndex;
        int flagWidth = (int)Math.Round(Font.Height * 1.55);
        int flagHeight = italian ? flagWidth * 2 / 3 : flagWidth / 2;
        var flag = new Rectangle(args.Bounds.Left + 5, args.Bounds.Top + (args.Bounds.Height - flagHeight) / 2,
            flagWidth, flagHeight);
        if (italian)
            DrawItalianFlag(args.Graphics, flag);
        else
            DrawBritishFlag(args.Graphics, flag);

        int textLeft = flag.Right + 6;
        var text = new Rectangle(textLeft, args.Bounds.Top, args.Bounds.Right - textLeft, args.Bounds.Height);
        TextRenderer.DrawText(args.Graphics, GetItemText(Items[args.Index]), Font, text, args.ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        args.DrawFocusRectangle();
    }

    private static void DrawItalianFlag(Graphics graphics, Rectangle flag)
    {
        int stripe = flag.Width / 3;
        using var green = new SolidBrush(Color.FromArgb(0, 146, 70));
        using var red = new SolidBrush(Color.FromArgb(206, 43, 55));
        graphics.FillRectangle(Brushes.White, flag);
        graphics.FillRectangle(green, flag.X, flag.Y, stripe, flag.Height);
        graphics.FillRectangle(red, flag.Right - stripe, flag.Y, stripe, flag.Height);
        graphics.DrawRectangle(SystemPens.ControlDark, flag.X, flag.Y, flag.Width - 1, flag.Height - 1);
    }

    private static void DrawBritishFlag(Graphics graphics, Rectangle flag)
    {
        using var blue = new SolidBrush(Color.FromArgb(1, 33, 105));
        using var red = new SolidBrush(Color.FromArgb(200, 16, 46));
        using var white = new Pen(Color.White, 6);
        GraphicsState state = graphics.Save();
        graphics.SetClip(flag, CombineMode.Intersect);
        graphics.TranslateTransform(flag.Left, flag.Top);
        graphics.ScaleTransform(flag.Width / 60F, flag.Height / 30F);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.FillRectangle(blue, 0, 0, 60, 30);
        graphics.DrawLine(white, 0, 0, 60, 30);
        graphics.DrawLine(white, 0, 30, 60, 0);
        graphics.FillPolygon(red, [new PointF(0, 0), new(30, 15), new(25.5F, 15), new(0, 2.25F)]);
        graphics.FillPolygon(red, [new PointF(60, 30), new(30, 15), new(34.5F, 15), new(60, 27.75F)]);
        graphics.FillPolygon(red, [new PointF(60, 0), new(30, 15), new(30, 12.75F), new(55.5F, 0)]);
        graphics.FillPolygon(red, [new PointF(0, 30), new(30, 15), new(30, 17.25F), new(4.5F, 30)]);
        graphics.FillRectangle(Brushes.White, 25, 0, 10, 30);
        graphics.FillRectangle(Brushes.White, 0, 10, 60, 10);
        graphics.FillRectangle(red, 27, 0, 6, 30);
        graphics.FillRectangle(red, 0, 12, 60, 6);
        graphics.Restore(state);
        graphics.DrawRectangle(SystemPens.ControlDark, flag.X, flag.Y, flag.Width - 1, flag.Height - 1);
    }
}

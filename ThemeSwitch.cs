using System.ComponentModel;
using System.Drawing.Drawing2D;
using static Translator.AppTheme;

namespace Translator;

internal sealed class ThemeSwitch : Control
{
    private bool isChecked = Dark;

    internal event EventHandler? CheckedChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Checked
    {
        get => isChecked;
        set
        {
            if (isChecked == value)
                return;

            isChecked = value;
            Invalidate();
            if (IsHandleCreated)
                AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal ThemeSwitch()
    {
        AccessibleName = T("Dark mode", "Tema scuro");
        Size = new Size(60, 30);
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque | ControlStyles.Selectable, true);
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        if (args.Button == MouseButtons.Left)
            Focus();
        base.OnMouseDown(args);
    }

    protected override void OnClick(EventArgs args)
    {
        if (Enabled && args is not MouseEventArgs { Button: not MouseButtons.Left })
            Checked = !Checked;
        base.OnClick(args);
    }

    protected override void OnKeyDown(KeyEventArgs args)
    {
        base.OnKeyDown(args);
        if (args.KeyData == Keys.Space)
            args.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs args)
    {
        base.OnKeyUp(args);
        if (args.KeyData == Keys.Space)
        {
            args.Handled = true;
            OnClick(EventArgs.Empty);
        }
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new SwitchAccessibleObject(this);

    private sealed class SwitchAccessibleObject(ThemeSwitch owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.CheckButton;
        public override AccessibleStates State => base.State | (owner.Checked ? AccessibleStates.Checked : AccessibleStates.None);
        public override string DefaultAction => owner.Checked ? T("Turn off", "Disattiva") : T("Turn on", "Attiva");
        public override void DoDefaultAction() => owner.OnClick(EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        Graphics graphics = args.Graphics;
        graphics.Clear(Navy);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int margin = Math.Max(2, Height / 10);
        var track = Rectangle.Inflate(ClientRectangle, -margin, -margin);
        if (track.Height < 8 || track.Width <= track.Height)
            return;

        using var outline = new GraphicsPath();
        outline.AddArc(track.Left, track.Top, track.Height, track.Height, 90, 180);
        outline.AddArc(track.Right - track.Height, track.Top, track.Height, track.Height, 270, 180);
        outline.CloseFigure();
        using var fill = new SolidBrush(Checked ? Primary : Color.FromArgb(52, 68, 103));
        graphics.FillPath(fill, outline);

        int inset = Math.Max(2, track.Height / 10);
        int diameter = track.Height - 2 * inset;
        int thumbX = Checked ? track.Right - diameter - inset : track.Left + inset;
        var thumb = new Rectangle(thumbX, track.Top + inset, diameter, diameter);
        var other = new Rectangle(Checked ? track.Left + inset : track.Right - diameter - inset, thumb.Top, diameter, diameter);
        using var thumbFill = new SolidBrush(Enabled ? Color.FromArgb(238, 243, 255) : Color.FromArgb(184, 193, 213));
        graphics.FillEllipse(thumbFill, thumb);
        ToolbarIcons.Draw(graphics, thumb, Checked ? ToolbarIcon.Moon : ToolbarIcon.Sun, Color.FromArgb(28, 42, 78));
        ToolbarIcons.Draw(graphics, other, Checked ? ToolbarIcon.Sun : ToolbarIcon.Moon, Color.FromArgb(212, 223, 248));
        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(Color.White) { DashStyle = DashStyle.Dot };
            graphics.DrawPath(focus, outline);
        }
    }
}

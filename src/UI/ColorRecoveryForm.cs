using static Translator.AppTheme;

namespace Translator;

internal sealed class ColorRecoveryForm : Form
{
    internal ColorRecoveryForm(IReadOnlyList<int> invalidSlots)
    {
        Text = T("Invalid filament colors · 3MF Translator", "Colori filamento non validi · 3MF Translator");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 10F);
        BackColor = Canvas;
        ForeColor = Ink;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Padding = new Padding(20)
        };
        layout.Controls.Add(new Label
        {
            Text = T("The source project contains invalid filament colors.", "Il progetto di origine contiene colori filamento non validi."),
            Font = new Font("Segoe UI Semibold", 14F), AutoSize = true, Margin = new Padding(0, 0, 0, 11)
        });

        var warning = new RoundedPanel(() => ConversionDetailsForm.WarningColors.Surface)
        {
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 14)
        };
        warning.Controls.Add(new Label
        {
            Text = T($"Invalid color in source slot(s) {string.Join(", ", invalidSlots)}. Replace {(invalidSlots.Count == 1 ? "it" : "them")} with black (#000000) and continue?",
                $"Colore non valido negli slot di origine {string.Join(", ", invalidSlots)}. Vuoi sostituir{(invalidSlots.Count == 1 ? "lo" : "li")} con il nero (#000000) e continuare?"),
            AutoSize = true, MaximumSize = new Size(510, 0), ForeColor = ConversionDetailsForm.WarningColors.Ink
        });
        layout.Controls.Add(warning);

        var replace = new RoundedButton(primary: true)
        {
            Text = T("Replace with black", "Sostituisci con nero"), AutoSize = true, DialogResult = DialogResult.OK,
            Margin = new Padding(0, 0, 8, 0)
        };
        var cancel = new RoundedButton
        {
            Text = T("Cancel conversion", "Annulla conversione"), AutoSize = true, DialogResult = DialogResult.Cancel
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false
        };
        actions.Controls.Add(cancel);
        actions.Controls.Add(replace);
        layout.Controls.Add(actions);
        Controls.Add(layout);
        AcceptButton = replace;
        CancelButton = cancel;
    }
}

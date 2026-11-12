using static Translator.AppTheme;

namespace Translator;

internal sealed class DeleteProfileForm : Form
{
    internal DeleteProfileForm(string name)
    {
        Text = T("Delete profile · 3MF Translator", "Elimina profilo · 3MF Translator");
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
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Padding = new Padding(20) };
        layout.Controls.Add(new Label
        {
            Text = T("Delete this saved profile?", "Eliminare questo profilo salvato?"),
            AutoSize = true, Font = new Font("Segoe UI Semibold", 15F), Margin = new Padding(0, 0, 0, 12)
        });
        var card = new RoundedPanel(() => ConversionDetailsForm.WarningColors.Surface)
        {
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 14)
        };
        card.Controls.Add(new Label
        {
            Text = T($"“{name}” will be removed from the profile library.", $"“{name}” verrà rimosso dalla libreria profili."),
            AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = ConversionDetailsForm.WarningColors.Ink
        });
        layout.Controls.Add(card);
        var remove = new RoundedButton { Text = T("Delete profile", "Elimina profilo"), AutoSize = true, DialogResult = DialogResult.OK };
        remove.SetColors(ConversionDetailsForm.ErrorColors.Band, ConversionDetailsForm.ErrorColors.Ink);
        var cancel = new RoundedButton { Text = T("Cancel", "Annulla"), AutoSize = true, DialogResult = DialogResult.Cancel };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.AddRange([remove, cancel]);
        layout.Controls.Add(actions);
        Controls.Add(layout);
        AcceptButton = cancel;
        CancelButton = cancel;
        ActiveControl = cancel;
    }
}

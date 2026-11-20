using static Translator.AppTheme;

namespace Translator;

internal sealed class PlatePlacementForm : Form
{
    internal PlatePlacementForm(PlacementReview review)
    {
        Text = T("Object positions · 3MF Translator", "Posizioni degli oggetti · 3MF Translator");
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

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Padding = new Padding(18) };
        layout.Controls.Add(new Label
        {
            Text = T("Check positions on the destination plates", "Controlla le posizioni sui piatti di destinazione"),
            Font = new Font("Segoe UI Semibold", 15F), AutoSize = true, Margin = new Padding(0, 0, 0, 12)
        });

        var messages = new List<string>();
        if (review.Adjustable.Count > 0)
        {
            string plates = string.Join(", ", review.Adjustable);
            messages.Add(T($"Plate(s) {plates} need an adjustment. The app can move the objects together on each plate and keep your chosen print settings.",
                $"I piatti {plates} richiedono un adattamento. L’app può spostare insieme gli oggetti su ciascun piatto e mantenere le impostazioni di stampa che hai scelto."));
        }
        if (review.Manual.Count > 0)
        {
            string plates = string.Join(", ", review.Manual);
            messages.Add(T($"Plate(s) {plates} need manual arrangement in the destination slicer.",
                $"I piatti {plates} vanno sistemati manualmente nello slicer di destinazione."));
        }
        if (review.Unverified)
            messages.Add(T("The build plate differs and the app could not verify the object positions. Check them in the destination slicer.",
                "Il piatto di stampa è diverso e l’app non ha potuto verificare le posizioni. Controllale nello slicer di destinazione."));

        var card = new RoundedPanel(() => ConversionDetailsForm.WarningColors.Surface)
        {
            Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 12)
        };
        card.Controls.Add(new Label
        {
            Text = string.Join(Environment.NewLine + Environment.NewLine, messages), AutoSize = true,
            MaximumSize = new Size(475, 0), ForeColor = ConversionDetailsForm.WarningColors.Ink
        });
        layout.Controls.Add(card);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false
        };
        var keep = new RoundedButton(primary: review.Adjustable.Count == 0)
        {
            Text = T("Keep positions", "Mantieni posizioni"), AutoSize = true, DialogResult = DialogResult.No
        };
        var cancel = new RoundedButton { Text = T("Cancel", "Annulla"), AutoSize = true, DialogResult = DialogResult.Cancel };
        if (review.Adjustable.Count > 0)
        {
            var adjust = new RoundedButton(primary: true)
            {
                Text = T("Adjust positions", "Adatta posizioni"), AutoSize = true, DialogResult = DialogResult.Yes
            };
            actions.Controls.Add(adjust);
            AcceptButton = adjust;
        }
        else
            AcceptButton = keep;
        actions.Controls.AddRange([keep, cancel]);
        layout.Controls.Add(actions);
        Controls.Add(layout);
        CancelButton = cancel;
    }
}

using static Translator.AppTheme;

namespace Translator;

internal enum SettingChoice
{
    SourceObject,
    Source,
    Destination,
    Manual
}

internal sealed class SettingChoiceForm : Form
{
    internal SettingChoice Selection { get; private set; }

    internal SettingChoiceForm()
    {
        Text = T("Print settings · 3MF Translator", "Impostazioni di stampa · 3MF Translator");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 10F);
        BackColor = Canvas;
        ForeColor = Ink;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Padding = new Padding(18)
        };
        layout.Controls.Add(new Label
        {
            Text = T("Which print settings should we use?", "Quali impostazioni di stampa vuoi usare?"),
            Font = new Font("Segoe UI Semibold", 15F), AutoSize = true, Margin = new Padding(0, 0, 0, 14)
        });
        AddChoice(layout, SettingChoice.SourceObject, T("Keep source object settings", "Mantieni le impostazioni del pezzo dall’origine"),
            T("Use source walls, layers, infill, seam and supports.\nKeep destination speeds, flow, dimensional corrections and print order.",
                "Usa pareti, strati, riempimento, cucitura e supporti dell’origine.\nMantieni velocità, flusso, compensazioni e ordine di stampa della destinazione."));
        AddChoice(layout, SettingChoice.Source, T("Use all compatible source settings", "Usa tutte le impostazioni compatibili di origine"),
            T("Include compatible process speeds, calibrations and overrides.", "Includi velocità di stampa, calibrazioni e impostazioni specifiche compatibili."));
        AddChoice(layout, SettingChoice.Destination, T("Keep destination object settings", "Mantieni le impostazioni del pezzo dalla destinazione"),
            T("Start with the destination settings and edit only the settings for the part.",
                "Parti dalle impostazioni di destinazione e modifica solo quelle del pezzo."));
        AddChoice(layout, SettingChoice.Manual, T("Review manually…", "Rivedi manualmente…"),
            T("Start with source object settings and destination calibrations. Adjust values individually.",
                "Parti dalle impostazioni del pezzo di origine e dalle calibrazioni di destinazione. Scegli i valori voce per voce."));

        var cancel = new RoundedButton
        {
            Text = T("Cancel", "Annulla"), AutoSize = true, DialogResult = DialogResult.Cancel,
            Anchor = AnchorStyles.Right, Margin = new Padding(0, 5, 0, 0)
        };
        layout.Controls.Add(cancel);
        CancelButton = cancel;
        Controls.Add(layout);
    }

    private void AddChoice(TableLayoutPanel layout, SettingChoice choice, string title, string description)
    {
        var button = new RoundedButton
        {
            Text = title + Environment.NewLine + description, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill, Padding = new Padding(12, 7, 12, 7), Margin = new Padding(0, 0, 0, 8)
        };
        button.SetColors(Surface, Ink);
        button.Click += (_, _) =>
        {
            Selection = choice;
            DialogResult = DialogResult.OK;
        };
        layout.Controls.Add(button);
    }
}

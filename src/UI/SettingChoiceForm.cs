using static Translator.AppTheme;

namespace Translator;

internal enum SettingChoice
{
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
        AddChoice(layout, SettingChoice.Source, T("Use source settings", "Usa impostazioni di origine"),
            T("Apply compatible values from the source project.", "Applica i valori compatibili del progetto di origine."));
        AddChoice(layout, SettingChoice.Destination, T("Keep destination settings", "Mantieni impostazioni di destinazione"),
            T("Use the selected destination print settings.", "Usa le impostazioni di stampa della destinazione scelta."));
        AddChoice(layout, SettingChoice.Manual, T("Review manually…", "Rivedi manualmente…"),
            T("Compare the values and choose one setting at a time.", "Confronta i valori e scegli un’impostazione alla volta."));

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

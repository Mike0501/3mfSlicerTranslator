using static Translator.AppTheme;

namespace Translator;

internal sealed class LargeModelForm : Form
{
    private readonly NumericUpDown limit;
    internal long SelectedLimit => (long)limit.Value * ProjectArchive.MiB;

    internal LargeModelForm(LargeModelRequest request)
    {
        Text = T("Large 3MF models · 3MF Translator", "Modelli 3MF grandi · 3MF Translator");
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);
        BackColor = Canvas;
        ForeColor = Ink;
        ClientSize = new Size(650, 440);
        MinimumSize = new Size(570, 450);
        MinimizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = T("Increase the model read limit?", "Aumentare il limite di lettura dei modelli?"),
            AutoSize = true, Font = new Font("Segoe UI Semibold", 15F), Margin = new Padding(0, 0, 0, 12)
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = T("Some models exceed the default 128 MiB read limit. Choose a higher limit for this conversion. Loading them may require several times their size in memory.",
                "Alcuni modelli superano il limite di lettura predefinito di 128 MiB. Scegli un limite maggiore per questa conversione. Il caricamento può richiedere una quantità di memoria diverse volte superiore alla loro dimensione."),
            AutoSize = true, MaximumSize = new Size(590, 0), ForeColor = Muted, Margin = new Padding(0, 0, 0, 12)
        }, 0, 1);
        var panel = new RoundedPanel(() => ConversionDetailsForm.WarningColors.Surface)
        {
            Dock = DockStyle.Fill, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 12)
        };
        panel.Controls.Add(new RichTextBox
        {
            Text = request.ArchiveName + Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine, request.Parts.Select(part => $"{part.Name} · {(double)part.Bytes / ProjectArchive.MiB:F1} MiB")),
            Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, DetectUrls = false,
            BackColor = ConversionDetailsForm.WarningColors.Surface, ForeColor = ConversionDetailsForm.WarningColors.Ink,
            AccessibleName = T("Models exceeding the read limit", "Modelli oltre il limite di lettura")
        });
        layout.Controls.Add(panel, 0, 2);
        var setting = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 14) };
        setting.Controls.Add(new Label
        {
            Text = T("Maximum per model (MiB)", "Massimo per modello (MiB)"), AutoSize = true, Margin = new Padding(0, 5, 10, 0)
        });
        limit = new NumericUpDown
        {
            Minimum = request.RequiredMiB, Maximum = ProjectArchive.MaximumModelSizeLimit / ProjectArchive.MiB,
            Value = request.RequiredMiB, Width = 90, BackColor = Input, ForeColor = Ink,
            AccessibleName = T("Maximum model read size in MiB", "Dimensione massima di lettura modello in MiB")
        };
        setting.Controls.Add(limit);
        layout.Controls.Add(setting, 0, 3);
        var proceed = new RoundedButton(primary: true)
        {
            Text = T("Apply and continue", "Applica e continua"), AutoSize = true, DialogResult = DialogResult.OK
        };
        var cancel = new RoundedButton
        {
            Text = T("Cancel conversion", "Annulla conversione"), AutoSize = true, DialogResult = DialogResult.Cancel
        };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.AddRange([proceed, cancel]);
        layout.Controls.Add(actions, 0, 4);
        Controls.Add(layout);
        AcceptButton = proceed;
        CancelButton = cancel;
    }
}

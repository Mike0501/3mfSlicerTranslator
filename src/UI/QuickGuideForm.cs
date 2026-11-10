using static Translator.AppTheme;

namespace Translator;

internal sealed class QuickGuideForm : Form
{
    internal QuickGuideForm()
    {
        Text = T("Quick guide · 3MF Translator", "Guida rapida · 3MF Translator");
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);
        BackColor = Canvas;
        ForeColor = Ink;
        ClientSize = new Size(700, 550);
        MinimumSize = new Size(520, 410);
        ShowInTaskbar = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(20) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = T("Quick guide", "Guida rapida"), Font = new Font("Segoe UI Semibold", 20F),
            AutoSize = true, Margin = Padding.Empty
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = T("From a downloaded 3MF to a project for your slicer.", "Da un 3MF scaricato a un progetto per il tuo slicer."),
            AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 4, 0, 14)
        }, 0, 1);

        var guide = new RichTextBox
        {
            AccessibleName = T("Quick guide instructions", "Istruzioni della guida rapida"),
            Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, BorderStyle = BorderStyle.None,
            BackColor = Input, ForeColor = Ink, ScrollBars = RichTextBoxScrollBars.Vertical
        };
        using var heading = new Font("Segoe UI Semibold", 11F);
        AddStep("1", T("Open the source project", "Apri il progetto di origine"),
            T("Click Browse… and choose the downloaded 3MF. Use its active print settings or select an embedded preset if one is available.",
              "Premi Sfoglia… e scegli il 3MF scaricato. Usa le impostazioni di stampa attive oppure seleziona un preset incorporato, se disponibile."));
        AddStep("2", T("Choose the destination", "Scegli la destinazione"),
            T("Use Create profile… → New profile… to choose a machine from a slicer catalog and save it as a destination. To reuse a project saved in a slicer, use From a 3MF reference… in the library or Reference 3MF… in the main window. Use Customize… to save a print preset.",
              "Usa Crea profilo… → Nuovo profilo… per scegliere una macchina dal catalogo di uno slicer e salvarla come destinazione. Per riusare un progetto salvato nello slicer, usa Da un riferimento 3MF… in libreria oppure Riferimento 3MF… nella finestra principale. Usa Personalizza… per salvare un preset di stampa."));
        AddStep("3", T("Choose which settings to keep", "Decidi quali impostazioni tenere"),
            T("Click Translate and save… and choose a new file name. If print settings differ, use compatible source values, keep destination values, or open the manual review to choose them individually.",
              "Premi Traduci e salva… e scegli un nuovo nome. Se le impostazioni di stampa differiscono, usa i valori compatibili di origine, mantieni quelli di destinazione oppure apri la revisione manuale e scegli voce per voce."));
        AddStep("4", T("Check the result", "Controlla il risultato"),
            T("Open Result details… to see errors in red, warnings in yellow and completed operations in green. Open the translated 3MF as a project in the destination slicer, slice it and check the preview before printing.",
              "Apri Dettagli esito… per vedere gli errori in rosso, gli avvisi in giallo e le operazioni riuscite in verde. Apri il 3MF tradotto come progetto nello slicer di destinazione, esegui lo slicing e controlla l’anteprima prima di stampare."));
        AddStep("5", T("Supported slicers", "Slicer supportati"),
            T("Bambu Studio, OrcaSlicer and Anycubic Slicer Next can be used as source or destination. Compatible PrusaSlicer 2.x and SuperSlicer projects can be used as source. Choose a destination with one physical nozzle and match the source nozzle diameter.",
              "Bambu Studio, OrcaSlicer e Anycubic Slicer Next possono essere usati come origine o destinazione. I progetti compatibili di PrusaSlicer 2.x e SuperSlicer possono essere usati come origine. Scegli una destinazione con un solo ugello fisico dello stesso diametro di quello di origine."));
        guide.Select(0, 0);
        guide.ScrollToCaret();
        var inset = new RoundedPanel(() => Input) { Dock = DockStyle.Fill, Padding = new Padding(14), Margin = Padding.Empty };
        inset.Controls.Add(guide);
        layout.Controls.Add(inset, 0, 2);

        var close = new RoundedButton
        {
            Text = T("Close", "Chiudi"), AutoSize = true, Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 14, 0, 0), DialogResult = DialogResult.Cancel
        };
        layout.Controls.Add(close, 0, 3);
        AcceptButton = close;
        CancelButton = close;
        Controls.Add(layout);

        void AddStep(string number, string title, string instructions)
        {
            guide.Select(guide.TextLength, 0);
            guide.SelectionFont = heading;
            guide.SelectionColor = Accent;
            guide.AppendText($"{number}. {title}{Environment.NewLine}");
            guide.Select(guide.TextLength, 0);
            guide.SelectionFont = Font;
            guide.SelectionColor = Ink;
            guide.AppendText(instructions + Environment.NewLine + Environment.NewLine);
        }
    }
}

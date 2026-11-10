using static Translator.AppTheme;

namespace Translator;

internal sealed record ConversionDetails(string Context, IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> Successes)
{
    internal static ConversionDetails Completed(string outputPath, ConversionResult result)
    {
        var successes = new List<string>
        {
            T($"Project saved to {outputPath}", $"Progetto salvato in {outputPath}"),
            T($"Destination profile applied · {result.Printer}", $"Profilo di destinazione applicato · {result.Printer}"),
            T($"Plates in the project · {result.Plates}", $"Piatti nel progetto · {result.Plates}"),
            T($"Filament slots imported · {result.Colors}", $"Slot filamento importati · {result.Colors}")
        };
        if (result.Settings.Count > 0)
            successes.Add(T($"Process settings transferred · {result.Settings.Count}", $"Impostazioni di processo trasferite · {result.Settings.Count}"));
        if (result.RetainedSettings.Count > 0)
            successes.Add(T($"Destination settings kept by choice · {result.RetainedSettings.Count}",
                $"Impostazioni di destinazione mantenute per scelta · {result.RetainedSettings.Count}"));
        if (result.LayerHeight is string height)
            successes.Add(T($"Base layer height in the project · {height} mm", $"Altezza strato di base nel progetto · {height.Replace('.', ',')} mm"));
        if (result.LayerChanges > 0)
            successes.Add(T($"Layer filament changes transferred · {result.LayerChanges}", $"Cambi filamento per layer trasferiti · {result.LayerChanges}"));
        return new ConversionDetails(Path.GetFileName(outputPath), [], result.Warnings, successes);
    }

    internal static ConversionDetails Failed(string context, string error) => new(context, [error], [], []);
}

internal sealed class ConversionDetailsForm : Form
{
    internal static (Color Ink, Color Band, Color Surface) ErrorColors =>
        (Shade(0x92223A, 0xFFC0CC), Shade(0xFCE5EB, 0x51283B), Shade(0xFFF5F7, 0x2F202F));
    internal static (Color Ink, Color Band, Color Surface) WarningColors =>
        (Shade(0x694803, 0xFFE0A0), Shade(0xFFEFB4, 0x4E3D25), Shade(0xFFFAE7, 0x2E2A26));
    internal static (Color Ink, Color Band, Color Surface) SuccessColors =>
        (Shade(0x176641, 0xAEEBCB), Shade(0xD7F1E0, 0x244539), Shade(0xF0FAF4, 0x1F302E));

    internal ConversionDetailsForm(ConversionDetails details)
    {
        Text = T("Result details · 3MF Translator", "Dettagli esito · 3MF Translator");
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);
        BackColor = Canvas;
        ForeColor = Ink;
        ClientSize = new Size(740, 610);
        MinimumSize = new Size(520, 495);
        ShowInTaskbar = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 89));
        AddSection(layout, 1, T("Errors", "Errori"), T("No errors found.", "Nessun errore rilevato."), details.Errors, ErrorColors);
        AddSection(layout, 2, T("To review", "Da verificare"), T("No warnings found.", "Nessun avviso rilevato."), details.Warnings, WarningColors);
        AddSection(layout, 3, T("Completed actions", "Operazioni riuscite"), T("No actions completed.", "Nessuna operazione completata."), details.Successes, SuccessColors);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new Panel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(23, 14, 23, 13),
            BackColor = Navy
        };
        var title = new Label
        {
            Text = T("Result details", "Dettagli esito"), Font = new Font("Segoe UI Semibold", 17F),
            ForeColor = Color.White, Dock = DockStyle.Top, Height = 35
        };
        var context = new Label
        {
            Text = details.Context, ForeColor = Color.FromArgb(207, 220, 248), AutoEllipsis = true,
            Dock = DockStyle.Fill
        };
        header.Controls.Add(context);
        header.Controls.Add(title);
        layout.Controls.Add(header, 0, 0);

        var close = new RoundedButton
        {
            Text = T("Close", "Chiudi"), AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(18, 4, 18, 13),
            DialogResult = DialogResult.Cancel
        };
        layout.Controls.Add(close, 0, 4);
        AcceptButton = close;
        CancelButton = close;
        Controls.Add(layout);
    }

    private static void AddSection(TableLayoutPanel layout, int row, string title, string empty,
        IReadOnlyList<string> items, (Color Ink, Color Band, Color Surface) colors)
    {
        layout.RowStyles.Add(items.Count == 0
            ? new RowStyle(SizeType.Absolute, 76)
            : new RowStyle(SizeType.Percent, Math.Min(items.Count, 6) + 2));
        var panel = new RoundedPanel(() => colors.Surface)
        {
            Dock = DockStyle.Fill, Margin = new Padding(18, 10, 18, 0), Padding = new Padding(5)
        };
        var heading = new Label
        {
            Text = $"{title}  ·  {items.Count}", ForeColor = colors.Ink, BackColor = colors.Band,
            Font = new Font("Segoe UI Semibold", 10F), Dock = DockStyle.Top,
            Height = 29, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0)
        };
        var body = new Panel { BackColor = colors.Surface, Dock = DockStyle.Fill, Padding = new Padding(11, 8, 11, 6) };
        var text = new RichTextBox
        {
            AccessibleName = title,
            Text = items.Count == 0 ? empty : string.Join(Environment.NewLine + Environment.NewLine, items.Select(item => "• " + item)),
            ReadOnly = true, DetectUrls = false, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
            ForeColor = colors.Ink, BackColor = colors.Surface, ScrollBars = RichTextBoxScrollBars.Vertical
        };
        body.Controls.Add(text);
        panel.Controls.Add(body);
        panel.Controls.Add(heading);
        layout.Controls.Add(panel, 0, row);
    }
}

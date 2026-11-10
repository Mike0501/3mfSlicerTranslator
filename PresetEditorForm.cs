using static Translator.AppTheme;

namespace Translator;

internal sealed class PresetEditorForm : Form
{
    private readonly TextBox name = new() { Dock = DockStyle.Fill, MaxLength = 60 };
    private readonly NumericUpDown layer;
    private readonly NumericUpDown walls;
    private readonly NumericUpDown infill;
    private readonly CheckBox supports;

    internal string PresetName => name.Text.Trim();
    internal PrintPreset Values => new(layer.Enabled ? layer.Value : null, walls.Enabled ? (int)walls.Value : null,
        infill.Enabled ? infill.Value : null, supports.Enabled ? supports.Checked : null);

    internal PresetEditorForm(DestinationProfile profile, PrintPreset values, LayerHeightRange range, IReadOnlySet<string> usedNames)
    {
        Text = T("Customize print preset", "Personalizza preset di stampa");
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Font = new Font("Segoe UI", 10F);
        BackColor = Canvas;
        ForeColor = Ink;
        ClientSize = new Size(540, 365);
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;

        string suffix = T(" · Custom", " · Personalizzato");
        name.Text = profile.Preset is null
            ? profile.Name[..Math.Min(60 - suffix.Length, profile.Name.Length)] + suffix
            : profile.Name;
        layer = Number(values.LayerHeight, range.Minimum, range.Maximum, 3, 0.01m);
        walls = Number(values.WallLoops, 0, 100, 0, 1);
        infill = Number(values.InfillDensity, 0, 100, 2, 1);
        supports = new CheckBox { Checked = values.Supports == true, Enabled = values.Supports is not null, AutoSize = true };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9, Padding = new Padding(20) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var printer = new Label
        {
            Text = profile.Printer, Font = new Font("Segoe UI Semibold", 14F), Dock = DockStyle.Fill,
            AutoEllipsis = true, Height = 28, Margin = new Padding(0, 0, 0, 10)
        };
        layout.Controls.Add(printer, 0, 0);
        layout.SetColumnSpan(printer, 2);
        AddField(1, T("Preset name", "Nome del preset"), name);
        AddField(2, T("Layer height (mm)", "Altezza strato (mm)"), layer);
        AddField(3, T("Wall loops", "Numero di pareti"), walls);
        AddField(4, T("Sparse infill (%)", "Riempimento sparso (%)"), infill);
        AddField(5, T("Generate supports", "Genera supporti"), supports);
        var description = new Label
        {
            Text = T("These values apply when you keep the destination settings. Unavailable fields use the reference.",
                "Questi valori si applicano quando mantieni le impostazioni di destinazione. I campi non disponibili seguono il riferimento."),
            Dock = DockStyle.Fill, AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 10, 0, 4)
        };
        layout.Controls.Add(description, 0, 6);
        layout.SetColumnSpan(description, 2);
        var hint = new Label { Dock = DockStyle.Fill, AutoSize = true, ForeColor = ConversionDetailsForm.ErrorColors.Ink };
        layout.Controls.Add(hint, 0, 7);
        layout.SetColumnSpan(hint, 2);
        var save = new RoundedButton(primary: true) { Text = T("Save preset", "Salva preset"), DialogResult = DialogResult.OK };
        var cancel = new RoundedButton { Text = T("Cancel", "Annulla"), DialogResult = DialogResult.Cancel };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 6, 0, 0)
        };
        actions.Controls.AddRange([save, cancel]);
        layout.Controls.Add(actions, 0, 8);
        layout.SetColumnSpan(actions, 2);
        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;
        ActiveControl = name;

        name.TextChanged += (_, _) => UpdateValidation();
        layer.ValueChanged += (_, _) => UpdateValidation();
        walls.ValueChanged += (_, _) => UpdateValidation();
        UpdateValidation();

        void UpdateValidation()
        {
            bool duplicate = usedNames.Contains(PresetName);
            bool invalidHeight = layer.Enabled && layer.Value != values.LayerHeight &&
                (layer.Value < range.Minimum || layer.Value > range.Maximum);
            bool invalidWalls = walls.Enabled && walls.Value != values.WallLoops && walls.Value > 100;
            save.Enabled = PresetName.Length > 0 && !duplicate && !invalidHeight && !invalidWalls;
            hint.Text = duplicate ? T("Choose a different name.", "Scegli un altro nome.")
                : invalidHeight ? T("Choose a layer height within the printer's limits.", "Scegli un’altezza strato entro i limiti della stampante.")
                : invalidWalls ? T("Choose up to 100 walls.", "Scegli fino a 100 pareti.") : "";
        }

        void AddField(int row, string label, Control input)
        {
            input.AccessibleName = label;
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 12, 5) }, 0, row);
            layout.Controls.Add(input, 1, row);
        }
    }

    private static NumericUpDown Number(decimal? value, decimal min, decimal max, int decimals, decimal increment) => new()
    {
        Dock = DockStyle.Fill, Enabled = value is not null, DecimalPlaces = decimals, Increment = increment,
        Minimum = Math.Min(value ?? min, min), Maximum = Math.Max(value ?? max, max), Value = value ?? min
    };
}

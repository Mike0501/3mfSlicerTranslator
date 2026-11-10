using static Translator.AppTheme;

namespace Translator;

internal sealed class MachineCatalogForm : Form
{
    private readonly List<CatalogMachine> machines = [];
    private readonly HashSet<string> loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly ComboBox brand = Picker();
    private readonly ComboBox model = Picker();
    private readonly ComboBox nozzle = Picker();
    private readonly ComboBox plate = Picker();
    private readonly NumericUpDown slots = new() { Minimum = 1, Maximum = 4, Value = 1, Dock = DockStyle.Fill };
    private readonly TextBox name = new() { MaxLength = 60, Dock = DockStyle.Fill, BackColor = Input, ForeColor = Ink };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = Muted };
    private readonly Label presets = new() { AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = Muted };
    private readonly RoundedButton browse = new() { Text = T("Locate slicer…", "Trova slicer…") };
    private readonly RoundedButton save = new(primary: true) { Text = T("Save", "Salva"), DialogResult = DialogResult.OK };
    private readonly HashSet<string> names;
    private string suggestedName = "";

    internal CatalogMachine? Machine => nozzle.SelectedItem as CatalogMachine;
    internal string ProfileName => name.Text.Trim();
    internal string BuildPlate => plate.SelectedIndex > 0 ? (string)plate.SelectedItem! : "";
    internal int SlotCount => (int)slots.Value;

    internal MachineCatalogForm(IEnumerable<string> usedNames)
    {
        names = usedNames.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        Text = T("Create from slicer presets", "Crea dai preset dello slicer");
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Canvas;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10F);
        ClientSize = new Size(550, 440);
        MinimumSize = new Size(500, 390);
        MinimizeBox = false;
        ShowInTaskbar = false;
        var layout = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, Padding = new Padding(16) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(browse, 1, 0);
        AddField(T("Printer brand", "Marchio stampante"), brand, 1);
        AddField(T("Printer model", "Modello stampante"), model, 2);
        AddField(T("Nozzle preset", "Preset ugello"), nozzle, 3);
        AddField(T("Build plate", "Piatto di stampa"), plate, 4);
        AddField(T("Filament slots", "Slot filamento"), slots, 5);
        AddField(T("Profile name", "Nome del profilo"), name, 6);
        layout.Controls.Add(presets, 0, 7);
        layout.SetColumnSpan(presets, 2);
        layout.Controls.Add(status, 0, 8);
        layout.SetColumnSpan(status, 2);
        var cancel = new RoundedButton { Text = T("Cancel", "Annulla"), DialogResult = DialogResult.Cancel };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.AddRange([save, cancel]);
        layout.Controls.Add(actions, 0, 9);
        layout.SetColumnSpan(actions, 2);
        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;

        brand.SelectedIndexChanged += (_, _) => UpdateModels();
        model.SelectedIndexChanged += (_, _) => UpdateNozzles();
        nozzle.SelectedIndexChanged += (_, _) => UpdateMachine();
        plate.FormattingEnabled = true;
        plate.Format += (_, args) =>
        {
            if (args.ListItem is string value)
                args.Value = ReferenceConfiguration.PlateLabel(value);
        };
        name.TextChanged += (_, _) => UpdateSave();
        Shown += async (_, _) => await LoadMachines(LegacyProfileCatalog.InstalledSlicers().ToArray());
        browse.Click += async (_, _) =>
        {
            using var picker = new OpenFileDialog
            {
                Filter = T("Slicer executable (*.exe)|*.exe", "Eseguibile dello slicer (*.exe)|*.exe"), CheckFileExists = true
            };
            if (picker.ShowDialog(this) == DialogResult.OK)
                await LoadMachines([picker.FileName], remember: true);
        };
        UpdateSave();

        void AddField(string label, Control field, int row)
        {
            field.AccessibleName = label;
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            layout.Controls.Add(field, 1, row);
        }
    }

    private async Task LoadMachines(string[] paths, bool remember = false)
    {
        UseWaitCursor = true;
        browse.Enabled = false;
        UpdateSave();
        status.Text = T("Reading slicer presets…", "Lettura dei preset degli slicer…");
        try
        {
            string? error = null;
            foreach (string path in paths)
            {
                if (loaded.Contains(path))
                    continue;
                try
                {
                    CatalogMachine[] entries = await Task.Run(() => LegacyProfileCatalog.ReadMachines(path));
                    if (entries.Length > 0)
                    {
                        machines.AddRange(entries);
                        loaded.Add(path);
                        if (remember)
                            LegacyProfileCatalog.RememberSlicerLocation(path);
                    }
                    else
                        error = T("No compatible presets found for this installation.", "Nessun preset compatibile trovato per questa installazione.");
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
                {
                    error = failure.Message;
                }
            }
            Populate(brand, machines.Select(machine => machine.Brand).Distinct().Order());
            status.Text = error ?? (machines.Count == 0
                ? T("Locate an installed slicer with printer presets, or add a reference 3MF from the profile library.",
                    "Trova uno slicer installato con preset stampante oppure aggiungi un 3MF di riferimento dalla libreria.")
                : CatalogSummary());
        }
        finally
        {
            UseWaitCursor = false;
            browse.Enabled = true;
            UpdateSave();
        }
    }

    private void UpdateModels() => Populate(model, machines.Where(machine => Equals(machine.Brand, brand.SelectedItem))
        .Select(machine => machine.Model).Distinct().Order());

    private void UpdateNozzles() => Populate(nozzle, machines.Where(machine =>
        Equals(machine.Brand, brand.SelectedItem) && Equals(machine.Model, model.SelectedItem)).OrderBy(machine => machine.Nozzle));

    private void UpdateMachine()
    {
        plate.Items.Clear();
        presets.Text = "";
        if (Machine is CatalogMachine machine)
        {
            presets.Text = T($"Process · {machine.Process}\nFilament · {machine.Filament}",
                $"Processo · {machine.Process}\nFilamento · {machine.Filament}");
            string defaultLabel = T("Slicer default", "Predefinito dello slicer");
            if (machine.DefaultPlate is { Length: > 0 } defaultPlate)
                defaultLabel += " · " + ReferenceConfiguration.PlateLabel(defaultPlate);
            Populate(plate, new[] { defaultLabel }.Concat(machine.Plates));
            if (name.Text.Length == 0 || name.Text == suggestedName)
            {
                string suggestion = $"{machine.Model} {machine.Nozzle} mm";
                suggestedName = suggestion[..Math.Min(suggestion.Length, name.MaxLength)];
                name.Text = suggestedName;
            }
        }
        UpdateSave();
    }

    private void UpdateSave() => save.Enabled = !UseWaitCursor && Machine is not null &&
        ProfileName.Length > 0 && !names.Contains(ProfileName);

    private string CatalogSummary()
    {
        int models = machines.Select(item => item.Model).Distinct().Count();
        string slicers = string.Join(", ", machines.Select(item => item.Catalog.SlicerName).Distinct());
        return T($"{brand.Items.Count} brands · {models} models · {slicers}",
            $"{brand.Items.Count} marchi · {models} modelli · {slicers}");
    }

    private static ComboBox Picker() => new()
    {
        Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Input, ForeColor = Ink
    };

    private static void Populate<T>(ComboBox picker, IEnumerable<T> options)
    {
        picker.Items.Clear();
        picker.Items.AddRange(options.Cast<object>().ToArray());
        if (picker.Items.Count > 0)
            picker.SelectedIndex = 0;
    }

    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        base.OnFormClosing(args);
        if (UseWaitCursor && args.CloseReason == CloseReason.UserClosing)
            args.Cancel = true;
    }
}

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
    private readonly NumericUpDown slots = new() { Minimum = 1, Maximum = 4, Value = 1, BorderStyle = BorderStyle.None, BackColor = Input, ForeColor = Ink };
    private readonly TextBox name = new() { MaxLength = 60, BorderStyle = BorderStyle.None, BackColor = Input, ForeColor = Ink };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = Muted };
    private readonly Label presets = new() { AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = Muted };
    private readonly Label orcaStatus = new() { AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = Muted };
    private readonly RoundedButton connectOrca = new() { Text = T("Connect OrcaSlicer…", "Collega OrcaSlicer…") };
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
        ClientSize = new Size(600, 585);
        MinimumSize = new Size(550, 500);
        MinimizeBox = false;
        ShowInTaskbar = false;
        var layout = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 4, Dock = DockStyle.Fill, Padding = new Padding(16), AutoScroll = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = T("New printer profile", "Nuovo profilo stampante"),
            AutoSize = true, Font = new Font("Segoe UI Semibold", 18F), Margin = new Padding(0, 0, 0, 10)
        }, 0, 0);
        var connection = Card(1, 1);
        var fields = Card(2, 2);
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var guidance = new Label
        {
            AutoSize = true, MaximumSize = new Size(490, 0),
            Text = T("To create a custom profile from the Orca catalog, connect OrcaSlicer (installed or portable). An existing 3MF reference can be used on its own.",
                "Per creare un profilo personalizzato dal catalogo Orca, collega OrcaSlicer (installato o portatile). Puoi usare un 3MF di riferimento già pronto anche da solo.")
        };
        connection.Controls.Add(guidance, 0, 0);
        connection.Controls.Add(orcaStatus, 0, 1);
        var locations = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        locations.Controls.AddRange([connectOrca, browse]);
        connection.Controls.Add(locations, 0, 2);
        AddField(T("Printer brand", "Marchio stampante"), brand, 0);
        AddField(T("Printer model", "Modello stampante"), model, 1);
        AddField(T("Nozzle preset", "Preset ugello"), nozzle, 2);
        AddField(T("Build plate", "Piatto di stampa"), plate, 3);
        AddField(T("Filament slots", "Slot filamento"), slots, 4);
        AddField(T("Profile name", "Nome del profilo"), name, 5);
        fields.Controls.Add(presets, 0, 6);
        fields.SetColumnSpan(presets, 2);
        fields.Controls.Add(status, 0, 7);
        fields.SetColumnSpan(status, 2);
        var cancel = new RoundedButton { Text = T("Cancel", "Annulla"), DialogResult = DialogResult.Cancel };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.AddRange([save, cancel]);
        layout.Controls.Add(actions, 0, 3);
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
        Shown += async (_, _) =>
        {
            orcaStatus.Text = T("Checking for OrcaSlicer…", "Ricerca di OrcaSlicer…");
            await LoadMachines(LegacyProfileCatalog.InstalledSlicers().ToArray());
        };
        connectOrca.Click += async (_, _) => await ConnectOrca();
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

        TableLayoutPanel Card(int row, int columns)
        {
            var card = new RoundedPanel(() => Surface)
            {
                Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(13), Margin = new Padding(0, 0, 0, 10)
            };
            var content = new TableLayoutPanel
            {
                ColumnCount = columns, Dock = DockStyle.Top, AutoSize = true, Margin = Padding.Empty, BackColor = Color.Transparent
            };
            card.Controls.Add(content);
            layout.Controls.Add(card, 0, row);
            return content;
        }

        void AddField(string label, Control field, int row)
        {
            field.AccessibleName = label;
            var inset = new RoundedPanel(() => Input, radius: 8)
            {
                Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8, 4, 8, 4)
            };
            field.Dock = DockStyle.Top;
            inset.Controls.Add(field);
            fields.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            fields.Controls.Add(inset, 1, row);
        }
    }

    private async Task ConnectOrca()
    {
        using var picker = new FolderBrowserDialog
        {
            Description = T("Choose the folder containing orca-slicer.exe.", "Scegli la cartella che contiene orca-slicer.exe."),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (picker.ShowDialog(this) != DialogResult.OK)
            return;
        string executable = Path.Combine(picker.SelectedPath, "orca-slicer.exe");
        if (!File.Exists(executable))
        {
            status.Text = T("This folder does not contain orca-slicer.exe. Choose the installation folder or the extracted portable folder.",
                "Questa cartella non contiene orca-slicer.exe. Scegli la cartella di installazione o quella della versione portatile estratta.");
            return;
        }
        await LoadMachines([executable], remember: true);
    }

    private async Task LoadMachines(string[] paths, bool remember = false)
    {
        UseWaitCursor = true;
        connectOrca.Enabled = false;
        browse.Enabled = false;
        UpdateSave();
        status.Text = T("Reading slicer presets…", "Lettura dei preset degli slicer…");
        try
        {
            string? error = null;
            foreach (string path in paths)
            {
                if (loaded.Contains(path))
                {
                    if (remember)
                        LegacyProfileCatalog.RememberSlicerLocation(path);
                    continue;
                }
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
            connectOrca.Enabled = true;
            browse.Enabled = true;
            orcaStatus.Text = machines.Any(machine => machine.Catalog.SlicerName == "OrcaSlicer")
                ? T("OrcaSlicer connected.", "OrcaSlicer collegato.")
                : T("OrcaSlicer catalog unavailable. Use Connect OrcaSlicer to select its folder.",
                    "Catalogo OrcaSlicer non disponibile. Usa Collega OrcaSlicer per selezionare la cartella.");
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

    private static ComboBox Picker()
    {
        var picker = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed,
            FlatStyle = FlatStyle.Flat, BackColor = Input, ForeColor = Ink
        };
        picker.DrawItem += (_, args) =>
        {
            bool highlighted = (args.State & DrawItemState.Selected) != 0 &&
                (args.State & DrawItemState.ComboBoxEdit) == 0;
            using var background = new SolidBrush(highlighted ? AccentLight : Input);
            args.Graphics.FillRectangle(background, args.Bounds);
            if (args.Index >= 0)
                TextRenderer.DrawText(args.Graphics, picker.GetItemText(picker.Items[args.Index]), picker.Font,
                    Rectangle.Inflate(args.Bounds, -3, 0), picker.Enabled ? Ink : Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            args.DrawFocusRectangle();
        };
        return picker;
    }

    private static void Populate<T>(ComboBox picker, IEnumerable<T> options)
    {
        picker.Items.Clear();
        picker.Items.AddRange(options.Cast<object>().ToArray());
        if (picker.Items.Count > 0)
            picker.SelectedIndex = 0;
    }

    protected override void OnHandleCreated(EventArgs args)
    {
        base.OnHandleCreated(args);
        ApplyTitleBar(Handle);
    }

    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        base.OnFormClosing(args);
        if (UseWaitCursor && args.CloseReason == CloseReason.UserClosing)
            args.Cancel = true;
    }
}

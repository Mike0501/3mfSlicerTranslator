using System.Text.Json;
using static Translator.AppTheme;

namespace Translator;

internal sealed class ProfileCreationForm : Form
{
    private sealed record ReferenceChoice(string Path, string Label, ReferenceConfiguration Configuration)
    {
        public override string ToString() => Label;
    }

    private readonly List<ReferenceChoice> references = [];
    private readonly ComboBox brand = Picker();
    private readonly ComboBox model = Picker();
    private readonly ComboBox nozzle = Picker();
    private readonly ComboBox plate = Picker();
    private readonly ComboBox reference = Picker();
    private readonly TextBox name = new() { Dock = DockStyle.Fill, MaxLength = 60, BackColor = Input, ForeColor = Ink };
    private readonly Label status = new() { AutoSize = true, ForeColor = Muted, MaximumSize = new Size(500, 0) };
    private readonly Label nameHint = new() { AutoSize = true, ForeColor = Muted };
    private readonly RoundedButton add = new() { Text = T("Add from 3MF…", "Aggiungi da 3MF…") };
    private readonly RoundedButton save = new(primary: true) { Text = T("Save", "Salva"), DialogResult = DialogResult.OK };
    private readonly IReadOnlySet<string> usedNames;
    private string suggestedName = "";

    internal string ProfileName => name.Text.Trim();
    internal string? ReferencePath => (reference.SelectedItem as ReferenceChoice)?.Path;
    internal ReferenceMachine? Machine => (reference.SelectedItem as ReferenceChoice)?.Configuration.Machine;
    internal string? BuildPlate => plate.SelectedItem is string { Length: > 0 } selected ? selected : null;

    internal ProfileCreationForm(IReadOnlyList<DestinationProfile> profiles)
    {
        usedNames = profiles.Select(item => item.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        Text = T("New printer profile", "Nuovo profilo stampante");
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Font = new Font("Segoe UI", 10F);
        BackColor = Canvas;
        ForeColor = Ink;
        ClientSize = new Size(555, 385);
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var intro = new Label
        {
            AutoSize = true, MaximumSize = new Size(500, 0), ForeColor = Muted,
            Text = T("Choose a configuration from saved profiles or add reference projects from your slicer.",
                "Scegli una configurazione dai profili salvati o aggiungi progetti di riferimento dal tuo slicer.")
        };
        layout.Controls.Add(intro, 0, 0);
        layout.SetColumnSpan(intro, 2);
        layout.Controls.Add(add, 1, 1);
        AddField(T("Printer brand", "Marchio stampante"), brand, 2);
        AddField(T("Printer model", "Modello stampante"), model, 3);
        AddField(T("Nozzle diameter", "Diametro ugello"), nozzle, 4);
        AddField(T("Build plate", "Piatto di stampa"), plate, 5);
        AddField(T("Reference project", "Progetto di riferimento"), reference, 6);
        AddField(T("Profile name", "Nome del profilo"), name, 7);
        layout.Controls.Add(nameHint, 1, 8);
        layout.Controls.Add(status, 0, 9);
        layout.SetColumnSpan(status, 2);

        var cancel = new RoundedButton { Text = T("Cancel", "Annulla"), DialogResult = DialogResult.Cancel };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.AddRange([save, cancel]);
        layout.Controls.Add(actions, 0, 10);
        layout.SetColumnSpan(actions, 2);
        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;

        nozzle.Format += (_, args) => args.Value = $"{args.ListItem} mm";
        plate.Format += (_, args) =>
        {
            if (args.ListItem is "")
                args.Value = T("Reference default", "Predefinito del riferimento");
        };
        brand.SelectionChangeCommitted += (_, _) => UpdateModels();
        model.SelectionChangeCommitted += (_, _) => UpdateNozzles();
        nozzle.SelectionChangeCommitted += (_, _) => UpdatePlates();
        plate.SelectionChangeCommitted += (_, _) => UpdateReferences();
        reference.SelectionChangeCommitted += (_, _) => UpdateName();
        name.TextChanged += (_, _) => UpdateSave();
        add.Click += async (_, _) => await AddReferences();
        Shown += async (_, _) => await LoadReferences(profiles.Where(item => item.ReferencePath is not null)
            .Select(item => (item.ReferencePath!, item.Name)).ToArray());
        ActiveControl = add;
        UpdateBrands();

        void AddField(string label, Control field, int row)
        {
            field.AccessibleName = label;
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            layout.Controls.Add(field, 1, row);
        }
    }

    private async Task AddReferences()
    {
        using var file = new OpenFileDialog
        {
            Title = T("Choose reference projects", "Scegli i progetti di riferimento"),
            Filter = T("3MF project (*.3mf)|*.3mf", "Progetto 3MF (*.3mf)|*.3mf"),
            CheckFileExists = true, Multiselect = true
        };
        if (file.ShowDialog(this) == DialogResult.OK)
            await LoadReferences(file.FileNames.Select(path => (path, Path.GetFileNameWithoutExtension(path))).ToArray());
    }

    private async Task LoadReferences((string Path, string Label)[] files)
    {
        if (files.Length == 0)
            return;
        UseWaitCursor = true;
        add.Enabled = false;
        UpdateSave();
        status.Text = T("Reading reference projects…", "Lettura dei progetti di riferimento…");
        try
        {
            var (loaded, error) = await Task.Run(() => ReadReferences(files));
            foreach (ReferenceChoice item in loaded)
            {
                int index = references.FindIndex(existing => StringComparer.OrdinalIgnoreCase.Equals(existing.Path, item.Path));
                if (index < 0)
                    references.Add(item);
                else
                    references[index] = item;
            }
            if (loaded.FirstOrDefault() is ReferenceChoice first)
                UpdateBrands(first);
            status.Text = error ?? "";
        }
        finally
        {
            UseWaitCursor = false;
            add.Enabled = true;
            UpdateSave();
        }
    }

    private static (List<ReferenceChoice> References, string? Error) ReadReferences((string Path, string Label)[] files)
    {
        var loaded = new List<ReferenceChoice>();
        string? errorMessage = null;
        foreach (var (path, label) in files.DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                loaded.Add(new(path, label, ProjectConverter.ReadReferenceConfiguration(path)));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or
                UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                errorMessage ??= $"{label} · {error.Message}";
            }
        }
        return (loaded, errorMessage);
    }

    private IEnumerable<ReferenceChoice> ByBrand => references.Where(item => Equals(item.Configuration.Machine.Brand, brand.SelectedItem));
    private IEnumerable<ReferenceChoice> ByModel => ByBrand.Where(item => Equals(item.Configuration.Machine.Model, model.SelectedItem));
    private IEnumerable<ReferenceChoice> ByNozzle => ByModel.Where(item => Equals(item.Configuration.Machine.NozzleDiameter, nozzle.SelectedItem));

    private void UpdateBrands(ReferenceChoice? preferred = null)
    {
        SetChoices(brand, references.Select(item => item.Configuration.Machine.Brand).Distinct().Order(), preferred?.Configuration.Machine.Brand);
        UpdateModels(preferred);
    }

    private void UpdateModels(ReferenceChoice? preferred = null)
    {
        SetChoices(model, ByBrand.Select(item => item.Configuration.Machine.Model).Distinct().Order(), preferred?.Configuration.Machine.Model);
        UpdateNozzles(preferred);
    }

    private void UpdateNozzles(ReferenceChoice? preferred = null)
    {
        SetChoices(nozzle, ByModel.Select(item => item.Configuration.Machine.NozzleDiameter).Distinct().Order(), preferred?.Configuration.Machine.NozzleDiameter);
        UpdatePlates(preferred);
    }

    private void UpdatePlates(ReferenceChoice? preferred = null)
    {
        var matches = ByNozzle.ToArray();
        SetChoices(plate, matches.SelectMany(item => Plates(item)).Distinct(),
            (preferred ?? matches.FirstOrDefault())?.Configuration.CurrentPlate ?? "");
        UpdateReferences(preferred);
    }

    private void UpdateReferences(ReferenceChoice? preferred = null)
    {
        SetChoices(reference, ByNozzle.Where(item => plate.SelectedItem is string selected && Plates(item).Contains(selected)), preferred);
        UpdateName();
    }

    private static IReadOnlyList<string> Plates(ReferenceChoice item) =>
        item.Configuration.CurrentPlate is null
            ? ["", .. item.Configuration.Plates]
            : item.Configuration.Plates;

    private void UpdateName()
    {
        if (reference.SelectedItem is ReferenceChoice item && (name.Text.Length == 0 || name.Text == suggestedName))
        {
            string suggestion = item.Label;
            int suffixNumber = 1;
            do
            {
                string suffix = suffixNumber == 1 ? "" : $" ({suffixNumber})";
                suggestedName = suggestion[..Math.Min(suggestion.Length, name.MaxLength - suffix.Length)] + suffix;
                suffixNumber++;
            } while (usedNames.Contains(suggestedName));
            name.Text = suggestedName;
        }
        UpdateSave();
    }

    private void UpdateSave()
    {
        bool duplicate = usedNames.Contains(ProfileName);
        save.Enabled = !UseWaitCursor && ReferencePath is not null && ProfileName.Length > 0 && !duplicate;
        nameHint.Text = duplicate ? T("Choose a different name.", "Scegli un altro nome.") : "";
    }

    private static ComboBox Picker() => new()
    {
        Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FormattingEnabled = true,
        BackColor = Input, ForeColor = Ink
    };

    private static void SetChoices<TValue>(ComboBox picker, IEnumerable<TValue> choices, object? preferred)
    {
        picker.Items.Clear();
        picker.Items.AddRange(choices.Cast<object>().ToArray());
        if (preferred is not null)
            picker.SelectedItem = preferred;
        if (picker.SelectedIndex < 0 && picker.Items.Count > 0)
            picker.SelectedIndex = 0;
        picker.Enabled = picker.Items.Count > 0;
    }

    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        base.OnFormClosing(args);
        if (UseWaitCursor && args.CloseReason == CloseReason.UserClosing)
            args.Cancel = true;
    }
}

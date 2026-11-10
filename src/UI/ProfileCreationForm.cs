using System.Globalization;
using System.Text.Json;
using static Translator.AppTheme;

namespace Translator;

internal sealed class ProfileCreationForm : Form
{
    private sealed record ReferenceChoice(string Path, string Label, ReferenceConfiguration Configuration)
    {
        public override string ToString() => $"{Label} · {Configuration.Machine.Model} · {Configuration.Machine.NozzleDiameter} mm";
    }

    private readonly List<ReferenceChoice> references = [];
    private readonly ComboBox brand = Picker(editable: true);
    private readonly ComboBox model = Picker(editable: true);
    private readonly ComboBox nozzle = Picker(editable: true);
    private readonly ComboBox plate = Picker(editable: true);
    private readonly ComboBox reference = Picker();
    private readonly TextBox name = new() { Dock = DockStyle.Fill, MaxLength = 60, BackColor = Input, ForeColor = Ink };
    private readonly Label status = new() { AutoSize = true, ForeColor = Muted, MaximumSize = new Size(500, 0) };
    private readonly Label nameHint = new() { AutoSize = true, ForeColor = Muted };
    private readonly RoundedButton add = new() { Text = T("Add from 3MF…", "Aggiungi da 3MF…") };
    private readonly RoundedButton save = new(primary: true) { Text = T("Save", "Salva"), DialogResult = DialogResult.OK };
    private readonly IReadOnlySet<string> usedNames;
    private string suggestedName = "";
    private bool refreshing;

    internal string ProfileName => name.Text.Trim();
    internal string? ReferencePath => (reference.SelectedItem as ReferenceChoice)?.Path;
    internal ReferenceMachine? Machine => (reference.SelectedItem as ReferenceChoice)?.Configuration.Machine;
    internal string? BuildPlate => (reference.SelectedItem as ReferenceChoice)?.Configuration.Plates
        .FirstOrDefault(value => Matches(plate.Text, value) && plate.Text.Trim().Length > 0);

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
            Text = T("Choose or type your hardware to find a matching 3MF. Leave a field blank to include any value.",
                "Scegli o digita i componenti per trovare un 3MF compatibile. Lascia vuoto un campo per includere tutti i valori.")
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

        foreach (ComboBox field in new[] { brand, model, nozzle, plate })
            field.TextChanged += (_, _) => RefreshChoices();
        reference.SelectionChangeCommitted += (_, _) => UpdateName();
        name.TextChanged += (_, _) => UpdateSave();
        add.Click += async (_, _) => await AddReferences();
        Shown += async (_, _) => await LoadReferences(profiles.Where(item => item.ReferencePath is not null)
            .Select(item => (item.ReferencePath!, item.Name)).ToArray());
        ActiveControl = brand;
        RefreshChoices();

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
            RefreshChoices(loaded.FirstOrDefault());
            if (error is not null)
                status.Text = error;
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

    private void RefreshChoices(ReferenceChoice? preferred = null)
    {
        if (refreshing)
            return;
        refreshing = true;
        try
        {
            var brands = references.Where(item => Matches(brand.Text, item.Configuration.Machine.Brand)).ToArray();
            var models = brands.Where(item => Matches(model.Text, item.Configuration.Machine.Model)).ToArray();
            var nozzles = models.Where(item => MatchesNozzle(nozzle.Text, item.Configuration.Machine.NozzleDiameter)).ToArray();
            var matches = nozzles.Where(item => string.IsNullOrWhiteSpace(plate.Text) ||
                item.Configuration.Plates.Any(value => Matches(plate.Text, value))).ToArray();

            SetSuggestions(brand, references.Select(item => item.Configuration.Machine.Brand)
                .Concat(["Anycubic", "Bambu Lab", "Creality", "Elegoo", "Original Prusa", "Qidi"]));
            SetSuggestions(model, brands.Select(item => item.Configuration.Machine.Model));
            SetSuggestions(nozzle, models.Select(item => item.Configuration.Machine.NozzleDiameter.ToString("G", CultureInfo.CurrentCulture))
                .Concat(new[] { 0.2, 0.25, 0.4, 0.6, 0.8 }.Select(value => value.ToString("G", CultureInfo.CurrentCulture))));
            SetSuggestions(plate, nozzles.SelectMany(item => item.Configuration.Plates)
                .Concat(ReferenceConfiguration.PlateTemperatureKeys.Keys));

            ReferenceChoice? selected = preferred ?? reference.SelectedItem as ReferenceChoice;
            reference.Items.Clear();
            reference.Items.AddRange(matches);
            if (selected is not null)
                reference.SelectedItem = selected;
            if (reference.SelectedIndex < 0 && matches.Length > 0)
                reference.SelectedIndex = 0;
            reference.Enabled = matches.Length > 0;
            status.Text = matches.Length == 0
                ? T("Add a 3MF saved with this configuration, or clear a filter to see more references.",
                    "Aggiungi un 3MF salvato con questa configurazione o svuota un filtro per vedere altri riferimenti.")
                : "";
            UpdateName();
        }
        finally { refreshing = false; }
    }

    private static bool Matches(string filter, string value) => string.IsNullOrWhiteSpace(filter) ||
        filter.Trim().Equals(value, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesNozzle(string filter, double diameter) => string.IsNullOrWhiteSpace(filter) ||
        ProcessSettings.TryNumber(filter.Trim().Replace(',', '.'), out double value) && value == diameter;

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

    private static ComboBox Picker(bool editable = false) => new()
    {
        Dock = DockStyle.Fill, DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList,
        BackColor = Input, ForeColor = Ink
    };

    private static void SetSuggestions(ComboBox picker, IEnumerable<string> choices)
    {
        string[] options = choices.Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
        if (picker.Items.Cast<string>().SequenceEqual(options))
            return;
        string text = picker.Text;
        int caret = picker.SelectionStart;
        picker.Items.Clear();
        picker.Items.AddRange(options);
        picker.Text = text;
        picker.SelectionStart = Math.Min(caret, text.Length);
    }

    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        base.OnFormClosing(args);
        if (UseWaitCursor && args.CloseReason == CloseReason.UserClosing)
            args.Cancel = true;
    }
}

using static Translator.AppTheme;

namespace Translator;

internal sealed class ProfileLibraryForm : Form
{
    private readonly ListBox profiles = new()
    {
        Dock = DockStyle.Fill, IntegralHeight = false, BackColor = Input, ForeColor = Ink, BorderStyle = BorderStyle.None,
        AccessibleName = T("Saved profiles", "Profili salvati")
    };
    private readonly Label details = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Padding = new Padding(12) };
    private readonly Label status = new() { AutoSize = true, ForeColor = Muted, MaximumSize = new Size(520, 0) };
    private readonly RoundedButton migrate = new() { Text = T("Convert to 3MF…", "Converti in 3MF…"), Visible = false };
    private readonly RoundedButton customize = new() { Text = T("Customize…", "Personalizza…"), Visible = false };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 7, 0, 0) };
    private readonly Button? use;

    internal DestinationProfile? Selection { get; private set; }

    internal ProfileLibraryForm(DestinationProfile? previous, bool saveOnly)
    {
        Text = T("Destination profiles · 3MF Translator", "Profili di destinazione · 3MF Translator");
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);
        BackColor = Canvas;
        ForeColor = Ink;
        ClientSize = new Size(680, 420);
        MinimumSize = new Size(530, 380);
        MinimizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = T("Profile library", "Libreria profili"), Font = new Font("Segoe UI Semibold", 19F),
            AutoSize = true, Margin = Padding.Empty
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = T("Save a reference 3MF for a printer you use.", "Salva un 3MF di riferimento per una stampante che usi."),
            AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 5, 0, 14)
        }, 0, 1);
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        var listInset = new RoundedPanel(() => Input) { Dock = DockStyle.Fill, Padding = new Padding(9) };
        listInset.Controls.Add(profiles);
        content.Controls.Add(listInset, 0, 0);
        var detailsInset = new RoundedPanel(() => Surface) { Dock = DockStyle.Fill, Padding = new Padding(7) };
        detailsInset.Controls.Add(details);
        content.Controls.Add(detailsInset, 1, 0);
        layout.Controls.Add(content, 0, 2);
        status.Margin = new Padding(3, 8, 3, 3);
        layout.Controls.Add(status, 0, 3);

        var catalog = new RoundedButton(primary: saveOnly) { Text = T("New profile…", "Nuovo profilo…") };
        var add = new RoundedButton { Text = T("From a 3MF reference…", "Da un riferimento 3MF…") };
        var close = new RoundedButton { Text = T("Close", "Chiudi"), DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange([catalog, add, migrate, customize]);
        if (!saveOnly)
        {
            use = new RoundedButton(primary: true) { Text = T("Use profile", "Usa profilo") };
            actions.Controls.Add(use);
            AcceptButton = use;
            use.Click += async (_, _) => await UseSelected();
        }
        actions.Controls.Add(close);
        layout.Controls.Add(actions, 0, 4);
        Controls.Add(layout);
        CancelButton = close;
        profiles.SelectedIndexChanged += (_, _) => UpdateDetails();
        profiles.DoubleClick += async (_, _) => { if (!saveOnly) await UseSelected(); };
        add.Click += async (_, _) => await CreateProfile();
        catalog.Click += async (_, _) => await CreateCatalogProfile();
        customize.Click += async (_, _) => await CustomizePreset();
        migrate.Click += async (_, _) =>
        {
            if (profiles.SelectedItem is DestinationProfile { ReferencePath: null } profile)
                _ = await MigrateProfile(profile);
        };
        Reload(previous?.Name);
        ActiveControl = saveOnly || profiles.Items.Count == 0 ? catalog : profiles;
    }

    private void Reload(string? name)
    {
        profiles.Items.Clear();
        profiles.Items.AddRange(ProfileStore.ReadSaved().ToArray());
        DestinationProfile? selected = profiles.Items.OfType<DestinationProfile>()
            .FirstOrDefault(profile => profile.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        profiles.SelectedItem = selected;
        if (selected is null && profiles.Items.Count > 0)
            profiles.SelectedIndex = 0;
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        if (use is not null)
            use.Enabled = profiles.SelectedItem is DestinationProfile;
        migrate.Visible = profiles.SelectedItem is DestinationProfile { ReferencePath: null };
        customize.Visible = profiles.SelectedItem is DestinationProfile { ReferencePath: not null };
        details.Text = profiles.SelectedItem is not DestinationProfile profile
            ? T("Add a reference project to create your first profile.", "Aggiungi un progetto di riferimento per creare il primo profilo.")
            : DescribePrinter(profile) + Environment.NewLine + Environment.NewLine + (profile.ReferencePath is string path
                ? (profile.Preset is null ? T("Base preset", "Preset base") : T("Custom preset", "Preset personalizzato"))
                    + Environment.NewLine + T("Reference 3MF", "Riferimento 3MF") + Environment.NewLine + path
                : T("Saved preset from an earlier version. It can be converted into a reusable 3MF profile.",
                    "Preset salvato con una versione precedente. Puoi convertirlo in un profilo 3MF riutilizzabile."));
    }

    private static string DescribePrinter(DestinationProfile profile)
    {
        if (profile.Machine is not ReferenceMachine machine)
            return profile.Printer;
        string plate = profile.Bed.Length == 0 ? T("Reference default", "Predefinito del riferimento") : ReferenceConfiguration.PlateLabel(profile.Bed);
        return string.Join(Environment.NewLine, machine.Model,
            T($"Nozzle · {machine.NozzleDiameter} mm", $"Ugello · {machine.NozzleDiameter} mm"),
            T($"Build plate · {plate}", $"Piatto · {plate}"));
    }

    private async Task CustomizePreset()
    {
        if (profiles.SelectedItem is not DestinationProfile { ReferencePath: not null } profile)
            return;
        try
        {
            var (values, range) = ProjectConverter.GetDestinationPreset(profile);
            if (!values.HasSettings)
                throw new InvalidDataException(T("This reference has no editable print settings.",
                    "Questo riferimento non contiene impostazioni di stampa modificabili."));
            var usedNames = profiles.Items.OfType<DestinationProfile>()
                .Where(item => profile.Preset is null || item != profile)
                .Select(item => item.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            using var editor = new PresetEditorForm(profile, values, range, usedNames) { Icon = Icon };
            if (editor.ShowDialog(this) != DialogResult.OK)
                return;

            string name = editor.PresetName;
            PrintPreset preset = editor.Values;
            _ = await SaveProfile(() => Task.Run(() => ProfileStore.SavePreset(profile, name, preset)),
                T("Saving preset…", "Salvataggio preset…"), T("Custom preset saved.", "Preset personalizzato salvato."));
        }
        catch (Exception error) { status.Text = error.Message; }
    }

    private async Task CreateProfile()
    {
        using var editor = new ProfileCreationForm(profiles.Items.OfType<DestinationProfile>().ToArray()) { Icon = Icon };
        if (editor.ShowDialog(this) != DialogResult.OK || editor.ReferencePath is not string path ||
            editor.Machine is not ReferenceMachine machine)
            return;

        string name = editor.ProfileName;
        string? plate = editor.BuildPlate;
        _ = await SaveProfile(
            () => Task.Run(() => ProfileStore.ImportReference(name, path, machine: machine, buildPlate: plate)),
            T("Saving the reference…", "Salvataggio del riferimento…"),
            T("Profile saved with a copy of the reference.", "Profilo salvato con una copia del riferimento."));
    }

    private async Task CreateCatalogProfile()
    {
        using var editor = new MachineCatalogForm(profiles.Items.OfType<DestinationProfile>().Select(item => item.Name)) { Icon = Icon };
        if (editor.ShowDialog(this) != DialogResult.OK || editor.Machine is not CatalogMachine machine)
            return;
        string name = editor.ProfileName;
        string plate = editor.BuildPlate;
        int slots = editor.SlotCount;
        _ = await SaveProfile(() => Task.Run(() => machine.Catalog.Create(machine, name, plate, slots)),
            T("Generating a reference in the slicer…", "Generazione del riferimento nello slicer…"),
            T("Printer profile saved.", "Profilo stampante salvato."));
    }

    private async Task UseSelected()
    {
        if (profiles.SelectedItem is not DestinationProfile profile)
            return;
        try
        {
            if (profile.ReferencePath is null)
            {
                if (await MigrateProfile(profile) is not DestinationProfile migrated)
                    return;
                profile = migrated;
            }

            if (profile.ReferencePath is string path)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException(T("The reference file is no longer in its saved location.",
                        "Il file di riferimento non è più nella posizione salvata."));
                if (ProjectConverter.GetReferencePrinter(path) != profile.Printer)
                    throw new InvalidDataException(T("The reference printer has changed. Add the reference again.",
                        "La stampante del riferimento è cambiata. Aggiungi di nuovo il riferimento."));
            }
            Complete(profile);
        }
        catch (Exception error) { status.Text = error.Message; }
    }

    private Task<DestinationProfile?> MigrateProfile(DestinationProfile profile) =>
        SaveProfile(async () =>
        {
            LegacyProfileCatalog catalog = await LegacyProfileCatalog.LoadAsync();
            return await Task.Run(() => catalog.Migrate(profile));
        }, T("Preparing the saved profile…", "Preparazione del profilo salvato…"),
            T("The profile now has its own reference 3MF.", "Ora il profilo ha un proprio 3MF di riferimento."));

    private async Task<DestinationProfile?> SaveProfile(Func<Task<DestinationProfile>> save, string progress, string success)
    {
        profiles.Enabled = false;
        actions.Enabled = false;
        UseWaitCursor = true;
        status.Text = progress;
        try
        {
            DestinationProfile saved = await save();
            Reload(saved.Name);
            status.Text = success;
            return saved;
        }
        catch (Exception error)
        {
            status.Text = error.Message;
            return null;
        }
        finally
        {
            profiles.Enabled = true;
            actions.Enabled = true;
            UseWaitCursor = false;
        }
    }

    private void Complete(DestinationProfile profile)
    {
        Selection = profile;
        DialogResult = DialogResult.OK;
    }

    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        base.OnFormClosing(args);
        if (UseWaitCursor && args.CloseReason == CloseReason.UserClosing)
            args.Cancel = true;
    }
}

using static Translator.AppTheme;

namespace Translator;

internal sealed record TranslatorSelection(string Source, string? Preset, string Destination,
    DestinationProfile? Profile, string? ReferencePrinter);

internal sealed class TranslatorForm : Form
{
    private readonly TextBox source = FileField(T("Source 3MF project", "Progetto 3MF di origine"), T("No project selected", "Nessun progetto selezionato"));
    private readonly TextBox template = FileField(T("Destination 3MF project", "Progetto 3MF di destinazione"), T("No reference selected", "Nessun modello selezionato"));
    private readonly ComboBox process = new()
    {
        AccessibleName = T("Source settings to import", "Impostazioni da importare dall’origine"),
        FlatStyle = FlatStyle.Flat,
        DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Enabled = false
    };
    private readonly RichTextBox report = new()
    {
        AccessibleName = T("Conversion report", "Resoconto della conversione"), ReadOnly = true, DetectUrls = false,
        BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
        ScrollBars = RichTextBoxScrollBars.Vertical, TabStop = false
    };
    private readonly Label status = new()
    {
        AutoSize = true, Padding = new Padding(9, 4, 9, 4), Margin = new Padding(0, 3, 0, 0),
        Anchor = AnchorStyles.Top | AnchorStyles.Right
    };
    private readonly Button sourceBrowse = StyledButton(T("Browse…", "Sfoglia…"));
    private readonly Button templateBrowse = StyledButton(T("Reference 3MF…", "Riferimento 3MF…"));
    private readonly Button profileBrowse = StyledButton(T("Profile…", "Profilo…"));
    private readonly Button createProfile = StyledButton(T("Create profile…", "Crea profilo…"));
    private readonly ThemeSwitch theme = new();
    private readonly Button help = IconButton(T("Quick guide", "Guida rapida"), ToolbarIcon.Help);
    private readonly ToolTip toolbarTips = new() { InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 5000 };
    private readonly Button convert = StyledButton(T("Translate and save…", "Traduci e salva…"), primary: true);
    private readonly RoundedButton detailsButton = StyledButton(T("Result details…", "Dettagli esito…"));
    private readonly Label destinationHint = Caption("", 9);
    private readonly LanguageSelector language = new()
    {
        Margin = new Padding(12, 6, 0, 0), Anchor = AnchorStyles.Right
    };
    private DestinationProfile? destinationProfile;
    private string? referencePrinter;
    private ConversionDetails? conversionDetails;
    private bool reportIsError;
    private readonly List<Action> themeColors = [];
    internal TranslatorSelection? RestartSelection { get; private set; }

    internal TranslatorForm(TranslatorSelection? selection = null)
    {
        Text = "3MF Translator";
        MinimumSize = new Size(850, 580);
        ClientSize = new Size(980, 565);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);

        using Stream iconStream = typeof(TranslatorForm).Assembly.GetManifestResourceStream("Translator.Icon")!;
        using var appIcon = new Icon(iconStream);
        Icon = (Icon)appIcon.Clone();

        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.Controls.Add(CreateHeader(), 0, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(24, 24, 24, 0)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 59));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 41));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(CreateWorkflow(), 0, 0);
        body.Controls.Add(CreateReport(), 1, 0);
        page.Controls.Add(body, 0, 1);
        Label footer = Caption(T("Export to Bambu Studio · OrcaSlicer · Anycubic Slicer Next · Creality Print   |   Also imports from PrusaSlicer and SuperSlicer", "Destinazione Bambu Studio · OrcaSlicer · Anycubic Slicer Next · Creality Print   |   Importa anche da PrusaSlicer e SuperSlicer"), 9);
        footer.Margin = new Padding(26, 12, 26, 14);
        page.Controls.Add(footer, 0, 2);
        Controls.Add(page);
        BindColors(footer, foreground: () => Muted);
        BindColors(destinationHint, foreground: () => Muted);
        foreach (Control input in new Control[] { source, template, process, report, language })
            BindColors(input, foreground: () => Ink, background: () => Input);
        ApplyTheme();

        sourceBrowse.AccessibleName = T("Choose the source 3MF project", "Scegli il progetto 3MF di origine");
        templateBrowse.AccessibleName = T("Choose the destination 3MF project", "Scegli il progetto 3MF di destinazione");
        sourceBrowse.Click += (_, _) => PickSourceProject();
        templateBrowse.Click += (_, _) => PickDestinationReference();
        profileBrowse.AccessibleName = T("Choose a destination profile", "Scegli un profilo di destinazione");
        profileBrowse.Click += (_, _) => PickDestinationProfile();
        createProfile.Click += (_, _) => PickDestinationProfile(saveOnly: true);
        theme.CheckedChanged += (_, _) => ChangeTheme();
        help.Click += (_, _) =>
        {
            using var guide = new QuickGuideForm { Icon = Icon };
            guide.ShowDialog(this);
        };
        toolbarTips.SetToolTip(help, help.AccessibleName);
        toolbarTips.SetToolTip(createProfile, T("Open the profile library", "Apri la libreria profili"));
        Disposed += (_, _) => toolbarTips.Dispose();
        convert.Enabled = false;
        convert.Click += ConvertProject;
        detailsButton.Click += (_, _) =>
        {
            if (conversionDetails is not ConversionDetails details)
                return;
            using var dialog = new ConversionDetailsForm(details) { Icon = Icon };
            dialog.ShowDialog(this);
        };
        AcceptButton = convert;
        ActiveControl = sourceBrowse;
        process.Items.Add(T("Available after selecting a project", "Disponibili dopo la scelta del progetto"));
        process.SelectedIndex = 0;
        ShowReport(T("Ready", "In attesa"), T("Choose a source 3MF and a reference project saved with the printer and settings you want to use.",
            "Scegli un 3MF di origine e un progetto di riferimento salvato con la stampante e le impostazioni che vuoi usare."));
        if (selection is not null)
        {
            source.Text = selection.Source;
            template.Text = selection.Destination;
            destinationProfile = selection.Profile;
            referencePrinter = selection.ReferencePrinter;
            if (source.TextLength > 0)
            {
                ReadSourceProcesses();
                if (selection.Preset is not null)
                {
                    SourceProcess? selected = process.Items.OfType<SourceProcess>().FirstOrDefault(item => item.Entry == selection.Preset);
                    if (selected is not null)
                        process.SelectedItem = selected;
                }
            }
            convert.Enabled = process.SelectedItem is SourceProcess && template.TextLength > 0;
        }
        UpdateDestinationHint();
    }

    private TableLayoutPanel CreateHeader()
    {
        var header = new TableLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2,
            Margin = Padding.Empty, Padding = new Padding(24, 22, 24, 22)
        };
        BindColors(header, background: () => Navy);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        using var artwork = new Icon(Icon!, new Size(64, 64));
        var mark = new PictureBox
        {
            Image = artwork.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(64, 64), Margin = Padding.Empty, Anchor = AnchorStyles.Left
        };
        mark.Disposed += (_, _) => mark.Image?.Dispose();
        header.Controls.Add(mark, 0, 0);
        header.SetRowSpan(mark, 2);

        Label title = Caption("3MF Translator", 25, Color.White, semibold: true);
        title.Margin = Padding.Empty;
        header.Controls.Add(title, 1, 0);
        Label description = Caption(T("Bring your 3MF projects to the slicer you use.", "Porta i tuoi progetti 3MF sullo slicer che usi."), 10, Color.FromArgb(207, 220, 248));
        description.Margin = new Padding(2, 2, 0, 0);
        header.Controls.Add(description, 1, 1);

        Label offline = Caption("OFFLINE", 9, Color.FromArgb(172, 247, 231), semibold: true);
        offline.BackColor = Color.FromArgb(37, 55, 94);
        offline.Padding = new Padding(12, 6, 12, 6);
        offline.Margin = new Padding(10, 2, 0, 0);
        var actions = new FlowLayoutPanel
        {
            AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false, Margin = new Padding(12, 0, 0, 0)
        };
        actions.Controls.Add(createProfile);
        actions.Controls.Add(offline);
        header.Controls.Add(actions, 2, 0);
        var preferences = new FlowLayoutPanel
        {
            AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false, Margin = Padding.Empty
        };
        theme.Margin = new Padding(0, 4, 0, 0);
        help.Margin = new Padding(6, 4, 0, 0);
        preferences.Controls.Add(theme);
        preferences.Controls.Add(help);
        preferences.Controls.Add(language);
        language.SelectedIndexChanged += ChangeLanguage;
        header.Controls.Add(preferences, 2, 1);
        return header;
    }

    private void ChangeLanguage(object? sender, EventArgs args)
    {
        bool italian = language.SelectedIndex == LanguageSelector.ItalianIndex;
        if (italian == IsItalian)
            return;
        try
        {
            SetLanguage(italian);
            Reopen();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            language.SelectedIndex = IsItalian ? LanguageSelector.ItalianIndex : 0;
            ShowError(T("Language", "Lingua"), T("Could not save the language preference. ",
                "Impossibile salvare la lingua preferita. ") + error.Message);
        }
    }

    private void ChangeTheme()
    {
        if (theme.Checked == Dark)
            return;
        try
        {
            SetDark(theme.Checked);
            ApplyNativeTheme();
            ApplyTitleBar(Handle);
            ApplyTheme();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            theme.Checked = Dark;
            ShowError(T("Theme", "Tema"), T("Could not save the theme preference. ",
                "Impossibile salvare il tema preferito. ") + error.Message);
        }
    }

    private void BindColors(Control control, Func<Color>? foreground = null, Func<Color>? background = null)
    {
        themeColors.Add(() =>
        {
            if (foreground is not null)
                control.ForeColor = foreground();
            if (background is not null)
                control.BackColor = background();
        });
    }

    private void ApplyTheme()
    {
        BackColor = Canvas;
        ForeColor = Ink;
        foreach (Action apply in themeColors)
            apply();
        ApplyStatusColors();
        SetDetails(conversionDetails);
        Invalidate(invalidateChildren: true);
    }

    private void Reopen()
    {
        RestartSelection = new TranslatorSelection(source.Text, (process.SelectedItem as SourceProcess)?.Entry,
            template.Text, destinationProfile, referencePrinter);
        Close();
    }

    private Control CreateWorkflow()
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 8)
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 8; row++)
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label heading = Caption(T("New conversion", "Nuova conversione"), 16, semibold: true);
        heading.Margin = new Padding(0, 0, 0, 13);
        card.Controls.Add(heading, 0, 0);
        card.Controls.Add(Step("01", T("Source project", "Progetto di origine")), 0, 1);
        card.Controls.Add(FilePicker(source, sourceBrowse), 0, 2);
        card.Controls.Add(Step("02", T("Source settings to import", "Impostazioni da importare dall’origine")), 0, 3);
        var presetInset = new RoundedPanel(() => Input, 8)
        {
            Dock = DockStyle.Top, Height = 31, Padding = new Padding(6, 3, 6, 3),
            Margin = new Padding(0, 4, 0, 9)
        };
        presetInset.Controls.Add(process);
        card.Controls.Add(presetInset, 0, 4);
        card.Controls.Add(Step("03", T("Destination", "Destinazione")), 0, 5);
        TableLayoutPanel destination = FilePicker(template, templateBrowse, profileBrowse);
        destination.Margin = new Padding(0, 4, 0, 4);
        card.Controls.Add(destination, 0, 6);
        card.Controls.Add(destinationHint, 0, 7);
        convert.Dock = DockStyle.Top;
        convert.Margin = new Padding(0, 7, 0, 0);
        card.Controls.Add(convert, 0, 9);
        return FrameCard(card, new Padding(0, 0, 10, 0));
    }

    private Control CreateReport()
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4,
            Margin = Padding.Empty, Padding = new Padding(10)
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Label heading = Caption(T("Report", "Resoconto"), 16, semibold: true);
        card.Controls.Add(heading, 0, 0);
        card.Controls.Add(status, 1, 0);
        Label subtitle = Caption(T("Result, settings and warnings", "Esito, impostazioni e avvisi"), 9);
        BindColors(subtitle, foreground: () => Muted);
        subtitle.Margin = new Padding(0, 4, 0, 16);
        card.Controls.Add(subtitle, 0, 1);
        card.SetColumnSpan(subtitle, 2);
        var inset = new RoundedPanel(() => Input) { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(13) };
        inset.Controls.Add(report);
        card.Controls.Add(inset, 0, 2);
        card.SetColumnSpan(inset, 2);
        detailsButton.Margin = new Padding(0, 12, 0, 0);
        card.Controls.Add(detailsButton, 0, 3);
        card.SetColumnSpan(detailsButton, 2);
        return FrameCard(card, new Padding(10, 0, 0, 0));
    }

    private Panel FrameCard(Control content, Padding margin)
    {
        var frame = new RoundedPanel(() => Surface, 18) { Dock = DockStyle.Fill, Padding = new Padding(13), Margin = margin };
        frame.Controls.Add(content);
        return frame;
    }

    private static Label Caption(string text, float size, Color color = default, bool semibold = false) => new()
    {
        Text = text, AutoSize = true, ForeColor = color, Margin = Padding.Empty,
        Font = new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", size)
    };

    private FlowLayoutPanel Step(string number, string text)
    {
        var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
        Label badge = Caption(number, 9, semibold: true);
        BindColors(badge, foreground: () => Accent, background: () => AccentLight);
        badge.Padding = new Padding(7, 3, 7, 3);
        badge.Margin = new Padding(0, 0, 9, 0);
        row.Controls.Add(badge);
        Label label = Caption(text, 10, semibold: true);
        label.Margin = new Padding(0, 2, 0, 0);
        row.Controls.Add(label);
        return row;
    }

    private static TextBox FileField(string name, string placeholder) => new()
    {
        AccessibleName = name, PlaceholderText = placeholder, ReadOnly = true,
        BorderStyle = BorderStyle.None, Dock = DockStyle.Fill
    };

    private TableLayoutPanel FilePicker(TextBox field, params Button[] buttons)
    {
        var row = new TableLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, ColumnCount = buttons.Length + 1, RowCount = 1,
            Margin = new Padding(0, 5, 0, 10)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        var inset = new RoundedPanel(() => Input, 8) { Dock = DockStyle.Fill, Padding = new Padding(10, 9, 10, 4) };
        inset.Margin = new Padding(0, 0, 8, 0);
        inset.Controls.Add(field);
        row.Controls.Add(inset, 0, 0);
        for (int i = 0; i < buttons.Length; i++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            if (i > 0)
                buttons[i].Margin = new Padding(6, 0, 0, 0);
            row.Controls.Add(buttons[i], i + 1, 0);
        }
        return row;
    }

    private static RoundedButton StyledButton(string text, bool primary = false) => new(primary)
    {
        Text = text, Margin = Padding.Empty,
        Padding = primary ? new Padding(18, 8, 18, 8) : new Padding(10, 5, 10, 5)
    };

    private static Button IconButton(string accessibleName, ToolbarIcon icon) => new RoundedButton(icon: icon)
    {
        AccessibleName = accessibleName, AutoSize = false, Size = new Size(34, 30), Padding = Padding.Empty
    };

    private string? BrowseProject(string title)
    {
        using var dialog = new OpenFileDialog { Title = title, Filter = T("3MF project (*.3mf)|*.3mf", "Progetto 3MF (*.3mf)|*.3mf"), CheckFileExists = true };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private void PickSourceProject()
    {
        if (BrowseProject(T("Choose the source project", "Scegli il progetto di origine")) is not string path)
            return;

        source.Text = path;
        source.Select(0, 0);
        ReadSourceProcesses();
        convert.Enabled = process.SelectedItem is SourceProcess && template.TextLength > 0;
    }

    private void PickDestinationReference()
    {
        if (BrowseProject(T("Choose a 3MF saved in the destination slicer", "Scegli un 3MF salvato nello slicer di destinazione")) is not string path)
            return;

        try
        {
            referencePrinter = ProjectConverter.GetReferencePrinter(path);
            destinationProfile = null;
            template.Text = path;
            template.Select(0, 0);
            UpdateDestinationHint();
            convert.Enabled = process.SelectedItem is SourceProcess;
            SetDetails(null);
            ShowReport(T("Reference loaded", "Riferimento letto"), destinationHint.Text);
        }
        catch (Exception error)
        {
            ShowError(Path.GetFileName(path), error.Message);
        }
    }

    private void UpdateDestinationHint()
    {
        string? printer = destinationProfile?.Printer ?? referencePrinter;
        destinationHint.Text = printer is null
            ? T("Use a project saved in the slicer you want to print from.", "Usa un progetto salvato nello slicer da cui vuoi stampare.")
            : T($"Destination printer · {printer}", $"Stampante di destinazione · {printer}");
        destinationHint.AutoEllipsis = true;
        destinationHint.AutoSize = false;
        destinationHint.Height = destinationHint.PreferredHeight;
        destinationHint.Dock = DockStyle.Top;
    }

    private void ReadSourceProcesses()
    {
        process.Items.Clear();
        process.Enabled = false;
        try
        {
            List<SourceProcess> processes = ProjectConverter.GetSourceProcesses(source.Text);
            process.Items.AddRange(processes.ToArray());
            process.SelectedIndex = 0;
            process.Enabled = true;
            SetDetails(null);
            ShowReport(T("Source loaded", "Origine letta"), processes.Count > 1
                ? T("Use the active settings or choose a preset embedded by the author.", "Puoi usare le impostazioni attive o scegliere un preset incorporato dall’autore.")
                : T("The active settings from the source project will be imported.", "Verranno importate le impostazioni attive del progetto di origine."));
        }
        catch (Exception error)
        {
            ShowError(Path.GetFileName(source.Text), T("Could not read the source project. ", "Impossibile leggere il progetto di origine. ") + error.Message);
        }
    }

    private void PickDestinationProfile(bool saveOnly = false)
    {
        try
        {
            using var dialog = new ProfileLibraryForm(saveOnly ? null : destinationProfile, saveOnly) { Icon = Icon };
            DialogResult result = dialog.ShowDialog(this);
            if (destinationProfile is not null && !ProfileStore.ReadSaved().Any(profile =>
                    profile.Name.Equals(destinationProfile.Name, StringComparison.CurrentCultureIgnoreCase)))
            {
                destinationProfile = null;
                referencePrinter = null;
                template.Clear();
                UpdateDestinationHint();
                convert.Enabled = false;
            }
            if (result != DialogResult.OK || dialog.Selection is not DestinationProfile selection)
                return;

            destinationProfile = selection;
            referencePrinter = null;
            template.Text = selection.ToString();
            UpdateDestinationHint();
            convert.Enabled = process.SelectedItem is SourceProcess;
        }
        catch (Exception error)
        {
            ShowError(T("Destination profile", "Profilo di destinazione"), error.Message);
        }
    }

    private void ShowReport(string title, string message, bool error = false)
    {
        reportIsError = error;
        status.Text = title;
        ApplyStatusColors();
        report.Text = message;
        report.Select(0, 0);
        report.ScrollToCaret();
    }

    private void ApplyStatusColors()
    {
        status.ForeColor = reportIsError ? ConversionDetailsForm.ErrorColors.Ink : Accent;
        status.BackColor = reportIsError ? ConversionDetailsForm.ErrorColors.Band : AccentLight;
    }

    private void ShowError(string context, string message)
    {
        SetDetails(ConversionDetails.Failed(context, message));
        ShowReport(T("Error", "Errore"), message, error: true);
    }

    private void SetDetails(ConversionDetails? details)
    {
        conversionDetails = details;
        detailsButton.Enabled = details is not null;
        var (ink, fill, description) = details switch
        {
            { Errors.Count: > 0 } => (ConversionDetailsForm.ErrorColors.Ink, ConversionDetailsForm.ErrorColors.Band, T("Errors found", "Sono presenti errori")),
            { Warnings.Count: > 0 } => (ConversionDetailsForm.WarningColors.Ink, ConversionDetailsForm.WarningColors.Band, T("Warnings to review", "Sono presenti avvisi da verificare")),
            not null => (ConversionDetailsForm.SuccessColors.Ink, ConversionDetailsForm.SuccessColors.Band, T("Completed without warnings", "Operazione riuscita senza avvisi")),
            _ => (Accent, AccentLight, T("Awaiting result", "In attesa di un esito"))
        };
        detailsButton.SetColors(fill, ink);
        detailsButton.AccessibleDescription = description;
    }

    private async void ConvertProject(object? sender, EventArgs args)
    {
        string sourcePath = source.Text;
        string templatePath = template.Text;
        DestinationProfile? selectedProfile = destinationProfile;
        string? preset = ((SourceProcess)process.SelectedItem!).Entry;
        using var save = new SaveFileDialog
        {
            Title = T("Save the translated project", "Salva il progetto tradotto"), Filter = T("3MF project (*.3mf)|*.3mf", "Progetto 3MF (*.3mf)|*.3mf"),
            DefaultExt = "3mf", FileName = Path.GetFileNameWithoutExtension(sourcePath) + "-translated.3mf",
            InitialDirectory = Path.GetDirectoryName(sourcePath), OverwritePrompt = false
        };
        if (save.ShowDialog(this) != DialogResult.OK)
            return;

        string outputPath = save.FileName;
        ReviewSettings review = differences => Invoke(() => ChooseSettings(differences)) ?? throw new OperationCanceledException();
        SetBusy(true);
        SetDetails(null);
        ShowReport(T("In progress", "In corso"), T("Converting…", "Conversione in corso…"));
        try
        {
            string reference = selectedProfile?.ReferencePath ?? templatePath;
            ConversionResult result = await Task.Run(() =>
                ProjectConverter.Convert(sourcePath, reference, outputPath, preset, review, selectedProfile?.Preset,
                    (slots, colors, capacity) => Invoke(() =>
                    {
                        using var mapping = new FilamentMappingForm(slots, colors, capacity) { Icon = Icon };
                        return mapping.ShowDialog(this) == DialogResult.OK ? mapping.Destinations : throw new OperationCanceledException();
                    }), invalidSlots => Invoke(() =>
                    {
                        using var recovery = new ColorRecoveryForm(invalidSlots) { Icon = Icon };
                        return recovery.ShowDialog(this) == DialogResult.OK;
                    }), largeModels => Invoke(() =>
                    {
                        using var recovery = new LargeModelForm(largeModels) { Icon = Icon };
                        return recovery.ShowDialog(this) == DialogResult.OK ? recovery.SelectedLimit : (long?)null;
                    })));
            SetDetails(ConversionDetails.Completed(outputPath, result));
            ShowReport(T("File saved", "File salvato"), T($"Project saved to {outputPath}{Environment.NewLine}{Environment.NewLine}", $"Progetto salvato in {outputPath}{Environment.NewLine}{Environment.NewLine}") + Program.FormatReport(result));
        }
        catch (OperationCanceledException)
        {
            ShowReport(T("Cancelled", "Annullata"), T("Conversion cancelled. No project was saved.",
                "Conversione annullata. Nessun progetto è stato salvato."));
        }
        catch (Exception error)
        {
            ShowError(Path.GetFileName(outputPath), T("Conversion stopped. ", "Conversione interrotta. ") + error.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private IReadOnlyDictionary<string, int>? ChooseSettings(IReadOnlyList<SettingDifference> differences)
    {
        using var choices = new SettingChoiceForm { Icon = Icon };
        if (choices.ShowDialog(this) != DialogResult.OK)
            return null;

        return choices.Selection switch
        {
            SettingChoice.Source => differences.ToDictionary(item => item.Key, item => item.DefaultIndex, StringComparer.Ordinal),
            SettingChoice.Destination => differences.ToDictionary(item => item.Key, _ => SettingDifference.DestinationIndex, StringComparer.Ordinal),
            _ => ShowSettingReview(differences)
        };
    }

    private IReadOnlyDictionary<string, int>? ShowSettingReview(IReadOnlyList<SettingDifference> differences)
    {
        using var dialog = new SettingReviewForm(differences) { Icon = Icon };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Selections : null;
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        sourceBrowse.Enabled = !busy;
        templateBrowse.Enabled = !busy;
        profileBrowse.Enabled = !busy;
        createProfile.Enabled = !busy;
        language.Enabled = !busy;
        theme.Enabled = !busy;
        help.Enabled = !busy;
        process.Enabled = !busy && process.SelectedItem is SourceProcess;
        convert.Enabled = process.Enabled && template.TextLength > 0;
    }

    protected override void OnHandleCreated(EventArgs args)
    {
        base.OnHandleCreated(args);
        ApplyTitleBar(Handle);
    }

    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        base.OnFormClosing(args);
        if (args.CloseReason == CloseReason.UserClosing && UseWaitCursor)
            args.Cancel = true;
    }
}
